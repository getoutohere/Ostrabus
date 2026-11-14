using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Trade;
using Ostranauts.ShipGUIs.Market;
using Ostranauts.Trading;

namespace Ostrabus.MiscBugFixes;

// Explicit maintenance only. All scans/snapshots die with the command invocation.
internal static class CargoPodFix {
    static readonly System.Reflection.FieldInfo ManagerInstance = AccessTools.Field(typeof(MonoSingleton<MarketManager>), "_instance");
    static readonly System.Reflection.FieldInfo LoaderInstance = AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance");
    static readonly AccessTools.FieldRef<MarketManager, Dictionary<string, ShipMarket>> Markets = AccessTools.FieldRefAccess<MarketManager, Dictionary<string, ShipMarket>>("_market");
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, MarketActorConfig>> Actors = AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, MarketActorConfig>>("_marketMapping");
    static readonly AccessTools.FieldRef<ShipMarket, List<MarketActor>> Loaded = AccessTools.FieldRefAccess<ShipMarket, List<MarketActor>>("_loadedMarketActors");
    static readonly AccessTools.FieldRef<Ship, DictionaryArray<string, CondOwner>> Objects = AccessTools.FieldRefAccess<Ship, DictionaryArray<string, CondOwner>>("mapICOs");
    static readonly AccessTools.FieldRef<Ship, bool> Ready = AccessTools.FieldRefAccess<Ship, bool>("_bDoneLoading");
    static readonly AccessTools.FieldRef<AsyncShipLoader, Dictionary<string, IAsyncLoadable>> Copies = AccessTools.FieldRefAccess<AsyncShipLoader, Dictionary<string, IAsyncLoadable>>("_loadStateDict");
    static readonly AccessTools.FieldRef<MarketActorConfig, string> Recipe = AccessTools.FieldRefAccess<MarketActorConfig, string>("_name");
    const int MaxRecords = 4096;
    internal sealed class Inventory {
        internal Ship Ship;
        internal BarterZoneShip Remote;
        internal readonly HashSet<string> Present = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Ambiguous = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> Pods = new(StringComparer.Ordinal);
        internal string Error;
    }
    internal sealed class Change {
        internal string Id, Recipe;
        internal bool Remove, Register;
        internal MarketActorConfig Expected;
    }
    internal sealed class Plan {
        internal Inventory Inventory;
        internal ShipMarket Market;
        internal readonly List<Change> Changes = new();
        internal int Preserved;
        internal string Error;
    }
    static bool Physical(Ship ship) => ship != null && !ship.bDestroyed && !(ship is AsyncRatingShip) && !(ship is AsyncPreviewShip) && !(ship is BarterZoneShip);
    internal static JsonShip Saved(Ship ship) => ship is BarterZoneShip remote ? remote.json : ship.json;
    internal static string Busy() {
        if (CrewSim.objInstance == null || !CrewSim.objInstance.FinishedLoading || CrewSim.bShipEdit) return "World is loading or being edited.";
        var ui = CrewSim.goUI;
        if (ui != null && (ui.GetComponentInChildren<GUITradeBase>() != null || ui.GetComponentInChildren<GUIShipMarket>() != null ||
            ui.GetComponentInChildren<Ostranauts.ShipGUIs.Market.GUICargoPod.GUICargoPod>() != null)) return "Close the cargo/trade window before running cargopodfix.";
        return null;
    }
    static bool Template(string name) => !string.IsNullOrEmpty(name) && DataHandler.dictMarketConfigs != null &&
        DataHandler.dictMarketConfigs.TryGetValue(name, out var config) && config != null && config.aCargoPodSpecs != null &&
        (config.aStock == null || config.aStock.Length == 0) && (config.aSupplyDemandMaps == null || config.aSupplyDemandMaps.Length == 0);
    static bool Mapped(Ship ship, string id, string recipe) => ship.MarketConfigs != null && ship.MarketConfigs.TryGetValue(id, out var current) && current == recipe &&
        (Saved(ship) == null || Saved(ship).aMarketConfigs != null && Saved(ship).aMarketConfigs.TryGetValue(id, out var stored) && stored == recipe);
    static void Identity(Inventory inv, string id) {
        if (string.IsNullOrEmpty(id)) { inv.Error = "An inventory item has no ID."; return; }
        if (!inv.Present.Add(id)) { inv.Ambiguous.Add(id); inv.Pods.Remove(id); }
    }
    static void Pod(Inventory inv, string id, string recipe) {
        if (inv.Ambiguous.Contains(id)) return;
        if (!Template(recipe)) { inv.Error = "Cargo pod " + id + " has an unknown or nonempty market template."; return; }
        inv.Pods[id] = recipe;
    }
    static Inventory Live(Ship source) {
        var inv = new Inventory { Ship = source };
        var objects = Objects(source);
        if (objects == null) { inv.Error = "Live inventory is unavailable."; return inv; }
        foreach (var co in objects.Values) {
            if (co == null || co.bDestroyed || !ReferenceEquals(co.ship, source)) { inv.Error = "Live inventory is incomplete or has conflicting ownership."; break; }
            Identity(inv, co.strID);
            if (co.HasCond("IsCargoPod", isThreshold: false) && co.HasCond("IsMarketActor", isThreshold: false) && co.HasCond("IsInstalled", isThreshold: false))
                Pod(inv, co.strID, MarketActor.GetMarketConfig(co));
        }
        return inv;
    }
    static double Amount(DataCO definition, JsonItem item, JsonCondOwnerSave saved, string name) {
        double? over = item.GetCondAmountOverride(name);
        if (over.HasValue) return over.Value;
        if (saved?.aCondZeroes != null && Array.IndexOf(saved.aCondZeroes, name) >= 0) return 0;
        if (saved?.aConds != null) {
            foreach (string text in saved.aConds) if (text != null && text.StartsWith(name + "=", StringComparison.Ordinal)) {
                var value = new DataCond(text);
                return value.NegativeValue ? -value.Amount : value.Amount;
            }
            if (Array.IndexOf(saved.aConds, "DEFAULT") < 0) return 0;
        }
        return definition.GetCondAmount(name);
    }
    static string Pair(string[] pairs, string key) {
        if (pairs == null) return null;
        for (int i = 0; i + 1 < pairs.Length; i += 2) if (pairs[i] == key) return pairs[i + 1];
        return null;
    }
    static string SavedRecipe(DataCO definition, JsonItem item) {
        string gpm = Pair(definition.JCO.mapGUIPropMaps, "Trader"), recipe = null;
        // COOverlay replaces whole named GPMs before per-item saved overrides.
        string overlay = Pair(definition.JCOO?.mapGUIPropMaps, "Trader");
        if (overlay != null) gpm = overlay;
        if (gpm != null && DataHandler.dictGUIPropMaps != null && DataHandler.dictGUIPropMaps.TryGetValue(gpm, out var props)) props.TryGetValue("strMarketConfig", out recipe);
        if (item.aGPMSettings != null) foreach (var settings in item.aGPMSettings) if (settings?.strName == "Trader") {
            string over = Pair(settings.dictGUIPropMap, "strMarketConfig");
            if (over != null) recipe = over;
        }
        return recipe;
    }
    internal static Inventory Read(Ship ship) {
        var inv = new Inventory { Ship = ship };
        if (!Physical(ship)) { inv.Error = "Not a live canonical ship."; return inv; }
        if (ship.LoadState == Ship.Loaded.Full) {
            if (!Ready(ship)) { inv.Error = "Ship inventory is still loading."; return inv; }
            inv = Live(ship);
        } else if (ship.LoadState == Ship.Loaded.Shallow && ship.json?.aItems != null) {
            var saves = new Dictionary<string, JsonCondOwnerSave>(StringComparer.Ordinal);
            if (ship.json.aCOs != null) foreach (var saved in ship.json.aCOs) if (saved?.strID != null) saves[saved.strID] = saved;
            foreach (var item in ship.json.aItems) {
                if (item == null) { inv.Error = "Saved inventory has a missing item record."; break; }
                Identity(inv, item.strID);
                if (DataHandler.dictDataCOs == null || !DataHandler.dictDataCOs.TryGetValue(item.strName ?? "", out var definition)) {
                    inv.Error = "Saved item definition is unavailable: " + item.strName; break;
                }
                if (string.IsNullOrEmpty(item.strID)) break;
                JsonCondOwnerSave saved = null;
                if (DataHandler.dictCOSaves != null) DataHandler.dictCOSaves.TryGetValue(item.strID, out saved);
                if (saved == null) saves.TryGetValue(item.strID, out saved);
                if (saved != null && !string.IsNullOrEmpty(saved.strCODef) && saved.strCODef != item.strName) {
                    inv.Error = "Saved item definitions disagree for " + item.strID; break;
                }
                if (Amount(definition, item, saved, "IsCargoPod") > 0 && Amount(definition, item, saved, "IsMarketActor") > 0 && Amount(definition, item, saved, "IsInstalled") > 0)
                    Pod(inv, item.strID, SavedRecipe(definition, item));
            }
            if (ship.json.aCrew != null) foreach (var crew in ship.json.aCrew) if (crew?.strID != null) inv.Present.Add(crew.strID);
        } else inv.Error = "Ship is loading/editing or has no authoritative saved inventory.";
        if (inv.Error != null) return inv;
        var loader = (AsyncShipLoader)LoaderInstance.GetValue(null);
        if (loader != null && Copies(loader)?.TryGetValue(ship.strRegID, out var copy) == true) {
            if (copy == null || !copy.FullyLoaded) inv.Error = "An offscreen ship copy is still loading.";
            else if (copy is BarterZoneShip remote) {
                var live = Live(remote);
                if (live.Error != null || live.Pods.Count != inv.Pods.Count) inv.Error = "Offscreen and canonical cargo-pod inventories disagree.";
                else foreach (var pod in inv.Pods) if (!live.Pods.TryGetValue(pod.Key, out var recipe) || recipe != pod.Value) inv.Error = "Offscreen and canonical cargo-pod inventories disagree.";
                if (inv.Error == null) { inv.Remote = remote; inv.Present.UnionWith(live.Present); }
            }
        }
        return inv;
    }
    // Only candidate IDs are indexed; do not retain every item in the simulation.
    internal static Dictionary<string, HashSet<string>> Index(StarSystem world, HashSet<string> candidates, Dictionary<string, ShipMarket> markets) {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string id, string owner) {
            if (id == null || !candidates.Contains(id)) return;
            if (!index.TryGetValue(id, out var owners)) index[id] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(owner);
        }
        foreach (var ship in world.dictShips.Values) {
            if (!Physical(ship)) continue;
            if (Objects(ship) != null && ship.LoadState >= Ship.Loaded.Edit) foreach (var co in Objects(ship).Values) if (co != null) Add(co.strID, ship.strRegID);
            if (ship.LoadState <= Ship.Loaded.Edit && ship.json != null) {
                if (ship.json.aItems != null) foreach (var item in ship.json.aItems) Add(item?.strID, ship.strRegID);
                if (ship.json.aCrew != null) foreach (var item in ship.json.aCrew) Add(item?.strID, ship.strRegID);
            }
            if (ship.MarketConfigs != null) foreach (string id in ship.MarketConfigs.Keys) Add(id, "mapping:" + ship.strRegID);
            if (ship.json?.aMarketConfigs != null) foreach (string id in ship.json.aMarketConfigs.Keys) Add(id, "mapping:" + ship.strRegID);
        }
        if (DataHandler.mapCOs != null) foreach (string id in candidates) if (DataHandler.mapCOs.TryGetValue(id, out var co) && co != null && !(co.ship is AsyncPreviewShip) && !(co.ship is AsyncRatingShip)) Add(id, "live:" + co.ship?.strRegID);
        foreach (var pair in markets) if (pair.Value != null && Actors(pair.Value) != null)
            foreach (string id in Actors(pair.Value).Keys) Add(id, "market:" + pair.Key);
        return index;
    }
    static bool Empty(MarketActorConfig config) {
        if (config?.Stock == null || config.MaxVirtualInventorySize == null || config.GetProductionMaps() == null || config.GetProductionMaps().Count != 0 || !string.IsNullOrEmpty(config.HubRegId)) return false;
        foreach (var items in config.Stock.Values) if (items == null || items.Count != 0) return false;
        return true;
    }
    static bool ActorExists(ShipMarket market, string id) {
        if (market == null || Loaded(market) == null) return false;
        foreach (var actor in Loaded(market)) if (actor != null && actor.GetCOId() == id) return true;
        return false;
    }
    internal static Plan Inspect(Inventory inv, Dictionary<string, ShipMarket> markets, Dictionary<string, HashSet<string>> index, Action<string> report) {
        var plan = new Plan { Inventory = inv, Error = inv.Error };
        if (plan.Error != null) return plan;
        Ship ship = inv.Ship;
        markets.TryGetValue(ship.strRegID, out plan.Market);
        var actors = plan.Market == null ? null : Actors(plan.Market);
        if (plan.Market != null && (plan.Market.ShipRegId != ship.strRegID || actors == null || actors.Count > MaxRecords)) { plan.Error = "Market actor metadata is unavailable, inconsistent or excessive."; return plan; }
        var ids = new HashSet<string>(inv.Pods.Keys, StringComparer.Ordinal);
        if (actors != null) foreach (var pair in actors) if (pair.Value?.IsCargoPod == true) ids.Add(pair.Key);
        if (ship.MarketConfigs != null) foreach (var pair in ship.MarketConfigs) if (Template(pair.Value)) ids.Add(pair.Key);
        if (ship.json?.aMarketConfigs != null) foreach (var pair in ship.json.aMarketConfigs) if (Template(pair.Value)) ids.Add(pair.Key);
        foreach (string id in ids) {
            MarketActorConfig config = null;
            actors?.TryGetValue(id, out config);
            if (inv.Ambiguous.Contains(id) || config != null && config.COwnerId != id) { Keep(id, "ambiguous item/config ownership"); continue; }
            bool foreign = false;
            if (index.TryGetValue(id, out var owners)) foreach (string owner in owners)
                if (owner != ship.strRegID && owner != "mapping:" + ship.strRegID && owner != "live:" + ship.strRegID && owner != "market:" + ship.strRegID) foreign = true;
            if (foreign) { Keep(id, "item or mapping is associated with another ship"); continue; }
            if (inv.Pods.TryGetValue(id, out var recipe)) {
                if (config != null && (!config.IsCargoPod || Recipe(config) != recipe)) { Keep(id, "existing market template differs; stock retained"); continue; }
                if (config == null || !Mapped(ship, id, recipe) || inv.Remote != null && !Mapped(inv.Remote, id, recipe))
                    plan.Changes.Add(new Change { Id = id, Recipe = recipe, Expected = config, Register = config == null });
            } else {
                bool present = inv.Present.Contains(id) || ActorExists(plan.Market, id);
                if (index.TryGetValue(id, out owners) && (owners.Contains(ship.strRegID) || owners.Contains("live:" + ship.strRegID))) present = true;
                if (present) { Keep(id, "physical item or loaded actor still exists"); continue; }
                if (config != null && (!config.IsCargoPod || !Empty(config))) { Keep(id, "cargo-bearing or nonempty/unknown actor metadata"); continue; }
                plan.Changes.Add(new Change { Id = id, Expected = config, Remove = true });
            }
        }
        return plan;
        void Keep(string id, string reason) { plan.Preserved++; report?.Invoke(ship.strRegID + " / " + id + ": preserved (" + reason + ")."); }
    }
    static void Mapping(Ship ship, string id, string recipe) {
        ship.MarketConfigs ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (recipe == null) ship.MarketConfigs.Remove(id); else ship.MarketConfigs[id] = recipe;
        var json = Saved(ship);
        if (json != null) {
            json.aMarketConfigs ??= new Dictionary<string, string>(StringComparer.Ordinal);
            if (recipe == null) json.aMarketConfigs.Remove(id); else json.aMarketConfigs[id] = recipe;
        }
    }
    internal static bool Apply(Plan plan, Dictionary<string, ShipMarket> markets, out string error) {
        error = null;
        Ship ship = plan.Inventory.Ship;
        if (plan.Error != null) { error = plan.Error; return false; }
        markets.TryGetValue(ship.strRegID, out var original);
        if (!ReferenceEquals(original, plan.Market)) { error = "Ship market changed; rerun cargopodfix."; return false; }
        var rollback = new CargoPodFixRollback(ship, plan.Inventory.Remote, markets);
        try {
            var fresh = Read(ship);
            if (fresh.Error != null || !ReferenceEquals(fresh.Remote, plan.Inventory.Remote) || fresh.Pods.Count != plan.Inventory.Pods.Count) throw new InvalidOperationException("Inventory changed; rerun cargopodfix.");
            foreach (var pod in plan.Inventory.Pods) if (!fresh.Pods.TryGetValue(pod.Key, out var recipe) || recipe != pod.Value) throw new InvalidOperationException("Pod identities changed; rerun cargopodfix.");
            foreach (var change in plan.Changes) {
                markets.TryGetValue(ship.strRegID, out var market);
                var current = market?.GetConfigForMarketActor(change.Id);
                if (!ReferenceEquals(current, change.Expected)) throw new InvalidOperationException("Market entry changed; rerun cargopodfix.");
                if (change.Remove) {
                    if (fresh.Present.Contains(change.Id) || ActorExists(market, change.Id) || current != null && !Empty(current)) throw new InvalidOperationException("Removal candidate became occupied or nonempty.");
                    if (current != null) {
                        var removed = MarketManager.RemoveMarketActorFromShip(ship, change.Id);
                        if (removed == null || removed.Count != 0) throw new InvalidOperationException("Native removal returned cargo; repair rolled back.");
                    }
                    Mapping(ship, change.Id, null);
                    if (fresh.Remote != null) Mapping(fresh.Remote, change.Id, null);
                } else {
                    Mapping(ship, change.Id, change.Recipe);
                    if (fresh.Remote != null) Mapping(fresh.Remote, change.Id, change.Recipe);
                    if (change.Register) MarketManager.AddMarketActorToShip(ship, change.Id);
                    if (!markets.TryGetValue(ship.strRegID, out market) || market.GetConfigForMarketActor(change.Id) == null) throw new InvalidOperationException("Native cargo-pod registration did not complete.");
                }
            }
            return true;
        } catch (Exception failure) { rollback.Restore(); error = failure.Message; return false; }
    }
    internal static void Run(bool apply, string callSign, Action<string> report) {
        void Say(string message) => report("[CargoPodFix] " + message);
        var world = CrewSim.system;
        string player = CrewSim.coPlayer?.strID;
        if (world == null || string.IsNullOrEmpty(player)) { Say("No loaded player world."); return; }
        string busy = Busy();
        if (busy != null) { Say(busy); return; }
        var manager = (MarketManager)ManagerInstance.GetValue(null);
        var markets = manager == null ? null : Markets(manager);
        if (markets == null) { Say("Market registry is unavailable."); return; }
        var owned = world.GetShipsForOwner(player);
        var inventories = new List<Inventory>();
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in owned) {
            if (callSign != null && !string.Equals(callSign, id, StringComparison.OrdinalIgnoreCase)) continue;
            if (!world.dictShips.TryGetValue(id, out var ship) || !Physical(ship) || world.GetShipOwner(id) != player) { Say(id + ": skipped; canonical owned hull is unavailable."); continue; }
            var inventory = Read(ship); inventories.Add(inventory);
            foreach (string pod in inventory.Pods.Keys) candidates.Add(pod);
            if (ship.MarketConfigs != null) foreach (string pod in ship.MarketConfigs.Keys) candidates.Add(pod);
            if (ship.json?.aMarketConfigs != null) foreach (string pod in ship.json.aMarketConfigs.Keys) candidates.Add(pod);
            if (markets.TryGetValue(id, out var market) && Actors(market) != null) foreach (var pair in Actors(market)) if (pair.Value?.IsCargoPod == true) candidates.Add(pair.Key);
        }
        if (inventories.Count == 0) { Say(callSign == null ? "No eligible player ships found." : "No eligible player-owned ship matches " + callSign + "."); return; }
        var index = Index(world, candidates, markets);
        int repaired = 0, planned = 0, preserved = 0, skipped = 0;
        foreach (var inventory in inventories) {
            var plan = Inspect(inventory, markets, index, Say);
            if (plan.Error != null) { skipped++; Say(inventory.Ship.strRegID + ": skipped; " + plan.Error); continue; }
            planned += plan.Changes.Count; preserved += plan.Preserved;
            Say(inventory.Ship.strRegID + ": " + inventory.Pods.Count + " installed pods; " + plan.Changes.Count + " repairs; " + plan.Preserved + " preserved records.");
            foreach (var change in plan.Changes) Say(inventory.Ship.strRegID + " / " + change.Id + ": " + (change.Remove ? "remove empty orphan" : change.Register ? "restore registration" : "synchronize pod mapping") + (apply ? "." : " (dry run)."));
            if (!apply || plan.Changes.Count == 0) continue;
            if (world.GetShipOwner(inventory.Ship.strRegID) != player || !ReferenceEquals(world.GetShipByRegID(inventory.Ship.strRegID), inventory.Ship)) { skipped++; Say("Ship identity/ownership changed; skipped."); continue; }
            if (Apply(plan, markets, out string error)) repaired += plan.Changes.Count;
            else { skipped++; Say(inventory.Ship.strRegID + ": rolled back; " + error); }
        }
        Say((apply ? "Apply" : "Dry run") + ": " + inventories.Count + " ships, " + planned + " proposed repairs, " + repaired + " applied, " + preserved + " preserved, " + skipped + " skipped." + (apply ? "" : " Use cargopodfix apply to repair."));
    }
}

