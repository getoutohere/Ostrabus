using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SolarSystem;

namespace Ostrabus.BoneyardTurnover
{
    internal sealed class Departure
    {
        internal string OldRegion, NewRegion;
        internal List<FieldState> Fields = new List<FieldState>();
    }

    internal static class Runtime
    {
        internal static TurnoverState State = new TurnoverState();
        internal static StarSystem World;
        internal static bool Ready;
        private static string pendingEntry;
        private delegate bool CrewPresent(Ship station);
        private static readonly CrewPresent HasCrew = AccessTools.MethodDelegate<CrewPresent>(
            AccessTools.Method(typeof(EnvironmentEntities), "IsCrewPresentInRegion"));

        internal static void Reset()
        {
            Ready = false; World = null; pendingEntry = null; State = new TurnoverState();
        }

        internal static void Loaded(TurnoverState restored)
        {
            World = CrewSim.system;
            State = restored ?? new TurnoverState();
            State.Region = AIShipManager.strATCLast;
            Ready = true;
            pendingEntry = null; // Reloading is never a region return.
        }

        internal static bool Active => Ready && Plugin.Enabled && World != null && ReferenceEquals(World, CrewSim.system);

        internal static Departure BeforeDeparture()
        {
            if (!Active || string.IsNullOrEmpty(AIShipManager.strATCLast) ||
                AIShipManager.strATCLast == CollisionManager.strATCClosest) return null;
            var departure = new Departure { OldRegion = AIShipManager.strATCLast, NewRegion = CollisionManager.strATCClosest };
            var station = World.GetShipByRegID(departure.OldRegion);
            string blocked = station == null ? "regional reference missing" :
                HasCrew(station) ? "player crew remain in region" : HasUnresolvedCrew() ? "crew whereabouts unresolved" :
                !Curve.Positive(Plugin.HalfLife) || !Curve.Positive(Plugin.Shape) ? "invalid curve settings" : null;
            if (blocked != null)
            {
                Plugin.LogDeparture("Departure " + departure.OldRegion + ": not tracked (" + blocked + ").");
                return departure;
            }
            var eligibility = new Eligibility();
            foreach (var field in Field.ForRegion(departure.OldRegion))
            {
                var previous = State.Fields.FirstOrDefault(x => x.Key == field.Key && x.Definition == field.Definition);
                var record = new FieldState
                {
                    Key = field.Key, Region = field.Region, Definition = field.Definition, Away = true,
                    Departure = StarSystem.fEpoch, HalfLife = Plugin.HalfLife, Shape = Plugin.Shape,
                    Seed = Guid.NewGuid().GetHashCode()
                };
                foreach (var wreck in eligibility.List(field))
                    record.Ages[wreck.Id] = previous != null && previous.Ages.TryGetValue(wreck.Id, out double age) ? age : 0;
                if (record.Ages.Count != 0) departure.Fields.Add(record);
                Plugin.LogDeparture("Departure " + field.Key + ": " + record.Ages.Count + " eligible wrecks.");
            }
            return departure;
        }

        private static bool HasUnresolvedCrew()
        {
            // The native region check omits roster IDs that do not currently
            // resolve to a live CO. Uncertain crew whereabouts cannot establish
            // an empty region, so decline this departure rather than guess.
            var roster = CrewSim.coPlayer?.Company?.mapRoster;
            return roster != null && roster.Keys.Any(id =>
                !DataHandler.mapCOs.TryGetValue(id, out var member) || member == null || member.ship == null);
        }

        internal static void AfterDeparture(Departure departure)
        {
            if (!Active || departure == null || AIShipManager.strATCLast != departure.NewRegion ||
                AIShipManager.strATCLast == departure.OldRegion) return;
            State.Fields.RemoveAll(x => x.Region == departure.OldRegion);
            State.Fields.AddRange(departure.Fields);
            State.Region = departure.NewRegion;
            pendingEntry = departure.NewRegion;
        }

        internal static void Enter(string region)
        {
            if (!Active || string.IsNullOrEmpty(region) || region != pendingEntry) return;
            pendingEntry = null;
            // Consume before any mutation: a repeated native callback cannot
            // reroll a protected, failed or partially completed return.
            var due = State.Fields.Where(x => x.Region == region && x.Away).ToArray();
            foreach (var record in due) record.Away = false;
            if (due.Length == 0) return;
            var fields = Field.ForRegion(region);
            foreach (var record in due)
            {
                var field = fields.FirstOrDefault(x => x.Key == record.Key && x.Definition == record.Definition);
                if (field == null) { State.Fields.Remove(record); continue; }
                if (!Apply(field, record)) return;
            }
            try { if (GUIOrbitDraw.Instance != null) GUIOrbitDraw.Instance.LoadOtherStellarObjects(); }
            catch (Exception error) { Plugin.Warn("Map refresh: " + error.Message); }
        }

        internal static bool Apply(Field field, FieldState record)
        {
            double elapsed = StarSystem.fEpoch - record.Departure;
            if (elapsed < 0 && StateCodec.SameNativeTime(StarSystem.fEpoch, record.Departure)) elapsed = 0;
            double days = elapsed / 86400d;
            if (!Curve.Finite(days) || days < 0) { record.Ages.Clear(); return true; }
            var eligible = new Eligibility().List(field).Where(x => record.Ages.ContainsKey(x.Id)).ToArray();
            var probabilities = new double[eligible.Length];
            for (int i = 0; i < eligible.Length; ++i)
            {
                double age = record.Ages[eligible[i].Id];
                if (!Curve.TryProbability(age, days, record.HalfLife, record.Shape, out probabilities[i]) || !Curve.Finite(age + days))
                { Plugin.Warn("Invalid curve; retained field " + field.Key); return true; }
            }
            var random = new Random(record.Seed);
            var selected = Curve.Select(probabilities, Curve.Shuffle(eligible.Length, random), random.NextDouble());
            var survivors = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < eligible.Length; i++) survivors[eligible[i].Id] = record.Ages[eligible[i].Id] + days;
            record.Ages = survivors;
            int replaced = 0, skipped = 0;
            for (int i = 0; i < eligible.Length; i++)
            {
                if (!selected[i]) continue;
                var old = eligible[i];
                var next = Replacement.Prepare(field, old, random.Next(), i < .05 * eligible.Length);
                if (next == null || !new Eligibility().Allows(field, old)) { skipped++; continue; }
                try { Replacement.Commit(old, next); }
                catch (Exception error)
                {
                    // Native destruction is irreversible. Never re-register a
                    // partially destroyed ship as a pretend rollback.
                    Plugin.Warn("Stopped turnover after native removal failure for " + old.Id + ": " + error);
                    return false;
                }
                record.Ages.Remove(old.Id); record.Ages[next.strID] = 0;
                replaced++;
            }
            Plugin.Summary(region: field.Region, replaced: replaced, retained: eligible.Length - replaced, skipped: skipped);
            return true;
        }
    }
}
