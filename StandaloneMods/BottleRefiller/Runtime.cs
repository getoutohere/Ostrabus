using System;
using System.Collections.Generic;
using Ostranauts.Core;
using UnityEngine;

namespace Ostrabus.BottleRefiller
{
    internal static class Runtime
    {
        internal const string Item = "ItmOstrabusBottleRefiller";
        internal static bool Transferring;
        internal static bool Definition(string name) => name == Item || name == Item + "Loose" ||
            name == Item + "Dmg" || name == Item + "DmgLoose";

        internal static bool LiveShip(Ship ship)
        {
            if (ship == null || ship.bDestroyed || ship.bRemove || ship.LoadState != Ship.Loaded.Full ||
                ship.GetType() != typeof(Ship) || string.IsNullOrEmpty(ship.strRegID)) return false;
            return CrewSim.system != null &&
                ReferenceEquals(CrewSim.system.GetShipByRegID(ship.strRegID), ship) &&
                ReferenceEquals(CrewSim.GetLoadedShipByRegId(ship.strRegID), ship);
        }

        internal static bool LiveOwner(CondOwner co) => co != null && !co.bDestroyed && !co.bFreezeConds &&
            LiveShip(co.ship) && !string.IsNullOrEmpty(co.strID) &&
            (co.HasCond("IsRoom") ? ReferenceEquals(co.ship.GetRoomByID(co.strID)?.CO, co) :
                ReferenceEquals(co.ship.GetCOByID(co.strID), co));

        internal static bool Enabled(CondOwner co) => co != null && !co.bDestroyed && co.strCODef == Item &&
            co.objCOParent == null && co.HasCond("IsInstalled") && co.HasCond("IsOverrideOn") &&
            !co.HasCond("IsOverrideOff") && co.HasCond("IsPowered") && !co.HasCond("IsDamaged") &&
            !co.HasCond("IsModeSwitching");

        internal static bool Working(CondOwner co) => LiveOwner(co) && Enabled(co);

        internal static bool Bottle(CondOwner bottle, CondOwner rack) => LiveOwner(bottle) &&
            ReferenceEquals(bottle.objCOParent, rack) && bottle.GasContainer != null &&
            DataHandler.GetCondTrigger("TIsFitContainerEVABottleNotDamaged").Triggered(bottle);

        internal static void ApplyInstalledHandling(CondOwner owner)
        {
            // Old saved condition lists can omit newly authored defaults. Restore
            // only our installed world's native inventory-hiding flag. A unit
            // already mis-carried in 0.1.0 stays visible for the Repack action.
            if ((owner.strCODef != Item && owner.strCODef != Item + "Dmg") || !owner.HasCond("IsInstalled")) return;
            if (owner.objCOParent != null)
            {
                // A saved DEFAULT list may have picked up the new hidden flag;
                // expose a previously mis-carried instance for its Repack action.
                owner.ZeroCondAmount("IsHiddenInv");
                return;
            }
            if (!owner.HasCond("IsHiddenInv")) owner.SetCondAmount("IsHiddenInv", 1);
            owner.mapSlotEffects?.Remove("drag");
        }

        internal static CondOwner SelectSource(IEnumerable<CondOwner> candidates, CondOwner rack,
            out bool ambiguous)
        {
            CondOwner tank = null, room = null;
            ambiguous = false;
            foreach (var candidate in candidates)
            {
                if (candidate == null || ReferenceEquals(candidate, rack) || !LiveOwner(candidate) ||
                    candidate.GasContainer == null || candidate.objCOParent != null || candidate.HasCond("IsHuman")) continue;
                if (candidate.HasCond("IsRoom"))
                {
                    if (room != null && !ReferenceEquals(room, candidate)) ambiguous = true;
                    room = candidate;
                }
                else if (candidate.HasCond("IsAirtight"))
                {
                    if (tank != null && !ReferenceEquals(tank, candidate)) { ambiguous = true; return null; }
                    tank = candidate;
                }
            }
            // Physical source always wins; its emptiness is not a room fallback.
            if (tank != null) { ambiguous = false; return tank; }
            return ambiguous ? null : room;
        }
    }

    public sealed class Refiller : MonoBehaviour
    {
        private CondOwner owner;
        private readonly ServiceClock serviceClock = new ServiceClock();
        private bool handlingChecked;
        private AudioEmitter pumpAudio, gasAudio;
        private bool audioActive, fillingActive;
        private Renderer fillingIndicator, standbyIndicator;
        private enum IndicatorState { Off, Standby, Filling }
        private IndicatorState? indicatorState;
        private readonly List<CondOwner> bottles = new List<CondOwner>(4);
        private readonly List<CondOwner> candidates = new List<CondOwner>();

        private void Awake() { owner = GetComponent<CondOwner>(); }

        private void Start() { RefreshIndicator(); }

        private void Update()
        {
            if (owner == null || owner.Clock == null || CrewSim.system == null || CrewSim.bShipEdit ||
                MonoSingleton<CrewSim>.Instance == null || !MonoSingleton<CrewSim>.Instance.FinishedLoading)
            {
                fillingActive = false;
                SetAudioActive(false);
                SetIndicator(IndicatorState.Off);
                return;
            }
            if (fillingActive && !Runtime.Enabled(owner))
                SetFilling(false);
            RefreshIndicator();
            if (CrewSim.Paused)
            {
                // Silence the motor, but retain the last simulated indicator
                // state just as native equipment does while time is paused.
                if (audioActive) ReleaseAudio();
                return;
            }
            if (!handlingChecked && Runtime.LiveOwner(owner))
            {
                Runtime.ApplyInstalledHandling(owner);
                handlingChecked = true;
            }
            double now = owner.Clock.Epoch;
            if (!serviceClock.Due(now, out double elapsed)) return;
            if (Runtime.Transferring || !Runtime.Working(owner) || owner.objContainer == null)
            {
                SetFilling(false);
                return;
            }
            try
            {
                SetFilling(Service(elapsed));
            }
            catch (Exception error)
            {
                // Do not retry an interrupted native commit and risk double credit.
                enabled = false;
                Plugin.Log?.LogError("Bottle refiller " + owner.strID + " stopped after a service error: " + error);
            }
            finally { bottles.Clear(); candidates.Clear(); }
        }