[HarmonyPatch(typeof(ConsoleResolver), "ResolveString")]
internal static class CargoPodFixCommand {
    internal static bool Parse(string input, out bool apply, out string id, out string error) {
        apply = false; id = error = null;
        if (input == null) return false;
        string[] words = input.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || !string.Equals(words[0], "cargopodfix", StringComparison.OrdinalIgnoreCase)) return false;
        bool mode = false;
        for (int i = 1; i < words.Length; i++) {
            string word = words[i];
            if (word.Equals("apply", StringComparison.OrdinalIgnoreCase) || word.Equals("dryrun", StringComparison.OrdinalIgnoreCase)) {
                if (mode) { error = "Specify only one mode."; break; }
                mode = true; apply = word.Equals("apply", StringComparison.OrdinalIgnoreCase);
            } else if (id != null || word.Length > 64 || word.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) { error = "Expected at most one ship call sign."; break; }
            else id = word;
        }
        return true;
    }
    static bool Prefix(string strInput) {
        if (!Parse(strInput, out bool apply, out string id, out string error)) return true;
        if (error != null) ConsoleToGUI.instance?.LogInfo("[CargoPodFix] " + error + " Usage: cargopodfix [dryrun|apply] [CALLSIGN].");
        else try { CargoPodFix.Run(apply, id, message => ConsoleToGUI.instance?.LogInfo(message)); }
        catch (Exception failure) { ConsoleToGUI.instance?.LogInfo("[CargoPodFix] Command stopped: " + failure.Message); }
        return false;
    }
}
