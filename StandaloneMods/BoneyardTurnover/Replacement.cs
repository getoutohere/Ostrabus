using System;
using System.Linq;
using HarmonyLib;
using SolarSystem;

namespace Ostrabus.BoneyardTurnover
{
    internal static class Replacement
    {
        private delegate float BreakIn(JsonSpawnDerelict ring, double radius);
        private static readonly BreakIn Damage = AccessTools.MethodDelegate<BreakIn>(
            AccessTools.Method(typeof(EnvironmentEntities), "CalculateBreakInMultiplier"));

        // Native loot/placement evaluators use Unity's RNG. Restore its exact
        // state so turnover does not change later combat, loot or AI rolls.
        internal static T WithNativeRandom<T>(int seed, Func<T> action)
        {
            var saved = UnityEngine.Random.state;
            try { UnityEngine.Random.InitState(seed); return action(); }
            finally { UnityEngine.Random.state = saved; }
        }

        internal static StellarDerelict Prepare(Field field, Candidate old, int seed, bool extended)
        {
            return WithNativeRandom(seed, () =>
            {
                var names = DataHandler.GetLoot(field.Ring.strLootShipType)?.GetLootNames();
                if (names == null || names.Count == 0 || DataHandler.GetShip(names[0])?.objSS == null) return null;
                // Construction remains detached from native registries until
                // the predecessor has been removed and verified.
                var wreck = new StellarDerelict(names[0], field.Ring.strOwner, field.Map, null);
                if (CrewSim.system.dictShips.ContainsKey(wreck.strID) || CrewSim.system.dictStellarObjects.ContainsKey(wreck.strID) ||
                    CrewSim.system.GetShipOwner(wreck.strID) != "UNREGISTERED" || new Eligibility().References(wreck.strID) ||
                    DataHandler.dictCOSaves.Values.Any(x => x?.strRegIDLast == wreck.strID)) return null;
                double body = field.Ring.fAltSpawnDistanceKM > 0 ? 0 : field.Anchor.fRadiusKM;
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    double r = MathUtils.Rand(0d, 1d, MathUtils.RandType.Low);
                    double radius = body + field.Ring.fSpawnRadiusMinKM + r * r *
                        (field.Ring.fSpawnRadiusMaxKM - field.Ring.fSpawnRadiusMinKM);
                    if (extended)
                        radius = body + MathUtils.Rand(field.Ring.fSpawnRadiusMinKM, field.Ring.fSpawnRadiusMaxKM, MathUtils.RandType.High);
                    double min = Math.Max(radius - 10, field.InnerKm);
                    double max = Math.Min(radius + 10, field.OuterKm);
                    if (!CrewSim.system.SetSituToRandomSafeCoords(wreck.objSS, min / Field.AuKm, max / Field.AuKm,
                        field.Center.X, field.Center.Y)) continue;
                    wreck.objSS.fRot = MathUtils.Rand(0f, (float)(Math.PI * 2), MathUtils.RandType.Flat);
                    wreck.objSS.LockToBO(field.Anchor);
                    if (!field.Contains(wreck.objSS) || !Clear(wreck, old.Id)) continue;
                    wreck.BreakInMultiplier = Damage(field.Ring, radius - body);
                    if (!Curve.Finite(wreck.BreakInMultiplier)) return null;
                    wreck.Factions = field.Ring.aFactions == null ? null : (string[])field.Ring.aFactions.Clone();
                    return wreck;
                }
                return null;
            });
        }

        private static bool Clear(StellarDerelict next, string predecessor)
        {
            foreach (var hull in CrewSim.system.dictShips.Values)
                if (hull.strRegID != predecessor && !Separated(next.objSS, hull.objSS)) return false;
            // Native safe coordinates omit deferred wrecks. Include them and
            // replacements already published earlier in this same batch.
            foreach (var deferred in CrewSim.system.dictStellarObjects)
                if (deferred.strID != predecessor && !Separated(next.objSS, deferred.objSS)) return false;
            return true;
        }

        private static bool Separated(ShipSitu a, ShipSitu b)
        {
            if (b == null) return false;
            b.UpdateTime(StarSystem.fEpoch);
            double separation = (a.vPos - b.vPos).magnitude;
            return Curve.Finite(separation) && separation > a.GetRadiusAU() + b.GetRadiusAU() + .01 / Field.AuKm;
        }

        internal static void Commit(Candidate old, StellarDerelict next)
        {
            var world = CrewSim.system;
            if (world.dictShips.ContainsKey(next.strID) || world.dictStellarObjects.ContainsKey(next.strID))
                throw new InvalidOperationException("Replacement ID already exists.");
            if (old.Hull != null)
            {
                var co = old.Hull.ShipCO;
                string coId = co.strID;
                old.Hull.Destroy(isDespawning: false);
                if (!old.Hull.bDestroyed || world.dictShips.ContainsKey(old.Id) || !co.bDestroyed ||
                    DataHandler.mapCOs.ContainsKey(coId) || DataHandler.mapCOs.Values.Any(x => x != null && ReferenceEquals(x.ship, old.Hull)))
                    throw new InvalidOperationException("Native shallow hull removal did not complete: " + old.Id);
            }
            else
            {
                if (!world.dictStellarObjects.TryGetValue(old.Id, out var current) || !ReferenceEquals(current, old.Deferred))
                    throw new InvalidOperationException("Predecessor changed: " + old.Id);
                world.dictStellarObjects.Remove(old.Id);
            }
            world.dictStellarObjects.Add(next.strID, next);
            // Cache disposal is presentation work after successful publication.
            // It cannot turn a completed pair into a failed lifecycle operation.
            try
            {
                if (GUIOrbitDraw.Instance != null)
                {
                    if (old.Hull != null) GUIOrbitDraw.Instance.InvalidateShipDraw(old.Id);
                    else GUIOrbitDraw.Instance.InvalidateStellarObjectDraw(old.Id);
                }
            }
            catch (Exception error) { Plugin.Warn("Outline invalidation: " + error.Message); }
        }
    }
}
