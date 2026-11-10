using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ostrabus.NavConsole;

internal static class Scrollbacks {
    static readonly AccessTools.FieldRef<GUIOrbitDraw, ScrollRect> NavLog = AccessTools.FieldRefAccess<GUIOrbitDraw, ScrollRect>("srLog");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, CanvasGroup> NavGroup = AccessTools.FieldRefAccess<GUIOrbitDraw, CanvasGroup>("cgStatus");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, RectTransform> NavDisplay = AccessTools.FieldRefAccess<GUIOrbitDraw, RectTransform>("rectDisplayPanel");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, bool> Editing = AccessTools.FieldRefAccess<GUIOrbitDraw, bool>("_editModeActive");
    static readonly AccessTools.FieldRef<GUIMessageDisplay, ScrollRect> CommsLog = AccessTools.FieldRefAccess<GUIMessageDisplay, ScrollRect>("srLog");
    static readonly AccessTools.FieldRef<GUIMessageDisplay, CanvasGroup> CommsGroup = AccessTools.FieldRefAccess<GUIMessageDisplay, CanvasGroup>("cgComms");
    static readonly AccessTools.FieldRef<ScrollRect, Bounds> ContentBounds = AccessTools.FieldRefAccess<ScrollRect, Bounds>("m_ContentBounds");
    static readonly AccessTools.FieldRef<ScrollRect, Bounds> ViewBounds = AccessTools.FieldRefAccess<ScrollRect, Bounds>("m_ViewBounds");
    static ConditionalWeakTable<GUIData, Surface> panels = new ConditionalWeakTable<GUIData, Surface>();
    static ConditionalWeakTable<GUIMessageDisplay, ScrollbackFollow> follows = new ConditionalWeakTable<GUIMessageDisplay, ScrollbackFollow>();
    static WeakReference<GUIDockSys> dockOwner;
    internal sealed class Surface {
        internal RawImage Image;
        internal Camera Camera;
        internal ScrollRect Scroll;
        internal CanvasGroup Group;
        internal GUIMessageDisplay Comms;
        internal bool Dragging;
        float grip;
        internal bool Visible => Image != null && Image.isActiveAndEnabled && Image.canvasRenderer.GetInheritedAlpha() > .001f &&
            Scroll != null && Scroll.isActiveAndEnabled && Group != null && Group.alpha >= .95f;
        internal bool Map(RectTransform target, Vector2 screen, out Vector2 local, out bool insideDisplay) {
            local = default; insideDisplay = false;
            if (Image == null || Camera == null || Camera.targetTexture == null || target == null || Image.canvas == null) return false;
            var rect = Image.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, Image.canvas.worldCamera, out var point) || rect.rect.width <= 0 || rect.rect.height <= 0) return false;
            insideDisplay = rect.rect.Contains(point);
            Rect uv = Image.uvRect;
            float x = uv.x + (point.x - rect.rect.xMin) / rect.rect.width * uv.width;
            float y = uv.y + (point.y - rect.rect.yMin) / rect.rect.height * uv.height;
            var texturePoint = new Vector2(x * Camera.targetTexture.width, y * Camera.targetTexture.height);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(target, texturePoint, Camera, out local);
        }
        internal bool Input(ScrollbackFollow follow) {
            var mouse = Mouse.current;
            if (!Visible || mouse == null) { Dragging = false; return false; }
            Vector2 screen = mouse.position.ReadValue();
            RectTransform viewport = Scroll.viewport != null ? Scroll.viewport : Scroll.transform as RectTransform;
            if (!Map(viewport, screen, out var cursor, out bool overDisplay)) { Dragging = false; return false; }
            if (!Application.isFocused || Info.activeThisFrame) { Dragging = false; return true; }
            bool moved = false;
            if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed) Dragging = false;
            var bar = Scroll.verticalScrollbar;
            if (bar != null && bar.handleRect != null && bar.IsActive() && bar.IsInteractable()) {
                var track = bar.handleRect.parent as RectTransform;
                bool vertical = bar.direction == Scrollbar.Direction.BottomToTop || bar.direction == Scrollbar.Direction.TopToBottom;
                bool reversed = bar.direction == Scrollbar.Direction.TopToBottom || bar.direction == Scrollbar.Direction.RightToLeft;
                if (vertical && Map(track, screen, out var point, out bool inside)) {
                    Rect bounds = track.rect;
                    if (bounds.height > 0) {
                        float coordinate = (point.y - bounds.yMin) / bounds.height;
                        if (mouse.leftButton.wasPressedThisFrame && inside && Map(bar.transform as RectTransform, screen, out var hit, out _) && ((RectTransform)bar.transform).rect.Contains(hit)) {
                            float center = ScrollbackPolicy.ThumbCenter(bar.value, bar.size, reversed);
                            grip = Math.Abs(coordinate - center) <= bar.size * .5f ? coordinate - center : 0;
                            Dragging = true;
                        }
                        if (Dragging && mouse.leftButton.isPressed) {
                            float value = ScrollbackPolicy.Drag(coordinate - grip, bar.size, reversed);
                            moved = Set(Scroll, value);
                            follow?.User(value, ContentRange(Scroll));
                        }
                    }
                }
            }
            float wheel = mouse.scroll.ReadValue().y;
            if (!Dragging && wheel != 0 && overDisplay && viewport.rect.Contains(cursor)) {
                float value = ScrollbackPolicy.Wheel(Scroll.verticalNormalizedPosition, ContentHeight(Scroll), ViewportHeight(Scroll), wheel);
                moved |= Set(Scroll, value);
                follow?.User(value, ContentRange(Scroll));
            }
            if (moved) Sound();
            return true;
        }
    }
    internal static float ContentHeight(ScrollRect scroll) {
        if (scroll == null || scroll.content == null) return 0;
        // ScrollRect already computes both bounds in viewport-local units. An
        // offscreen canvas can have an extremely small world scale; don't treat
        // it as empty or try to reconstruct these bounds from lossyScale.
        _ = scroll.verticalNormalizedPosition; // Refresh native bounds.
        return ContentBounds(scroll).size.y;
    }
    internal static float ViewportHeight(ScrollRect scroll) {
        if (scroll == null) return 0;
        _ = scroll.verticalNormalizedPosition;
        return ViewBounds(scroll).size.y;
    }
    internal static float ContentRange(ScrollRect scroll) {
        if (scroll == null) return 0;
        return ScrollbackPolicy.Range(ContentHeight(scroll), ViewportHeight(scroll));
    }
    static bool Set(ScrollRect scroll, float value) {
        if (!ScrollbackPolicy.Finite(value)) return false;
        float old = scroll.verticalNormalizedPosition;
        scroll.StopMovement(); scroll.verticalNormalizedPosition = ScrollbackPolicy.Clamp(value);
        return ScrollbackPolicy.Finite(old) && Math.Abs(scroll.verticalNormalizedPosition - old) > .000001f;
    }
    static void Sound() { if (AudioManager.am != null) AudioManager.am.PlayAudioEmitter("ShipUINSMapPan02", bLoop:false); }
    internal static void Bind(GUIData owner) {
        try {
            if (owner == null || GUIRenderTargets.goThis == null) return;
            if (owner is GUIDockSys dock) dockOwner = new WeakReference<GUIDockSys>(dock);
            var cameras = GUIRenderTargets.goThis.GetComponentsInChildren<Camera>(true);
            Camera renderCamera = null;
            string cameraName = owner is GUIOrbitDraw ? "CameraOrbitDraw" : "CameraDockSysDraw";
            foreach (var camera in cameras) {
                if (camera.name == cameraName) { renderCamera = camera; break; }
            }
            RawImage image = null;
            var binding = new Surface { Camera = renderCamera };
            if (owner is GUIOrbitDraw nav) {
                var rect = NavDisplay(nav);
                image = rect != null ? rect.GetComponent<RawImage>() : null;
                binding.Scroll = NavLog(nav); binding.Group = NavGroup(nav);
            } else if (owner is GUIDockSys) {
                var source = GUIRenderTargets.goThis.transform.Find("CanvasDockSysDraw");
                binding.Comms = source != null ? source.GetComponent<GUIMessageDisplay>() : null;
                if (binding.Comms != null) {
                    binding.Scroll = CommsLog(binding.Comms); binding.Group = CommsGroup(binding.Comms);
                }
                if (renderCamera != null && renderCamera.targetTexture != null)
                    foreach (var raw in owner.GetComponentsInChildren<RawImage>(true))
                        if (raw.texture == renderCamera.targetTexture) { image = raw; break; }
            }
            binding.Image = image;
            panels.Remove(owner); panels.Add(owner, binding);
            if (image == null || renderCamera == null || binding.Scroll == null)
                OstrabusPlugin.Logger?.LogWarning("[NavScrollbacks] " + owner.GetType().Name + ": image=" + (image != null) + ", camera=" + (renderCamera != null) + ", scroll=" + (binding.Scroll != null) + ". Retaining native input.");
        } catch (Exception error) { OstrabusPlugin.Logger?.LogWarning("[NavScrollbacks] " + error); }
    }
    internal static bool NavInput(GUIOrbitDraw owner) {
        if (owner == null || Editing(owner) || !panels.TryGetValue(owner, out var binding)) return true;
        // NoteShowing can remain true when our Hide nav tutorial note option
        // makes that object inactive. Vanilla
        // wheel input does not gate on it; neither should log input.
        return !binding.Input(null); // Fail open if the rendered surface is unavailable.
    }
    internal static void LegacyNavWheel(GUIOrbitDraw owner, float amount) {
        var group = NavGroup(owner); var scroll = NavLog(owner);
        if (group == null || group.alpha < .95f || scroll == null) return; // No hidden-log sounds.
        if (Set(scroll, ScrollbackPolicy.Wheel(scroll.verticalNormalizedPosition, ContentHeight(scroll), ViewportHeight(scroll), amount))) Sound();
    }
    static ScrollbackFollow Follow(GUIMessageDisplay owner) => follows.GetValue(owner, _ => new ScrollbackFollow());
    internal static void Open(GUIMessageDisplay owner) => Follow(owner).Open();
    internal static void BeforeAppend(GUIMessageDisplay owner) {
        var scroll = CommsLog(owner);
        if (scroll != null) Follow(owner).BeforeAppend(scroll.verticalNormalizedPosition, ContentRange(scroll));
    }
    internal static void AfterAppend(GUIMessageDisplay owner) {
        var scroll = CommsLog(owner);
        if (scroll == null) return; // ShowLog fills its text before making cgComms visible.
        var state = Follow(owner);
        if (!state.Pending) LayoutRebuilder.MarkLayoutForRebuild(scroll.transform as RectTransform);
        state.Appended(Time.frameCount);
    }
    internal static bool CommsInput(GUIMessageDisplay owner) {
        var group = CommsGroup(owner);
        var state = Follow(owner);
        if (group == null || group.alpha < .95f) { state.Cancel(); return false; }
        bool dragging = false;
        bool routed = false;
        if (dockOwner != null && dockOwner.TryGetTarget(out var dock) && dock != null && dock.isActiveAndEnabled && dock.bActive &&
            panels.TryGetValue(dock, out var binding) && binding.Comms == owner) {
            routed = binding.Input(state); dragging = binding.Dragging;
        }
        // Never cancel the initial jump just because another UI (map, tutorial
        // note, layout editor) is inactive or a surface binding failed.
        if (!routed && Mouse.current != null && Mouse.current.scroll.ReadValue().y != 0) state.Cancel();
        var log = CommsLog(owner);
        if (log != null && state.Apply(Time.frameCount, ContentRange(log), dragging, out float position)) Set(log, position);
        return !routed; // Unsupported/unbound displays retain their native Update.
    }
    // Read on demand by the existing inspection dispatcher. No diagnostic hooks,
    // scans, sampling buffers or recurring logging are installed.
    internal static Dictionary<string, object> Snapshot() {
        GUIOrbitDraw nav = GUIOrbitDraw.Instance;
        GUIDockSys dock = null;
        if (dockOwner != null) dockOwner.TryGetTarget(out dock);
        return new Dictionary<string, object> {
            ["focused"] = Application.isFocused, ["infoActive"] = Info.activeThisFrame,
            ["nav"] = Describe(nav), ["comms"] = Describe(dock),
            ["noteFlag"] = nav != null && nav.NoteShowing, ["noteHidden"] = TutorialNote.Hidden
        };
    }
    static Dictionary<string, object> Describe(GUIData owner) {
        if (owner == null) return new Dictionary<string, object> { ["owner"] = false };
        var result = new Dictionary<string, object> { ["owner"] = true, ["active"] = owner.bActive, ["enabled"] = owner.isActiveAndEnabled };
        if (!panels.TryGetValue(owner, out var surface)) { result["bound"] = false; return result; }
        result["bound"] = surface.Image != null && surface.Camera != null && surface.Scroll != null;
        result["visible"] = surface.Visible; result["dragging"] = surface.Dragging;
        result["panelAlpha"] = surface.Group != null ? (object)surface.Group.alpha : null;
        result["imageAlpha"] = surface.Image != null ? (object)surface.Image.canvasRenderer.GetInheritedAlpha() : null;
        if (surface.Scroll != null) {
            var scroll = surface.Scroll;
            result["position"] = scroll.verticalNormalizedPosition;
            result["contentHeight"] = ContentHeight(scroll); result["viewportHeight"] = ViewportHeight(scroll);
            result["range"] = ContentRange(scroll);
            result["worldScaleY"] = scroll.content != null ? (object)scroll.content.lossyScale.y : null;
            if (scroll.verticalScrollbar != null) { result["thumbSize"] = scroll.verticalScrollbar.size; result["thumbValue"] = scroll.verticalScrollbar.value; }
            var viewport = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
            if (Mouse.current != null) {
                bool mapped = surface.Map(viewport, Mouse.current.position.ReadValue(), out var local, out bool inside);
                result["pointerMapped"] = mapped; result["pointerInDisplay"] = inside;
                result["pointerInViewport"] = mapped && viewport.rect.Contains(local);
                result["pointerLocalX"] = local.x; result["pointerLocalY"] = local.y;
            }
            if (surface.Comms != null && follows.TryGetValue(surface.Comms, out var state)) {
                result["following"] = state.Following; result["pending"] = state.Pending;
            }
        }
        return result;
    }
    internal static void Stop() {
        panels = new ConditionalWeakTable<GUIData, Surface>();
        follows = new ConditionalWeakTable<GUIMessageDisplay, ScrollbackFollow>();
        dockOwner = null;
    }
}
