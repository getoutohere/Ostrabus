using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ostrabus.NavConsole;

internal sealed class RotaryKnob : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler {
    internal Action<int> Callback;
    internal int State { get; private set; }
    RectTransform visual;
    float[] angles;
    string sound;
    Vector2 pressedAt;
    int pressedState;
    bool pressed, dragged;
    internal void Init(RectTransform image, float[] detents, string clickSound) { visual = image; angles = detents; sound = clickSound; }
    internal static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
    internal static Vector2 DetentPosition(int state) {
        if (state < 0 || state > 3) throw new ArgumentOutOfRangeException(nameof(state));
        float radians = (165f - state * 50f) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 20f;
    }
    internal static int DragState(int start, float dx, float dy) => Math.Max(0, Math.Min(3, start + (int)((dx + dy) / 24f)));
    internal void SetStateSilent(int state) {
        State = Math.Max(0, Math.Min(3, state));
        if (visual != null) visual.localRotation = Quaternion.Euler(0, 0, angles[State]);
    }
    void Change(int state) {
        state = Math.Max(0, Math.Min(3, state));
        if (state == State) return;
        SetStateSilent(state);
        Callback?.Invoke(state);
        if (!string.IsNullOrEmpty(sound)) AudioManager.am.PlayAudioEmitter(sound, bLoop: false);
    }
    public void OnPointerDown(PointerEventData data) {
        if (data.button != PointerEventData.InputButton.Left && data.button != PointerEventData.InputButton.Right) return;
        pressed = RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, data.position, data.pressEventCamera, out pressedAt);
        pressedState = State; dragged = false;
    }
    public void OnDrag(PointerEventData data) {
        if (!pressed || !RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, data.position, data.pressEventCamera, out var local)) return;
        Vector2 delta = local - pressedAt;
        if (delta.sqrMagnitude > 16) dragged = true;
        if (dragged) Change(DragState(pressedState, delta.x, delta.y));
    }
    public void OnPointerUp(PointerEventData data) {
        if (!pressed) return;
        pressed = false;
        if (!dragged) Change(State + (data.button == PointerEventData.InputButton.Left ? -1 : 1));
    }
    void OnDisable() { pressed = dragged = false; }
    void OnDestroy() { Callback = null; visual = null; angles = null; }
}
