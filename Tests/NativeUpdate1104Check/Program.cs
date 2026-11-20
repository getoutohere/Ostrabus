using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using Condowner.Utils;
using HarmonyLib;

// Executes native reply creation/expiry and the safe-atmosphere getter.
// Isolated clocks avoid the scheduler; Unity identity and selection are fixtures.
static class Program {
    static int checks;
    static void Check(bool value, string message) {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) {
        __result = ReferenceEquals(__0, __1); return false;
    }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) {
        __result = !ReferenceEquals(__0, __1); return false;
    }
    static bool Selected(ref CondOwner __result) { __result = null; return false; }
    static CondOwner Actor(string id, double epoch) {
        var actor = Empty<CondOwner>();
        Field(actor, "_strID", id);
        actor.aReplies = new List<ReplyThread>();
        actor.bAlive = true;
        actor.mapConds = new Dictionary<string, Condition>();
        DataHandler.mapCOs[id] = actor;
        actor.Clock = Empty<Clock>();
        Field(actor.Clock, "_isDisposed", true);
        Field(actor.Clock, "_epochAtAnchor", epoch);
        return actor;
    }
    static void Main(string[] args) {
        bool legacy = args.Single() == "legacy";
        string expected = legacy ? "e9e7b8796423701c157ea5718cb407e6517ac0435ef947062c9a27464f4bc82f" :
            "904c72870f55124bbe3f9ee924a4a650279c362541b5795174d0cf940b97c474";
        using (var hash = SHA256.Create())
            Check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(typeof(CondOwner).Assembly.Location))).Replace("-", "").ToLowerInvariant() == expected,
                "Loaded the wrong native assembly for this A/B control");
        var fixture = new Harmony("ostrabus.tests.native-update-1104");
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew)), prefix: new HarmonyMethod(typeof(Program), nameof(Selected)));
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            var add = AccessTools.Method(typeof(CondOwner), "AddReplyThread");
            var cleanup = AccessTools.Method(typeof(CondOwner), "CleanupReplies");
            Check(add.GetParameters().Length == (legacy ? 3 : 2), "Reply API signature changed unexpectedly");
            foreach (double senderEpoch in new[] { 100d, 200d, 300d }) {
                var sender = Actor("sender", senderEpoch);
                var recipient = Actor("recipient", 200);
                var interaction = new Interaction { strName = "fixture-reply", objUs = sender, objThem = recipient };
                void Send() => add.Invoke(recipient, legacy ? new object[] { sender.Clock.Epoch, sender.strID, interaction } :
                    new object[] { sender.strID, interaction });
                Send();
                Check(recipient.aReplies.Count == 1 && recipient.aReplies[0].fEpoch == (legacy ? senderEpoch : 200), "Wrong reply timestamp");
                Send();
                Check(recipient.aReplies.Count == 1, "Repeated reply must replace its earlier thread");
                cleanup.Invoke(recipient, new object[] { true });
                bool expired = legacy && senderEpoch < 170;
                Check(recipient.aReplies.Count == (expired ? 0 : 1), "Immediate reply expiry does not match clock provenance");
                if (expired) continue;
                // Both versions should retain the exact native 30-second lifetime.
                double stamped = recipient.aReplies[0].fEpoch;
                Field(recipient.Clock, "_epochAtAnchor", stamped + 30);
                cleanup.Invoke(recipient, new object[] { true });
                Check(recipient.aReplies.Count == 1, "Reply expired before the native timeout");
                Field(recipient.Clock, "_epochAtAnchor", stamped + 31);
                cleanup.Invoke(recipient, new object[] { true });
                Check(recipient.aReplies.Count == 0, "Reply never expired");
            }
            var safe = (JsonAtmosphere)AccessTools.Property(typeof(GasContainer), "AtmoSafe").GetValue(null);
            Check(safe.fO2 == (legacy ? 20 : 22) && safe.fN2 == 80 && safe.fTemp == 297, "Native safe-atmosphere preset changed");
            Console.WriteLine(checks + " native reply-clock and atmosphere checks passed; " + (legacy ? "1.1.0.3 reproduction" : "1.1.0.4 control") + ". No production patch applied.");
        } finally { fixture.UnpatchSelf(); }
    }
}
