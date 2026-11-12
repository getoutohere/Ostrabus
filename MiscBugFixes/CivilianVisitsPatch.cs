using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

namespace Ostrabus.MiscBugFixes;

internal static class CivilianVisits {
    internal const string InvalidKey = "Ostrabus.CivilianVisit.Invalid";
    static readonly AccessTools.FieldRef<SelectorCommand,List<ICommand>> Choices = AccessTools.FieldRefAccess<SelectorCommand,List<ICommand>>("_childCommands");
    internal static readonly AccessTools.FieldRef<StickySequenceCommand,int> Phase = AccessTools.FieldRefAccess<StickySequenceCommand,int>("_currentChild");
    static readonly AccessTools.FieldRef<AIShip,ICommand> Active = AccessTools.FieldRefAccess<AIShip,ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip,ICommand> Reaction = AccessTools.FieldRefAccess<AIShip,ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<QueuedCommandsNode,ICommand> Current = AccessTools.FieldRefAccess<QueuedCommandsNode,ICommand>("currentCommand");
    static readonly AccessTools.FieldRef<QueuedCommandsNode,Queue<ICommand>> Queue = AccessTools.FieldRefAccess<QueuedCommandsNode,Queue<ICommand>>("commandQueue");
    static readonly System.Reflection.FieldInfo Trip = AccessTools.Field(typeof(FlyTo), "_tripFuelDTO");

    internal static VisitSequence Sequence(CivilianPilot pilot) {
        if (!(pilot?.BehaviorTree is SelectorCommand root)) return null;
        var choices = Choices(root);
        return choices.Count == 0 ? null : choices[choices.Count - 1] as VisitSequence;
    }
    static bool SpaceHull(Ship ship) => ship != null && !ship.bDestroyed && ship.Classification != Ship.TypeClassification.Projectile &&
        ship.objSS != null && !ship.objSS.bIsBO && !ship.IsStation() && !ship.IsStationHidden();
    static Ship Goal(Ship ship) {
        string id = ship.targetRegID;
        if (string.IsNullOrEmpty(id) && ship.json?.strTargetRegID == ship.json?.strScanTargetID) id = ship.json?.strTargetRegID;
        if (!string.IsNullOrEmpty(id)) return CrewSim.system?.GetShipByRegID(id);
        return ReferenceEquals(ship.shipSituTarget, ship.shipScanTarget?.objSS) ? ship.shipScanTarget : null;
    }
    internal static void Registered(AIShip ai) {
        if (ai?.AICharacter is CivilianPilot pilot) Sequence(pilot)?.Observe(Goal(pilot.ShipUs));
    }
    internal static void Retired(Ship target) {
        if (!SpaceHull(target) || AIShipManager.dictAIs == null) return;
        foreach (var bucket in AIShipManager.dictAIs.Values) foreach (var ai in bucket)
            if (ai?.AICharacter is CivilianPilot pilot) Sequence(pilot)?.Observe(target, ai);
    }
    internal static StickySequenceCommand CreateMain(string name, ICommand[] commands, CivilianPilot pilot) {
        if (name != "Main" || commands.Length != 5 || !(commands[0] is Undock) || !(commands[1] is TargetNewIfNone) ||
            !(commands[2] is FlyTo) || !(commands[3] is Dock) || !(commands[4] is Hold))
            throw new InvalidOperationException("Unknown civilian Main sequence; refusing to change visit semantics.");
        return new VisitSequence(name, commands, pilot);
    }

