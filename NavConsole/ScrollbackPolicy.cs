using System;

namespace Ostrabus.NavConsole;

// Scroll positions use viewport/content units, independent of the drawn thumb.
internal static class ScrollbackPolicy {
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static float Clamp(float value) => Math.Max(0, Math.Min(1, value));
    internal static float Range(float content, float viewport) => Finite(content) && Finite(viewport) && viewport > 0 ? Math.Max(0, content - viewport) : 0;
    internal static float Wheel(float position, float content, float viewport, float wheel) {
        float range = Range(content, viewport);
        if (!Finite(position) || !Finite(wheel) || range <= 0 || wheel == 0) return position;
        return Clamp(position + Math.Sign(wheel) * viewport * .2f / range);
    }
    internal static float Drag(float center, float thumbSize, bool reversed) {
        float travel = 1 - Clamp(thumbSize);
        if (!Finite(center) || !Finite(thumbSize) || travel <= .0001f) return 0;
        float value = Clamp((center - thumbSize * .5f) / travel);
        return reversed ? 1 - value : value;
    }
    internal static float ThumbCenter(float value, float size, bool reversed) => Clamp(size) * .5f + (reversed ? 1 - Clamp(value) : Clamp(value)) * (1 - Clamp(size));
}

internal sealed class ScrollbackFollow {
    internal bool Following = true, Pending, Opening = true;
    internal float Top;
    internal int DueFrame;
    internal void Open() { Following = true; Pending = false; Opening = true; }
    internal void Cancel() { Pending = false; }
    internal void User(float position, float range) {
        Pending = false;
        Opening = false;
        Following = range <= 0 || position * range <= 2;
    }
    internal void BeforeAppend(float position, float range) {
        if (Pending) return; // One anchor for a batch; don't read stale layout repeatedly.
        if (Opening) { Opening = false; Top = 0; return; } // Ignore the previous conversation's scroll position.
        // Also recognize reaching bottom through a native fallback handler.
        Following = range <= 0 || position * range <= 2;
        Top = (1 - ScrollbackPolicy.Clamp(position)) * range;
    }
    internal void Appended(int frame) {
        if (Pending) return;
        Pending = true; DueFrame = frame + 2;
    }
    internal bool Apply(int frame, float range, bool dragging, out float position) {
        position = 0;
        if (!Pending || dragging || frame < DueFrame) return false;
        Pending = false;
        position = Following || range <= 0 ? 0 : ScrollbackPolicy.Clamp(1 - Top / range);
        return true;
    }
}
