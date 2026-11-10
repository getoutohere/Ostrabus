using System;
using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace Ostrabus.Hotkeys;

internal readonly struct CrewShortcut {
    internal readonly Key Key;
    internal readonly bool Shift, Ctrl, Alt, Meta;
    internal CrewShortcut(Key key, bool shift, bool ctrl, bool alt, bool meta) {
        Key = key; Shift = shift; Ctrl = ctrl; Alt = alt; Meta = meta;
    }
    internal static bool MainKey(Key key) => key != Key.None && key != Key.Escape && key != Key.LeftShift && key != Key.RightShift &&
        key != Key.LeftCtrl && key != Key.RightCtrl && key != Key.LeftAlt && key != Key.RightAlt && key != Key.LeftMeta && key != Key.RightMeta;
    internal bool Reserved => Shift && !Ctrl && !Alt && !Meta && (Key == Key.R || Key == Key.T);
    internal static bool TryParse(string value, out CrewShortcut shortcut) {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('+');
        bool shift = false, ctrl = false, alt = false, meta = false;
        Key key = Key.None;
        foreach (var raw in parts) {
            string part = raw.Trim();
            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
            else if (part.Equals("Meta", StringComparison.OrdinalIgnoreCase)) meta = true;
            else if (key != Key.None || !Enum.TryParse(part, true, out key) || !Enum.IsDefined(typeof(Key), key) || !MainKey(key)) return false;
        }
        var candidate = new CrewShortcut(key, shift, ctrl, alt, meta);
        if (!MainKey(key) || candidate.Reserved) return false;
        shortcut = candidate;
        return true;
    }
    internal static CrewShortcut Capture(Keyboard keyboard, Key key) => new CrewShortcut(key, keyboard.shiftKey.isPressed,
        keyboard.ctrlKey.isPressed, keyboard.altKey.isPressed, keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed);
    internal bool Pressed(Keyboard keyboard) => MainKey(Key) && keyboard[Key].wasPressedThisFrame &&
        Shift == keyboard.shiftKey.isPressed && Ctrl == keyboard.ctrlKey.isPressed && Alt == keyboard.altKey.isPressed &&
        Meta == (keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed);
    public override string ToString() => (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + (Meta ? "Meta+" : "") + Key;
}

internal static class EquipmentShortcut {
    internal const string Default = "Shift+F";
    internal static ConfigEntry<string> Binding;
    static string parsed;
    static CrewShortcut shortcut;
    internal static string Value => Binding?.Value ?? Default;
    internal static void Set(string value) {
        if (!CrewShortcut.TryParse(value, out var candidate)) throw new ArgumentException("Choose a keyboard shortcut other than Esc, Shift+R or Shift+T.");
        if (Binding == null) throw new InvalidOperationException("Equipment shortcut is not initialized.");
        Binding.Value = candidate.ToString();
    }
    internal static bool Pressed(Keyboard keyboard) {
        string value = Value;
        if (parsed != value) {
            parsed = value;
            // Invalid hand-edited config disables this shortcut, not the permission keys.
            CrewShortcut.TryParse(value, out shortcut);
        }
        return shortcut.Pressed(keyboard);
    }
}
