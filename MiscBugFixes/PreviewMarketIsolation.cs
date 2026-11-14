using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Trading;

namespace Ostrabus.MiscBugFixes;

// Presentation copies can share a real call sign but never own live markets.
// Guard registration, not the per-frame MarketActor.Update callback. Real remote
// barter copies remain authoritative and retain their native registration.
[HarmonyPatch(typeof(MarketManager), nameof(MarketManager.RegisterTrader))]
internal static class PreviewMarketIsolation {
    static readonly AccessTools.FieldRef<MarketActor, CondOwner> Owner = AccessTools.FieldRefAccess<MarketActor, CondOwner>("COSelf");
    static readonly AccessTools.FieldRef<MarketActor, string> Config = AccessTools.FieldRefAccess<MarketActor, string>("_strMarketConfig");
    static bool Prefix(MarketActor trader) {
        if (ReferenceEquals(trader, null)) return true;
        Ship ship = Owner(trader)?.ship;
        if (!(ship is AsyncRatingShip) && !(ship is AsyncPreviewShip)) return true;
        // Update calls SyncInventoryToMarket after RegisterTrader. Clearing the
        // presentation actor's local config also prevents that sync and later
        // ReportTransaction from mutating the real ship's existing stock.
        Config(trader) = null;
        return false;
    }
}
