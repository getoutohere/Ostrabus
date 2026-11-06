using HarmonyLib;

namespace Ostrabus.ShipboardAction;

[HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.Update))]
internal static class ShipboardActionPatch
{
    private static void Postfix() => ShipboardActionSimulation.Tick();
}
