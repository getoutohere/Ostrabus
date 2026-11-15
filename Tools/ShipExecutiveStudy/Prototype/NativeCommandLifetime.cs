using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

// Explicit adapter for the assembly-bound TakeFuel layout. Only new commands
// execute their native constructor, at receipt. Reconstruction restores state;
// it must not execute SyncFuel or start another absolute pause.
static class NativeCommandLifetime
{
    internal static void Capture(ICommand command, NativeDirective input, Ship ship)
    {
        if (input == null || !(command is TakeFuel))
            return;
        input.ConstructorRan = true;
        input.NativeState = command.SaveData == null ? null : (string[])command.SaveData.Clone();
        input.PauseUntil = ship.fAIPauseTimer;
        input.DockingRemaining = ship.fAIDockingExpire;
    }

    internal static TakeFuel RestoreTakeFuel(Ship ship, string[] state)
    {
        var fields = typeof(TakeFuel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (fields.Length != 1 || fields[0].Name != "_timer" || fields[0].FieldType != typeof(double) || state == null || state.Length != 1)
            throw new InvalidOperationException("Unqualified TakeFuel restoration layout/payload");
        var command = NativeWorld.Empty<TakeFuel>();
        // BaseCommand's reviewed ctor initializes only _nodeName and ShipUs.
        // Its status objects and auto-properties start null and are lazy.
        AccessTools.Field(typeof(BaseCommand), "_shipUs").SetValue(command, ship);
        AccessTools.Field(typeof(BaseCommand), "_nodeName").SetValue(command, "");
        command.SaveData = (string[])state.Clone();
        return command;
    }
}
