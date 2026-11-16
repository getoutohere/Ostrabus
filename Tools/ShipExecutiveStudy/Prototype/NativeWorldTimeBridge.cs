using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;

namespace ShipExecutivePrototype;

// Production SffAdvance is the sole jump coordinator. The fixture preserves
// the native/production world call and cuts only its unavailable Unity tail.
sealed class NativeWorldTimeBridge : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.navy.world-time");
    internal static NativeWorldTimeBridge Current;
    internal readonly NativeNavyActor Actor;
    readonly WorldServiceFixtures ownedData;
    readonly RetainedWorldServices ownedServices;
    readonly CrewSim previousCrew;
    bool updating;
    double segment;
    Exception failure;
    internal int Segments, NativeDispatches, SuppressedCatchups, PendingTurnDrains;
    internal bool InSffCaller, InPassCaller, RejectedCombatSkip;
    internal readonly List<string> EntryCalls = new List<string>();
    internal Action BeforeSegment;
    internal Action<Ship> BeforePhysicalShip;

    internal NativeWorldTimeBridge(NativeNavyActor actor, bool integratedServices = false)
    {
        Current = this;
        Actor = actor;
        previousCrew = CrewSim.objInstance;
        try
        {
            if (!integratedServices)
            {
                // Ability tests use the same coordinator/providers while retaining
                // their original geometry and explicitly zero fixture-body mass.
                ownedData = new WorldServiceFixtures(actor.World, false, true);
                ownedServices = new RetainedWorldServices(true);
            }
            CrewSim.objInstance = NativeWorld.Empty<CrewSim>();
            CrewSim.objInstance.workManager = NativeWorld.Empty<WorkManager>();
            Add(typeof(WorkManager), nameof(WorkManager.CancelAllClaims), nameof(Quiet));
            Add(typeof(Condowner.Utils.Scheduler), nameof(Condowner.Utils.Scheduler.CompletePendingEndTurns), nameof(Draining));
            NativeWorld.Set(CrewSim.system, "_dockingGroups", new List<Ship>());
            NativeWorld.Set(CrewSim.system, "_updatedGroups", new HashSet<string>());
            NativeWorld.Set(CrewSim.system, "temp_aDestroyed", new List<Ship>());
            NativeWorld.Set(CrewSim.system, "_asteroidClaimCheck", true);
            NativeWorld.Set(CrewSim.system, "dictShipOwners", new Dictionary<string, string>());
            AIShipManager.IsTestCase = true;
            CollisionManager.strATCClosest = AIShipManager.strATCLast;
            ((AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null)).Clear();
            hooks.Patch(AccessTools.Method(typeof(StarSystem), "Update"),
                prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(BeforeUpdate)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(AfterUpdate)) { priority = Priority.Last });
            hooks.Patch(AccessTools.Method(typeof(StarSystem), "UpdateShip"), transpiler: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(PhysicalSceneBody)));
            foreach (string name in new[] { "DeliverMessages", "LedgerAlert" })
                Add(typeof(StarSystem), name, nameof(Quiet));
            foreach (string name in new[] { "Skip", "ProcessRepeating" })
                Add(typeof(Ledger), name, nameof(Quiet));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(BeatManager)).Where(m => m.Name == "RunEncounter"))
                hooks.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(Quiet)));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CrewSim)).Where(m => m.Name == "UnlockAchievement"))
                hooks.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(Quiet)));
            Add(typeof(AIShip), "RunAI", nameof(Dispatch));
            Add(typeof(AIShip), "FFWD", nameof(Catchup), Priority.First);
            Add(typeof(AIShipManager), "CheckTracking", nameof(Quiet));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(AIShipManager)).Where(m => m.Name == "GetShipsOfTypeForRegion"))
                hooks.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(Actors)));
            foreach (string name in new[] { "CheckProjectileCollisions", "CheckCollisions", "RunQueue" })
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(CollisionManager)).Where(m => m.Name == name))
                    hooks.Patch(method, prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(Quiet)));
            Add(typeof(WeaponsSystem), "ActivateDefenseSystems", nameof(Quiet));
            Add(typeof(CrewSim), "GetSelectedCrew", nameof(Selected));
            CutAfterWorldUpdate(typeof(GUIFFWD), "FFWD");
            CutAfterWorldUpdate(typeof(CrewSim), "_TeleportCO");
        }
        catch { Dispose(); throw; }
    }

    void Add(Type type, string method, string prefix, int priority = Priority.Normal)
        => hooks.Patch(AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method),
            prefix: new HarmonyMethod(typeof(NativeWorldTimeBridge), prefix) { priority = priority });

    void CutAfterWorldUpdate(Type owner, string name)
        => hooks.Patch(AccessTools.EnumeratorMoveNext(AccessTools.Method(owner, name)),
            transpiler: new HarmonyMethod(typeof(NativeWorldTimeBridge), nameof(AfterUpdateCut))
            {
                priority = Priority.Last,
                after = new[] { "offline.executive-world.retained" }
            });

    static IEnumerable<CodeInstruction> AfterUpdateCut(IEnumerable<CodeInstruction> input)
    {
        int count = 0;
        foreach (var instruction in input)
        {
            yield return instruction;
            var call = instruction.operand as MethodInfo;
            if (call != AccessTools.Method(typeof(StarSystem), "Update") &&
                !(call?.DeclaringType?.FullName == "Ostrabus.Orbital.SffAdvance" && (call.Name == "Update" || call.Name == "Pass")))
                continue;
            count++;
            yield return new CodeInstruction(OpCodes.Ldc_I4_0);
            yield return new CodeInstruction(OpCodes.Ret);
        }
        if (count != 1)
            throw new InvalidOperationException("Expected one shared GUI/PASS world call before excluded scene tail");
    }

    void CheckBulk(double seconds)
    {
        if (seconds >= 60 && Actor.Machine.State.Active?.Kind == "Combat")
        {
            RejectedCombatSkip = true;
            throw new NotSupportedException("Bulk combat remains unqualified; no replacement outcome invented");
        }
    }

    static void BeforeUpdate(double fTimeDelta)
    {
        var context = Current;
        if (context.failure != null)
            throw new InvalidOperationException("Previous native segment failed", context.failure);
        if (context.updating)
            throw new InvalidOperationException("Nested executive world update");
        if (double.IsNaN(fTimeDelta) || double.IsInfinity(fTimeDelta) || fTimeDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(fTimeDelta));
        context.CheckBulk(fTimeDelta);
        context.Actor.Tick(0);
        context.CheckBulk(fTimeDelta);
        if (context.Actor.Machine.State.PendingArrival != null)
            throw new InvalidOperationException("Resolve pending world effect before advancement");
        context.BeforeSegment?.Invoke();
        context.segment = fTimeDelta;
        context.updating = true;
        context.EntryCalls.Add(GUIFFWD.Active ? "GUIFFWD" : context.InPassCaller ? "PASS" : "normal");
    }

    static Exception AfterUpdate(Exception __exception)
    {
        var context = Current;
        context.updating = false;
        if (__exception != null)
            context.failure = __exception;
        if (context.failure != null)
            return context.failure;
        context.Segments++;
        context.Actor.Machine.SettleWorldTime(StarSystem.fEpoch);
        try
        {
            if (context.Actor.Machine.State.Active?.Phase == "ArrivalReady")
                context.Actor.Tick(0);
        }
        catch (Exception error) { context.failure = error; return error; }
        return null;
    }

    static IEnumerable<CodeInstruction> PhysicalSceneBody(IEnumerable<CodeInstruction> _)
        => new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeWorldTimeBridge), nameof(ShipStep))), new CodeInstruction(OpCodes.Ret) };

    static void ShipStep(double fTimeDelta, Ship ship)
    {
        try
        {
            if (ship.HideFromSystem)
                return;
            Current.BeforePhysicalShip?.Invoke(ship);
            if (ship.objSS.NavData == null && !ship.objSS.bIgnoreGrav && !ship.objSS.bIsBO && !ship.objSS.bBOLocked)
            {
                var args = new object[] { ship.objSS, default(UnityEngine.Vector2), null };
                AccessTools.Method(typeof(StarSystem), "GetGreatestGravBOFast").Invoke(CrewSim.system, args);
                ship.objSS.vAccEx = (UnityEngine.Vector2)args[1];
            }
            ship.objSS.TimeAdvance(fTimeDelta);
        }
        catch (Exception error) { Current.failure = error; throw; }
    }

    static bool Dispatch(AIShip __instance)
    {
        if (!ReferenceEquals(__instance, Current.Actor.World.AI) || Current.Actor.Machine.State.Authority == "Native")
            return true;
        Current.NativeDispatches++;
        try
        {
            Current.Actor.Tick((float)Current.segment);
        }
        catch (Exception error) { Current.failure = error; throw; }
        return false;
    }
    static bool Catchup(AIShip __instance)
    {
        if (!ReferenceEquals(__instance, Current.Actor.World.AI) || Current.Actor.Machine.State.Authority == "Native")
            return true;
        Current.SuppressedCatchups++;
        return false;
    }
    static bool Actors(ref List<AIShip> __result)
    {
        __result = AIShipManager.dictAIs.Values.SelectMany(a => a).Where(a => a.AICharacter != null && !a.Ship.HideFromSystem && !a.Ship.bDestroyed).ToList();
        return false;
    }
    static bool Selected(ref CondOwner __result)
    {
        __result = CrewSim.coPlayer;
        return false;
    }
    static bool Quiet() => false;
    static void Draining() => Current.PendingTurnDrains++;

    internal void Normal(double seconds)
    {
        failure = null;
        CheckBulk(seconds);
        double end = StarSystem.fEpoch + seconds;
        if (double.IsNaN(end) || double.IsInfinity(end) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        int budget = 0;
        while (StarSystem.fEpoch < end)
        {
            if (++budget > 512)
                throw new InvalidOperationException("Normal fixture step budget exhausted");
            Actor.Tick(0);
            double arrival = Actor.World.Subject.objSS.NavData?.GetArrivalEpoch() ?? end;
            double after = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(arrival) + (arrival >= 0 ? 1 : -1));
            double boundary = arrival >= StarSystem.fEpoch && after < end ? after : end;
            CrewSim.system.Update(boundary - StarSystem.fEpoch);
            ThrowFailure();
        }
    }

    internal void Sff(float seconds)
    {
        failure = null;
        CheckBulk(seconds);
        var gui = NativeWorld.Empty<GUIFFWD>();
        NativeWorld.Set(gui, "fTimeDelta", seconds);
        var iterator = (IEnumerator)AccessTools.Method(typeof(GUIFFWD), "FFWD").Invoke(gui, null);
        InSffCaller = true;
        try
        {
            Drain(iterator);
            ThrowFailure();
        }
        finally { InSffCaller = false; AccessTools.Field(typeof(GUIFFWD), "bFFWDActive").SetValue(null, false); }
    }
    internal void Pass(double seconds)
    {
        failure = null;
        CheckBulk(seconds);
        var passenger = NativeWorld.Empty<CondOwner>();
        passenger.ship = CrewSim.coPlayer.ship;
        var iterator = (IEnumerator)AccessTools.Method(typeof(CrewSim), "_TeleportCO").Invoke(CrewSim.objInstance, new object[] { passenger, Actor.World.Home, seconds });
        InPassCaller = true;
        try
        {
            Drain(iterator);
            ThrowFailure();
        }
        finally { InPassCaller = false; }
    }
    static void Drain(IEnumerator iterator)
    {
        int budget = 10000;
        void Run(IEnumerator current)
        {
            try
            {
                while (current.MoveNext())
                {
                    if (--budget == 0)
                        throw new InvalidOperationException("Offline coroutine drain budget exhausted");
                    if (current.Current is IEnumerator nested)
                        Run(nested);
                }
            }
            finally { (current as IDisposable)?.Dispose(); }
        }
        Run(iterator);
    }

    void ThrowFailure()
    {
        if (failure != null)
            throw new InvalidOperationException("Native world segment failed", failure);
    }

    public void Dispose()
    {
        hooks.UnpatchSelf();
        ownedServices?.Dispose();
        ownedData?.Dispose();
        CrewSim.objInstance = previousCrew;
        AIShipManager.IsTestCase = false;
        Current = null;
    }
}
