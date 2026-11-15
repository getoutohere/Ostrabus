using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;

namespace ShipExecutivePrototype;

// Offline world coordinator outside the native Update prefix chain. Rendering
// and physical scene bodies have fixtures; integrated mode runs real retained
// orbital/economic scopes, stock algorithms and unrelated native AI dispatch.
sealed class NativeWorldTimeBridge : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.navy.world-time");
    internal static NativeWorldTimeBridge Current;
    internal readonly NativeNavyActor Actor;
    bool advancing;
    double segment;
    internal int Segments, NativeDispatches, SuppressedCatchups;
    internal bool InSffCaller, InPassCaller;
    internal bool RejectedCombatSkip;
    Exception failure;
    internal readonly List<string> EntryCalls = new List<string>();
    internal readonly bool IntegratedServices;
    internal Action BeforeSegment;
    internal Action<Ship> BeforePhysicalShip;

    internal NativeWorldTimeBridge(NativeNavyActor actor, bool integratedServices = false)
    {
        Current = this;
        Actor = actor;
        IntegratedServices = integratedServices;
        var world = actor.World;
        NativeWorld.Set(CrewSim.system, "_dockingGroups", new List<Ship>());
        NativeWorld.Set(CrewSim.system, "_updatedGroups", new HashSet<string>());
        NativeWorld.Set(CrewSim.system, "temp_aDestroyed", new List<Ship>());
        NativeWorld.Set(CrewSim.system, "_asteroidClaimCheck", true);
        NativeWorld.Set(CrewSim.system, "dictShipOwners", new Dictionary<string, string>());
        var stellar = AccessTools.Field(typeof(StarSystem), "dictStellarObjects");
        stellar.SetValue(CrewSim.system, Activator.CreateInstance(stellar.FieldType));
        AIShipManager.IsTestCase = true;
        CollisionManager.strATCClosest = AIShipManager.strATCLast;
        // Never replace this readonly static after native callers were JITed:
        // Mono may retain the original reference in compiled code. Reset the
        // actual native queue through its supported API instead.
        ((AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null)).Clear();
        hooks.Patch(AccessTools.Method(typeof(StarSystem), "UpdateShip"), transpiler: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(PhysicalSceneBody)));
        Add(typeof(StarSystem), "DeliverMessages", nameof(Quiet));
        Add(typeof(StarSystem), "LedgerAlert", nameof(Quiet));
        foreach (string name in new[] { "Skip", "ProcessRepeating" })
            Add(typeof(Ledger), name, nameof(Quiet));
        Add(typeof(AIShip), "RunAI", nameof(Dispatch));
        Add(typeof(AIShip), "FFWD", nameof(Catchup), priority: Priority.First);
        Add(typeof(AIShipManager), "CheckTracking", nameof(Quiet));
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(AIShipManager)).Where(m => m.Name == "GetShipsOfTypeForRegion"))
            hooks.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(Actors)));
        foreach (string name in new[] { "CheckProjectileCollisions", "CheckCollisions", "RunQueue" })
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CollisionManager)).Where(m => m.Name == name))
                hooks.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(Quiet)));
        Add(typeof(WeaponsSystem), "ActivateDefenseSystems", nameof(Quiet));
        if (!integratedServices)
            Add(typeof(MarketManager), "Run", nameof(Quiet));
        Add(typeof(CrewSim), "GetSelectedCrew", nameof(Selected));
        CutAfterWorldUpdate(typeof(GUIFFWD), "FFWD");
        CutAfterWorldUpdate(typeof(CrewSim), "_TeleportCO");
    }

    void Add(Type type, string method, string prefix, int priority = Priority.Normal)
    {
        var target = AccessTools.Method(type, method) ?? throw new InvalidOperationException("Missing fixture boundary " + type.Name + "." + method);
        hooks.Patch(target, prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), prefix) { priority = priority });
    }

    void CutAfterWorldUpdate(Type owner, string name)
    {
        var method = AccessTools.Method(owner, name);
        var iterator = method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType
            ?? owner.GetNestedTypes(BindingFlags.NonPublic).Single(t => t.Name.StartsWith("<" + name + ">d__", StringComparison.Ordinal));
        hooks.Patch(AccessTools.Method(iterator, "MoveNext"), transpiler: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(AfterUpdateCut)));
    }

    static IEnumerable<CodeInstruction> AfterUpdateCut(IEnumerable<CodeInstruction> input)
    {
        int count = 0;
        var update = AccessTools.Method(typeof(StarSystem), "Update");
        foreach (var instruction in input)
        {
            if (!instruction.Calls(update))
            {
                yield return instruction;
                continue;
            }
            count++;
            // Coordinate outside StarSystem.Update's prefix chain. Running
            // nested world calls inside a prefix lets later void prefixes
            // prepare another scope at the already-advanced epoch.
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(NativeWorldTimeBridge), nameof(Advance));
            yield return instruction;
            // Native caller and pre-update logic execute. The following GUI,
            // loading, crew/scene and report work is outside this fixture.
            yield return new CodeInstruction(OpCodes.Ldc_I4_0);
            yield return new CodeInstruction(OpCodes.Ret);
        }
        if (count != 1)
            throw new InvalidOperationException("Expected one native world call in time-skip caller");
    }

    static void Advance(StarSystem system, double fTimeDelta)
    {
        var context = Current;
        if (double.IsNaN(fTimeDelta) || double.IsInfinity(fTimeDelta) || fTimeDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(fTimeDelta));
        if (context.advancing)
            throw new InvalidOperationException("Nested executive world advance");
        context.advancing = true;
        try
        {
            context.EntryCalls.Add(GUIFFWD.Active ? "GUIFFWD" : context.InPassCaller ? "PASS" : "normal");
            if (fTimeDelta >= 60 && context.Actor.Machine.State.Active?.Kind == "Combat")
            {
                context.RejectedCombatSkip = true;
                throw new NotSupportedException("Bulk combat remains with the native world; no invented replacement outcome");
            }
            double end = StarSystem.fEpoch + fTimeDelta;
            int budget = 0;
            do
            {
                if (++budget > 128)
                    throw new InvalidOperationException("World interval split budget exhausted");
                context.Actor.Tick(0);
                context.BeforeSegment?.Invoke();
                if (fTimeDelta >= 60 && context.Actor.Machine.State.Active?.Kind == "Combat")
                {
                    context.RejectedCombatSkip = true;
                    throw new NotSupportedException("Combat began at the interval boundary; bulk integration is not qualified");
                }
                var route = context.Actor.World.Subject.objSS.NavData;
                double boundary = route == null ? end : Math.Min(end, route.GetArrivalEpoch());
                if (context.IntegratedServices && (GUIFFWD.Active || context.InPassCaller))
                    boundary = Math.Min(boundary, StarSystem.fEpoch + 86400);
                if (context.Actor.Machine.State.PendingArrival != null)
                    throw new InvalidOperationException("Resolve pending world effect before bulk advancement");
                if (boundary <= StarSystem.fEpoch && end > StarSystem.fEpoch)
                    throw new InvalidOperationException("No actor progress at route boundary");
                context.segment = boundary - StarSystem.fEpoch;
                try
                {
                    using (var scopes = RetainedWorldServices.Current?.EnterSharedWindow(system, context.segment, context.InPassCaller))
                        system.Update(context.segment);
                    context.Segments++;
                }
                catch (Exception e) { context.failure = e; throw; }
                context.Actor.Machine.SettleWorldTime(StarSystem.fEpoch);
                // The native queue dispatched the actor. Drive only immediate
                // completion edges so a finished flight cannot leak residual time.
                if (context.Actor.Machine.State.Active?.Phase == "ArrivalReady")
                    context.Actor.Tick(0);
            } while (StarSystem.fEpoch < end);
        }
        catch (Exception e) { context.failure = e; throw; }
        finally { context.advancing = false; }
    }
    static IEnumerable<CodeInstruction> PhysicalSceneBody(IEnumerable<CodeInstruction> _)
    {
        // Replace the unavailable Unity scene body, not its prefix chain. Real
        // freight ownership prefixes must be able to suppress this body.
        return new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeWorldTimeBridge), nameof(ShipStep))), new CodeInstruction(OpCodes.Ret) };
    }
    static void ShipStep(double fTimeDelta, Ship ship)
    {
        if (!ship.HideFromSystem)
        {
            Current.BeforePhysicalShip?.Invoke(ship);
            if (Current.IntegratedServices && ship.objSS.NavData == null && !ship.objSS.bIgnoreGrav && !ship.objSS.bIsBO && !ship.objSS.bBOLocked)
            {
                var args = new object[] { ship.objSS, default(UnityEngine.Vector2), null };
                AccessTools.Method(typeof(StarSystem), "GetGreatestGravBOFast").Invoke(CrewSim.system, args);
                ship.objSS.vAccEx = (UnityEngine.Vector2)args[1];
            }
            ship.objSS.TimeAdvance(fTimeDelta);
        }
    }
    static bool Dispatch(AIShip __instance)
    {
        if (!ReferenceEquals(__instance, Current.Actor.World.AI))
            return Current.IntegratedServices;
        if (Current.Actor.Machine.State.Authority == "Native")
            return true;
        Current.NativeDispatches++;
        Current.Actor.Tick((float)Current.segment);
        return false;
    }
    static bool Catchup(AIShip __instance)
    {
        if (!ReferenceEquals(__instance, Current.Actor.World.AI))
            return true;
        if (Current.Actor.Machine.State.Authority == "Native")
            return true;
        Current.SuppressedCatchups++;
        return false;
    }
    static bool Actors(ref List<AIShip> __result)
    {
        __result = Current.IntegratedServices
            ? AIShipManager.dictAIs.Values.SelectMany(a => a).Where(a => a.AICharacter != null && !a.Ship.HideFromSystem && !a.Ship.bDestroyed).ToList()
            : Current.Actor.World.Retired ? new List<AIShip>() : new List<AIShip> { Current.Actor.World.AI };
        return false;
    }
    static bool Selected(ref CondOwner __result)
    {
        __result = CrewSim.coPlayer;
        return false;
    }
    static bool Quiet() => false;

    internal void Normal(double seconds)
    {
        failure = null;
        Advance(CrewSim.system, seconds);
        if (failure != null)
            throw new InvalidOperationException("Native normal world step failed", failure);
    }
    internal void Sff(float seconds)
    {
        failure = null;
        var gui = NativeWorld.Empty<GUIFFWD>();
        NativeWorld.Set(gui, "fTimeDelta", seconds);
        var iterator = (IEnumerator)AccessTools.Method(typeof(GUIFFWD), "FFWD").Invoke(gui, null);
        InSffCaller = true;
        try
        {
            if (!iterator.MoveNext())
                throw new InvalidOperationException("SFF missed scheduler boundary");
            iterator.MoveNext();
            if (failure != null)
                throw new InvalidOperationException("Native SFF world step failed", failure);
        }
        finally { InSffCaller = false; AccessTools.Field(typeof(GUIFFWD), "bFFWDActive").SetValue(null, false); }
    }
    internal void Pass(double seconds)
    {
        failure = null;
        var passenger = NativeWorld.Empty<CondOwner>();
        passenger.ship = CrewSim.coPlayer.ship;
        var crew = NativeWorld.Empty<CrewSim>();
        var iterator = (IEnumerator)AccessTools.Method(typeof(CrewSim), "_TeleportCO").Invoke(crew, new object[] { passenger, Actor.World.Home, seconds });
        InPassCaller = true;
        try
        {
            iterator.MoveNext();
            if (failure != null)
                throw new InvalidOperationException("Native passenger world step failed", failure);
        }
        finally { InPassCaller = false; }
    }
    public void Dispose()
    {
        hooks.UnpatchSelf();
        AIShipManager.IsTestCase = false;
    }
}