        private bool Service(double elapsed)
        {
            Runtime.Transferring = true;
            try
            {
                foreach (var bottle in owner.objContainer.ContainedCOs)
                {
                    if (!Runtime.Bottle(bottle, owner)) continue;
                    if (bottles.Count == 4) return false;
                    bottles.Add(bottle);
                }
                if (bottles.Count == 0) { owner.mapInfo["Status"] = "No bottles"; return false; }
                bool needsGas = false;
                foreach (var bottle in bottles)
                {
                    var gas = bottle.GasContainer;
                    if (gas.RequiresInit || bottle.GasChanged || gas.mapDGasMols.Count != 0 || gas.fDGasTemp != 0 ||
                        bottle.GetCondAmount("StatGasPressure") < bottle.GetCondAmount("StatGasPressureMax") * (1 - 1e-10))
                        needsGas = true;
                }
                if (!needsGas) { owner.mapInfo["Status"] = "Bottles full"; return false; }
                owner.ship.GetCOsAtWorldCoords1(owner.GetPos("GasInput"),
                    DataHandler.GetCondTrigger("TIsAirtightNoHuman"), true, false, candidates);
                var source = Runtime.SelectSource(candidates, owner, out bool ambiguous);
                if (source == null) { owner.mapInfo["Status"] = ambiguous ? "Ambiguous gas source" : "No gas source"; return false; }
                bool moved = NativeTransfer.Fill(owner, source, bottles, elapsed);
                owner.mapInfo["Status"] = moved ? "Filling bottles" : "Ready / no gas needed or available";
                return moved;
            }
            finally { Runtime.Transferring = false; }
        }

        private AudioEmitter CreateAudio(string definition)
        {
            var node = new GameObject("BottleRefiller-" + definition);
            node.transform.SetParent(owner.tf, false);
            try
            {
                var emitter = node.AddComponent<AudioEmitter>();
                emitter.SetData(DataHandler.GetAudioEmitter(definition));
                return emitter;
            }
            catch { Destroy(node); throw; }
        }

        private void SetFilling(bool active)
        {
            fillingActive = active;
            RefreshIndicator();
            SetAudioActive(active);
        }

        private void SetAudioActive(bool active)
        {
            if (active == audioActive) return;
            if (active)
            {
                if (pumpAudio == null) pumpAudio = CreateAudio("AirPumpActive");
                if (gasAudio == null) gasAudio = CreateAudio("AirPumpActiveAir");
                pumpAudio.PlaySteady();
                gasAudio.PlaySteady();
            }
            else
            {
                ReleaseAudio();
            }
            audioActive = active;
        }

        private void OnDisable()
        {
            SetIndicator(IndicatorState.Off);
            fillingActive = false;
            ReleaseAudio();
        }

        private void RefreshIndicator()
        {
            SetIndicator(!Runtime.Enabled(owner) ? IndicatorState.Off :
                fillingActive ? IndicatorState.Filling : IndicatorState.Standby);
        }

        private void SetIndicator(IndicatorState state)
        {
            // Item.SetData owns placement, rotation, visibility and native light
            // material setup. Our two overlapping sprite lights have no room
            // illumination. Toggle their renderers without changing the item's
            // transform or native visibility/culling state.
            bool bound = false;
            if (fillingIndicator == null) { fillingIndicator = FindIndicator("ItmLitGreen"); bound |= fillingIndicator != null; }
            if (standbyIndicator == null) { standbyIndicator = FindIndicator("ItmLitYellow"); bound |= standbyIndicator != null; }
            if (indicatorState == state && !bound) return;
            if (fillingIndicator != null) fillingIndicator.enabled = state == IndicatorState.Filling;
            if (standbyIndicator != null) standbyIndicator.enabled = state == IndicatorState.Standby;
            indicatorState = state;
        }

        private Renderer FindIndicator(string image)
        {
            var item = owner == null ? null : owner.Item;
            if (item?.dictLightSprites == null) return null;
            foreach (var light in item.dictLightSprites.Keys)
            {
                if (light == null) continue;
                var renderer = light.GetComponent<Renderer>();
                // Native DataHandler.GetMaterial names its shared cached
                // material with the source image followed by its map IDs.
                if (renderer?.sharedMaterial != null && renderer.sharedMaterial.name.StartsWith(image, StringComparison.Ordinal))
                    return renderer;
            }
            return null;
        }

        private void ReleaseAudio()
        {
            // Immediate native source stop also works while paused or unloading;
            // fade coroutines would depend on advancing Unity time. Destroy the
            // child objects too, so repeated fills do not leave AudioSources.
            if (pumpAudio != null) { pumpAudio.Destroy(); Destroy(pumpAudio.gameObject); }
            if (gasAudio != null) { gasAudio.Destroy(); Destroy(gasAudio.gameObject); }
            pumpAudio = null; gasAudio = null; audioActive = false;
        }

        private void OnDestroy() { OnDisable(); }
    }
}
