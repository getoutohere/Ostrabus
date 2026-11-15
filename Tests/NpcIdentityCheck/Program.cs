using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;
using UnityEngine;

static partial class Program {
    const string Name = "Tomiwa Trejo";
    static CondOwner existing;
    static bool stopReuse;
    static int checks, forbiddenSelections;
    sealed class ReusedExisting : Exception { }
    sealed class RehydratedExisting : Exception { }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object value, string name, object data) => AccessTools.Field(value.GetType(), name).SetValue(value, data);
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    // Deterministically select the collision which historical data indicates.
    // This substitutes the name RNG, not name ownership/selection checks.
    static bool RollName(string strGender, out string strFirstName, out string strLastName) {
        nameRolls++; lastNameGender = strGender;
        if (nextNames != null) {
            if (nextNames.Count == 0) throw new InvalidOperationException("Unexpected extra native name draw.");
            (strFirstName, strLastName) = nextNames.Dequeue();
        } else { strFirstName = "Tomiwa"; strLastName = "Trejo"; }
        return false;
    }
    static bool Embellish(string strName, ref string __result) { __result = strName + " Distinct"; return false; }
    static bool NoVolunteers(List<string> aForbids, ref PersonSpec __result) {
        if (eligibleVolunteer != null) { Check(!aForbids.Contains(Name), "Eligible station recruit was forbidden."); __result = eligibleVolunteer; return false; }
        Check(aForbids.Contains(Name), "Recruiter did not forbid the currently serving pilot.");
        forbiddenSelections++; __result = null; return false;
    }
    static bool Underpopulated(ref bool __result) { __result = eligibleVolunteer == null; return false; }
    static bool Homeworld(ref string __result) { __result = "HW_BCRS"; return false; }
    static bool IgnoreLog() => false;
    static void FactoryReturn(CondOwner __result) {
        if (stopReuse && ReferenceEquals(__result, existing)) throw new ReusedExisting();
    }
    static void FactoryEnter(JsonCondOwnerSave jcos) {
        if (jcos?.strID == Name) throw new RehydratedExisting();
    }
    static Ship Hull(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        Field(ship, "nLoadState", Ship.Loaded.Shallow);
        Field(ship, "<objSS>k__BackingField", new ShipSitu(false));
        Field(ship, "aPeople", new List<PersonSpec>());
        return ship;
    }
    static int Main() {
        var h = new Harmony("offline.native.npc-identity");
        try {
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            h.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetFullName)), prefix: new HarmonyMethod(typeof(Program), nameof(RollName)));
            h.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.EmbellishName)), prefix: new HarmonyMethod(typeof(Program), nameof(Embellish)));
            h.Patch(AccessTools.Method(typeof(PersonSpec), "GetHomeWorld"), prefix: new HarmonyMethod(typeof(Program), nameof(Homeworld)));
            h.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.Log), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(IgnoreLog)));
            existing = Empty<CondOwner>(); existing.strName = existing.strNameFriendly = Name;
            Field(existing, "_strID", Name);
            DataHandler.mapCOs = new Dictionary<string, CondOwner> { [Name] = existing };
            DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>();
            var spec = new JsonPersonSpec { strName = "fixture-new-combat-pilot", strHomeworldSet = "HW_BCRS", nAgeMin = 46, nAgeMax = 46 };
            DataHandler.dictHomeworlds = new Dictionary<string, JsonHomeworld> { ["HW_BCRS"] = new JsonHomeworld() };
            DataHandler.dictPersonSpecs = new Dictionary<string, JsonPersonSpec> { [spec.strName] = spec };
            CrewSim.coPlayer = null;
            var former = Hull("O-6F7"); var recruited = Hull("O-MYSS");
            Field(existing, "_objShip", former);
            var oldProfile = Empty<PersonSpec>(); oldProfile.strCO = Name; existing.pspec = oldProfile;
            former.People.Add(oldProfile);
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship> { [former.strRegID] = former, [recruited.strRegID] = recruited };
            var generated = new PersonSpec(spec, false);
            Check(generated.FullName == Name && generated.GetCO() == null, "Native unchecked creation did not keep the occupied name.");
            Check(ReferenceEquals(DataHandler.GetCondOwner("Crew01", generated.FullName, null, true), existing), "Native factory did not return the occupied actor.");
            Check(ReferenceEquals(oldProfile.MakeCondOwner(PersonSpec.StartShip.OLD), existing), "Legitimate existing-profile reuse did not take its early return.");
            var distinct = new PersonSpec(spec, true);
            Check(distinct.FullName != Name && distinct.GetCO() == null, "Native new-name path did not disambiguate the occupied name.");
            var factory = AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondOwner), new[] { typeof(string), typeof(string), typeof(string), typeof(bool), typeof(string), typeof(JsonCondOwnerSave), typeof(string), typeof(Transform) });
            h.Patch(factory, postfix: new HarmonyMethod(typeof(Program), nameof(FactoryReturn)));
            h.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetPerson)), prefix: new HarmonyMethod(typeof(Program), nameof(NoVolunteers)));
            h.Patch(AccessTools.Method(typeof(AIShip), "NotEnoughNPCsOnStation"), prefix: new HarmonyMethod(typeof(Program), nameof(Underpopulated)));
            AIShipManager.ShipClosestPOI = null;
            stopReuse = true; bool caught = false;
            try { AIShip.AddCrew(spec.strName, recruited, "BCRS", false); }
            catch (ReusedExisting) { caught = true; }
            Check(forbiddenSelections == 1 && caught, "Native AddCrew fallback did not bypass its own serving-pilot exclusion by name collision.");
            DataHandler.mapCOs.Clear();
            DataHandler.dictCOSaves[Name] = new JsonCondOwnerSave { strID = Name, strCODef = "Crew01", strCondID = Name };
            var savedCollision = new PersonSpec(spec, true);
            Check(savedCollision.FullName == Name, "Native bNew=true unexpectedly checked saved-only identities.");
            h.Patch(factory, prefix: new HarmonyMethod(typeof(Program), nameof(FactoryEnter)));
            bool restored = false;
            try { savedCollision.MakeCondOwner(PersonSpec.StartShip.OLD, recruited); }
            catch (RehydratedExisting) { restored = true; }
            Check(restored, "Fresh generation did not route into the existing saved character loader.");
            Console.WriteLine(checks + " native identity baseline checks passed without production patches.");
            Console.WriteLine("Native AddCrew excluded serving Tomiwa, found no volunteer, generated occupied name, and MakeCondOwner received the existing remote pilot from the native factory.");
            Console.WriteLine("Controlled exception stops before Unity component/chargen mutation; later age overwrite and SetShip are verified in the native method source, not executed by this probe.");
            Console.WriteLine("Setting bNew=true alone is insufficient: saved-only identities are not checked and still route to the existing character loader.");
            RepairedCases(h, former, recruited, spec, oldProfile, factory);
            return 0;
        } finally { h.UnpatchSelf(); }
    }
}
