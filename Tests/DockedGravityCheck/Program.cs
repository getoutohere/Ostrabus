using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using SolarSystem;

static class Program {
    static int checks;
    static CondOwner actor;
    static bool failStorage;
    static int writes;
    static readonly Dictionary<string, double> stats = new Dictionary<string, double>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool Read(string strName, ref double __result) { stats.TryGetValue(strName, out __result); return false; }
    static bool Add(string strName, double fAmount) {
        if (failStorage) throw new InvalidOperationException("fixture condition storage failure");
        writes++; stats.TryGetValue(strName, out double before); stats[strName] = before + fAmount; return false;
    }
    static bool Person(ref CondOwner __result) { __result = actor; return false; }
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static int Main() {
        var h = new Harmony("offline.dockedgravity");
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        // Keep native group gravity propagation and CO.UpdateGravity. Substitute
        // orbital integration, lift/mass enumeration and scalar condition storage.
        h.Patch(AccessTools.Method(typeof(DockGroup), "CalculateAverageLiftDrag"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(ShipSitu), "TimeAdvance", new[] { typeof(double), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "GetCondAmount", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Read)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(Add)));
        var ship = Empty<Ship>();
        Field(ship, "strRegID", "DERELICT"); Field(ship, "fGravity", 0.3d);
        Field(ship, "nLoadState", Ship.Loaded.Full); Field(ship, "aPeople", new List<PersonSpec>());
        Field(ship, "<objSS>k__BackingField", Empty<ShipSitu>());
        var group = new DockGroup { Anchor = Empty<ShipSitu>() };
        group.Anchor.bBOLocked = true;
        group.DockedShips.Add(new DockGroupMember(ship, false)); ship.objSS._dockGroup = group;
        var crew = Empty<CondOwner>(); Field(crew, "_objShip", ship); Field(crew, "_isHumanOrRobot", true);
        stats["StatMass"] = 101.5; stats["StatGrav"] = 0;
        group.TimeAdvance(1);
        Check(ship.Gravity == 0.3, "Locked group unexpectedly corrected constructor-default gravity");
        crew.UpdateGravity();
        Check(Math.Abs(stats["StatEncumbrance"] - 30.45) < 0.000001, "Did not reproduce observed 30.45 encumbrance");
        Check(stats["StatGrav"] == 0, "UpdateGravity changed display-gravity condition");
        Console.WriteLine("Locked dock group: Ship.Gravity=" + ship.Gravity + "; encumbrance=" + stats["StatEncumbrance"] + "; StatGrav=" + stats["StatGrav"]);
        group.Anchor.bBOLocked = false;
        group.TimeAdvance(1);
        crew.UpdateGravity();
        Check(ship.Gravity == 0 && stats["StatEncumbrance"] == 0, "Free group did not restore native zero-acceleration gravity");
        Console.WriteLine("Identical free dock group: Ship.Gravity=" + ship.Gravity + "; encumbrance=" + stats["StatEncumbrance"]);

        // Test production synchronization with the real native Gravity setter,
        // including its crew updates and wear suppression during initialization.
        h.Patch(AccessTools.Method(typeof(PersonSpec), "GetCO", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(Program), nameof(Person)));
        actor = crew; ship.People.Add(Empty<PersonSpec>()); stats["IsHuman"] = 1;
        var source = Empty<Ship>(); Field(source, "<objSS>k__BackingField", Empty<ShipSitu>());
        Field(source, "nLoadState", Ship.Loaded.Shallow);
        source.objSS.bBOLocked = true; source.objSS._dockGroup = group;
        group.DockedShips.Add(new DockGroupMember(source, false)); group.Anchor.bBOLocked = true;
        foreach (double gravity in new[] { 0d, 0.000001d, 0.165d, 1d, 1.2d }) {
            Field(source, "fGravity", gravity); Field(ship, "fGravity", 0.3d);
            Field(ship, "fGravAmountLast", 0.3d); Field(ship, "fWearManeuver", 35f);
            stats["StatEncumbrance"] = 30.45; stats["StatGrav"] = 0;
            Ostrabus.MiscBugFixes.DockedGravityPatch.Synchronize(source);
            Check(ship.Gravity == gravity, "Attached materialized ship did not inherit native gravity");
            Check(Math.Abs(stats["StatEncumbrance"] - 101.5 * gravity) < 0.000001 && stats["StatGrav"] == gravity, "Crew gravity/encumbrance not refreshed");
            Check((float)AccessTools.Field(typeof(Ship), "fWearManeuver").GetValue(ship) == 35f, "Correction changed maneuver wear");
            Check(!ship.bNeedsGravSet, "Correction leaked temporary initialization flag");
            int count = writes;
            Ostrabus.MiscBugFixes.DockedGravityPatch.Synchronize(source);
            Check(writes == count, "Stable gravity unnecessarily reapplied crew conditions");
        }
        Field(source, "fGravity", 0d);
        foreach (var mode in new[] { "free-group", "free-source", "anchored-member", "shallow", "destroyed", "detached", "mooring" }) {
            Field(ship, "fGravity", 0.3d);
            if (mode == "free-group") group.Anchor.bBOLocked = false;
            if (mode == "free-source") source.objSS.bBOLocked = false;
            if (mode == "anchored-member") ship.objSS.bBOLocked = true;
            if (mode == "shallow") Field(ship, "nLoadState", Ship.Loaded.Shallow);
            if (mode == "destroyed") ship.bDestroyed = true;
            if (mode == "detached") ship.objSS._dockGroup = new DockGroup();
            if (mode == "mooring") group.DockedShips[0] = new DockGroupMember(ship, true);
            Ostrabus.MiscBugFixes.DockedGravityPatch.Synchronize(source);
            Check(ship.Gravity == 0.3d, "Out-of-scope member modified: " + mode);
            group.Anchor.bBOLocked = source.objSS.bBOLocked = true; ship.objSS.bBOLocked = false;
            Field(ship, "nLoadState", Ship.Loaded.Full); ship.bDestroyed = false; ship.objSS._dockGroup = group;
            group.DockedShips[0] = new DockGroupMember(ship, false);
        }
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity }) {
            Field(source, "fGravity", invalid); Field(ship, "fGravity", 0.3d);
            Ostrabus.MiscBugFixes.DockedGravityPatch.Synchronize(source);
            Check(ship.Gravity == 0.3d, "Invalid gravity propagated");
        }
        Field(source, "fGravity", 0d);
        ship.bNeedsGravSet = true;
        Ostrabus.MiscBugFixes.DockedGravityPatch.Synchronize(source);
        Check(ship.bNeedsGravSet && ship.Gravity == 0, "Pre-existing initialization flag lost");
        ship.bNeedsGravSet = false; Field(ship, "fGravity", 0.3d);
        failStorage = true;
        bool threw = false;
        try { Ostrabus.MiscBugFixes.DockedGravityPatch.Synchronize(source); }
        catch (InvalidOperationException) { threw = true; }
        finally { failStorage = false; }
        Check(threw && !ship.bNeedsGravSet, "Failure swallowed or initialization flag leaked");
        h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.DockedGravityPatch)).Patch();
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(DockGroup), nameof(DockGroup.TimeAdvance))).Postfixes.Count == 1, "Native group hook did not install");
        Field(ship, "fGravity", 0.3d); Field(source, "fGravity", 0d);
        group.TimeAdvance(0);
        Check(ship.Gravity == 0.3d, "Zero-time group refresh altered gravity");
        group.TimeAdvance(1);
        Check(ship.Gravity == 0 && stats["StatEncumbrance"] == 0 && stats["StatGrav"] == 0, "Patched native anchored group did not repair gravity and crew");
        group.Anchor.bBOLocked = false;
        group.Anchor.bIsBO = source.objSS.bIsBO = true;
        source.objSS.bBOLocked = false;
        Field(source, "fGravity", 1d); Field(ship, "fGravity", 0.3d);
        group.TimeAdvance(1);
        Check(ship.Gravity == 1 && stats["StatEncumbrance"] == 101.5 && stats["StatGrav"] == 1, "Native body-anchored station gravity not inherited");
        group.Anchor.bIsBO = source.objSS.bIsBO = false;
        group.Anchor.bOrbitLocked = source.objSS.bOrbitLocked = true;
        Field(source, "fGravity", 0d); Field(ship, "fGravity", 0.3d);
        group.TimeAdvance(1);
        Check(ship.Gravity == 0 && stats["StatEncumbrance"] == 0, "Orbit-locked group did not inherit native gravity");
        Field(ship, "fGravity", 0.3d); source.bNeedsGravSet = true;
        group.TimeAdvance(1);
        Check(ship.Gravity == 0.3d, "Uninitialized anchored source propagated its default");
        h.UnpatchSelf();
        Console.WriteLine(checks + " native docked-gravity diagnosis/correction checks passed.");
        return 0;
    }
}
