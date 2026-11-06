using System;
using UnityEngine;

namespace Ostrabus.NavConsole;

internal static class RoundKnobArt {
    static Texture2D texture;
    static Sprite sprite;
    internal static Sprite Get() {
        if (sprite != null) return sprite;
        using (var stream = typeof(RoundKnobArt).Assembly.GetManifestResourceStream("Ostrabus.NavConsole.RoundKnob.png")) {
            if (stream == null) throw new InvalidOperationException("Embedded proximity knob image is missing.");
            var bytes = new byte[checked((int)stream.Length)];
            int offset = 0;
            while (offset < bytes.Length) {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new System.IO.EndOfStreamException();
                offset += count;
            }
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Ostrabus.RoundKnob", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            try {
                if (!ImageConversion.LoadImage(texture, bytes, true)) throw new InvalidOperationException("Could not decode proximity knob PNG.");
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                sprite.name = "Ostrabus.RoundKnob"; sprite.hideFlags = HideFlags.DontSave;
            } catch { Release(); throw; }
        }
        return sprite;
    }
    internal static void Release() {
        if (sprite != null) UnityEngine.Object.Destroy(sprite);
        if (texture != null) UnityEngine.Object.Destroy(texture);
        sprite = null; texture = null;
    }
}
