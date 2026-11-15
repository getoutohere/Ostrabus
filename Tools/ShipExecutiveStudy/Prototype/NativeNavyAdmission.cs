using System;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

namespace ShipExecutivePrototype;

// Offline transport boundary. Let Comms.ApplyLoot execute its native asset
// parsing, clearance and port branches; only divert the resulting AI command
// for the single actor whose executive currently owns command execution.
sealed class NativeNavyAdmission : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.navy.admission");
    internal static NativeNavyAdmission Current;
    internal NativeNavyActor Actor;
    string counterpart, affectedCharacter;
    bool inComms;
    RawInput raw;
    sealed class RawInput
    {
        internal string Command;
        internal string[] Payload;
        internal bool Admitted;
        internal bool NativeLootProbe;
    }
    sealed class CommsScope
    {
        internal string Counterparty, AffectedCharacter;
        internal bool InComms;
    }
    sealed class LootScope
    {
        internal RawInput Previous;
        internal IDisposable Mutation;
    }

    internal NativeNavyAdmission(NativeNavyActor actor)
    {
        Current = this;
        Actor = actor;
        hooks.Patch(AccessTools.Method(typeof(AIShip), "AddCommandLoot"), prefix: new HarmonyMethod(typeof(NativeNavyAdmission), nameof(Loot)),
            finalizer: new HarmonyMethod(typeof(NativeNavyAdmission), nameof(EndLoot)));
        hooks.Patch(AccessTools.Method(typeof(AIShip), "AddCommand"), prefix: new HarmonyMethod(typeof(NativeNavyAdmission), nameof(Command)));
        hooks.Patch(AccessTools.Method(typeof(AIShip), "AddUrgentCommand"), prefix: new HarmonyMethod(typeof(NativeNavyAdmission), nameof(Urgent)));
        hooks.Patch(AccessTools.Method(typeof(Comms), "ApplyLoot"), prefix: new HarmonyMethod(typeof(NativeNavyAdmission), nameof(EnterComms)),
            finalizer: new HarmonyMethod(typeof(NativeNavyAdmission), nameof(LeaveComms)));
    }

    static bool Owned(AIShip ai) => ReferenceEquals(ai, Current.Actor.World.AI) && Current.Actor.Machine.State.Authority == "Executive";

    static void Loot(AIShip __instance, string commandName, string[] commandSaveData, out LootScope __state)
    {
        __state = new LootScope { Previous = Current.raw };
        if (!Owned(__instance))
            return;
        __state.Mutation = NativeExecutionFence.Mutation();
        if (Current.Actor.State.Inbox.Count >= 16 && (!Current.inComms || DataHandler.GetCommand(commandName) != null))
            throw new InvalidOperationException("Navy inbox is full; native construction was not admitted");
        Current.raw = new RawInput { Command = commandName, Payload = commandSaveData == null ? Array.Empty<string>() : (string[])commandSaveData.Clone(), NativeLootProbe = Current.inComms };
        // Native factory, constructor and payload setter execute at receipt.
        // The resulting AddCommand is redirected below, not the constructor.
    }
    static Exception EndLoot(Exception __exception, LootScope __state)
    {
        var received = Current.raw;
        Current.raw = __state.Previous;
        __state.Mutation?.Dispose();
        // Comms.ApplyLoot also probes ordinary condition-loot names (e.g.
        // TUpAltruism). A null native factory result is intentional there.
        if (__exception == null && !ReferenceEquals(received, __state.Previous) && received != null && !received.Admitted && !received.NativeLootProbe)
            return new NotSupportedException("Native factory could not admit preserved Navy input: " + received.Command);
        return __exception;
    }

    static bool Command(AIShip __instance, ICommand command) => Admit(__instance, command, false);
    static bool Urgent(AIShip __instance, ICommand command) => Admit(__instance, command, true);

    static bool Admit(AIShip ai, ICommand command, bool urgent)
    {
        if (!Owned(ai))
            return true;
        using var operation = NativeExecutionFence.Mutation();
        if (command == null)
            return false;
        bool once = command is OneShotWrapperCommand;
        if (once)
            command = (ICommand)AccessTools.Field(typeof(OneShotWrapperCommand), "_wrappedCommand").GetValue(command);
        if (command == null)
            return false;
        // RaceComms has a write-only payload; never recover it via BaseCommand's
        // unrelated SaveData getter. Raw loot arguments take the path above.
        bool fromLoot = Current.raw?.Command == command.GetType().Name;
        string[] payload = fromLoot ? Current.raw.Payload : command is RaceComms
            ? (string[])AccessTools.Field(typeof(RaceComms), "_payload").GetValue(command)
            : command.SaveData;
        var input = Current.Actor.Enqueue(command.GetType().Name, payload, fromLoot ? "AIShip.AddCommandLoot" : urgent ? "AIShip.AddUrgentCommand" : "AIShip.AddCommand", urgent, once, Current.counterpart, Current.affectedCharacter);
        if (fromLoot)
            Current.raw.Admitted = true;
        NativeCommandLifetime.Capture(command, input, Current.Actor.World.Subject);
        // Dodge target is published AFTER AddUrgentCommand by WeaponsSystem.
        // Defer context read to dispatch in this offline seam.
        return false;
    }

    static void EnterComms(CondOwner co, string regIDThem, out CommsScope __state)
    {
        __state = new CommsScope { Counterparty = Current.counterpart, AffectedCharacter = Current.affectedCharacter, InComms = Current.inComms };
        Current.inComms = true;
        Current.counterpart = regIDThem;
        // ApplyLoot runs on both send and receive. Its co argument is the
        // character receiving these effects, not necessarily the sender.
        Current.affectedCharacter = co?.strID;
    }

    static Exception LeaveComms(Exception __exception, CommsScope __state)
    {
        Current.counterpart = __state.Counterparty;
        Current.affectedCharacter = __state.AffectedCharacter;
        Current.inComms = __state.InComms;
        return __exception;
    }

    public void Dispose() => hooks.UnpatchSelf();
}