    // This is the native Main sequence with one validity precondition. Its native
    // children, order, sticky progress, save paths and priority/combat parent stay
    // authoritative. No new controller or per-frame/world polling is installed.
    internal sealed class VisitSequence : StickySequenceCommand {
        readonly CivilianPilot pilot;
        readonly ICommand fly, dock;
        internal VisitSequence(string name, ICommand[] commands, CivilianPilot owner) : base(name, commands) {
            pilot = owner; fly = commands[2]; dock = commands[3];
        }
        bool Approach => Phase(this) >= 1 && Phase(this) <= 3;
        bool OwnLeaf(ICommand command) => ReferenceEquals(command, fly) || ReferenceEquals(command, dock) ||
            command is OneShotWrapperCommand && (ReferenceEquals(command.FindCommand(typeof(FlyTo)), fly) || ReferenceEquals(command.FindCommand(typeof(Dock)), dock));
        bool OtherPriority() {
            var node = pilot.PriorityQueue;
            if (node == null) return false;
            if (Current(node) != null && !OwnLeaf(Current(node))) return true;
            foreach (var command in Queue(node)) if (!OwnLeaf(command)) return true;
            return false;
        }
        internal void Observe(Ship target, AIShip ai = null) {
            Ship ship = pilot.ShipUs;
            if (!Approach || ship == null || ship.bDestroyed || ship.NavPlayerManned || ship.IsDocked() || !SpaceHull(target) ||
                !ReferenceEquals(Goal(ship), target)) return;
            // A null ai is registration/load validation; a non-null ai means an
            // actual hide transition, before the setter changes the flag.
            if (ai == null && !target.HideFromSystem) return;
            pilot.Blackboard.Set(InvalidKey, target.strRegID);
            if (ai != null && Reaction(ai) == null && OwnLeaf(Active(ai)) && !OtherPriority()) Cancel();
        }
        internal bool Validate() {
            Ship ship = pilot.ShipUs;
            if (ship == null || ship.bDestroyed || ship.NavPlayerManned || !Approach) return false;
            string invalid = pilot.Blackboard.Get<string>(InvalidKey);
            var target = Goal(ship);
            if (ship.IsDocked() || !SpaceHull(target) || !target.HideFromSystem && invalid != target.strRegID) {
                if (invalid != null) pilot.Blackboard.Remove(InvalidKey);
                return false;
            }
            Cancel();
            return true;
        }
        void Cancel() {
            Ship ship = pilot.ShipUs;
            OnExit(pilot.Blackboard); // Native sticky sequence restarts at Undock.
            NavigationRoute.Stop(ship);
            pilot.Blackboard.Remove("TripFuelPrediction");
            pilot.Blackboard.Remove(InvalidKey);
            Trip.SetValue(fly, null);
            ship.ClearShipTarget(); // Existing target patch clears saved references.
            var ai = AIShipManager.GetAIShipByRegID(ship.strRegID);
            if (ai != null && OwnLeaf(Active(ai))) Active(ai) = null;
        }
        internal bool BeforeSff(ICommand command) {
            if (!OwnLeaf(command) || OtherPriority()) return false;
            var ai = AIShipManager.GetAIShipByRegID(pilot.ShipUs.strRegID);
            if (ai != null && Reaction(ai) != null) return false;
            return Validate();
        }
        public override CommandStatus RunCommand(Blackboard blackboard) {
            Validate();
            return base.RunCommand(blackboard);
        }
    }
}

[HarmonyPatch(typeof(CivilianPilot), "CanDockWith")]
internal static class CivilianVisibleVisitPatch {
    static bool Prefix(Ship shipTarget, ref bool __result) {
        if (!shipTarget.HideFromSystem) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(CivilianPilot), MethodType.Constructor, new[] { typeof(string), typeof(Ship) })]
internal static class CivilianVisitSequencePatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var ctor = AccessTools.Constructor(typeof(StickySequenceCommand), new[] { typeof(string), typeof(ICommand[]) });
        var create = AccessTools.Method(typeof(CivilianVisits), nameof(CivilianVisits.CreateMain));
        int names = 0, replacements = 0;
        bool main = false;
        foreach (var instruction in code) {
            if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "Main")) { main = true; names++; }
            if (main && instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, ctor)) {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                instruction.MoveLabelsTo(owner); instruction.MoveBlocksTo(owner);
                yield return owner;
                instruction.opcode = OpCodes.Call; instruction.operand = create;
                main = false; replacements++;
            }
            yield return instruction;
        }
        if (names != 1 || replacements != 1) throw new InvalidOperationException("Expected one civilian Main sequence; found " + names + "/" + replacements);
    }
}

[HarmonyPatch(typeof(Ship), nameof(Ship.HideFromSystem), MethodType.Setter)]
internal static class CivilianVisitRetiredPatch {
    static void Prefix(Ship __instance, bool value) {
        if (value && !__instance.HideFromSystem) CivilianVisits.Retired(__instance);
    }
}

[HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.AddAIToDict))]
internal static class CivilianVisitRestoredPatch {
    static void Postfix(AIShip __0) => CivilianVisits.Registered(__0);
}

[HarmonyPatch(typeof(CivilianPilot), nameof(CivilianPilot.FFWD))]
internal static class CivilianVisitSffPatch {
    static bool Prefix(CivilianPilot __instance, ICommand lastActiveCommand, ref ICommand __result) {
        if (CivilianVisits.Sequence(__instance)?.BeforeSff(lastActiveCommand) != true) return true;
        __result = null;
        return false;
    }
}

// Native retirement moves GetPeople(allowDocked:true) and hides the carrier.
// A civilian visit must finish first; ordinary hauler tow delivery is separate.
[HarmonyPatch(typeof(CrewSim), nameof(CrewSim.DockAndDespawn))]
internal static class AttachedRetirementPatch {
    static bool Prefix(Ship shipAI) {
        if (shipAI == null) return true;
        bool civilian = IsCivilian(shipAI);
        foreach (var attached in shipAI.GetAllDockedShips())
            if (attached != null && !attached.bDestroyed && attached.objSS != null && !attached.objSS.bIsBO &&
                !attached.IsStation() && !attached.IsStationHidden() && (civilian || IsCivilian(attached))) return false;
        return true;
    }
    static bool IsCivilian(Ship ship) => AIShipManager.dictAIs != null && AIShipManager.GetAIShipByRegID(ship.strRegID)?.AIType == AIType.Civilian;
}
