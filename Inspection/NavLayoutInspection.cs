using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NavDraggable = Ostranauts.ShipGUIs.NavStation.Draggable;

namespace Ostrabus.Inspection;

// Request-only scene inspection. Never enables modules, changes their layout,
// evaluates native fit methods or sends pointer events.
internal static class NavLayoutInspection
{
    const int Limit = 64;
    static readonly AccessTools.FieldRef<NavModBase, RectTransform> Container =
        AccessTools.FieldRefAccess<NavModBase, RectTransform>("_container");
    static readonly AccessTools.FieldRef<NavModBase, CondOwner> Owner =
        AccessTools.FieldRefAccess<NavModBase, CondOwner>("COSelf");
    static readonly FieldInfo State = AccessTools.Field(typeof(NavModBase), "Modstate")
        ?? throw new System.MissingFieldException(typeof(NavModBase).FullName, "Modstate");
    static readonly AccessTools.FieldRef<NavModCoursePlot, Slider> Drift =
        AccessTools.FieldRefAccess<NavModCoursePlot, Slider>("slidCrsCoast");
    static readonly AccessTools.FieldRef<NavModWrapper, string> WrapperName =
        AccessTools.FieldRefAccess<NavModWrapper, string>("_navModName");
    static readonly AccessTools.FieldRef<NavModWrapper, NavDraggable> Dragged =
        AccessTools.FieldRefAccess<NavModWrapper, NavDraggable>("_draggedMod");

    static Dictionary<string, object> Map(params object[] values) => SnapshotJson.Map(values);
    static object Vector(Vector2 value) => Map("x", value.x, "y", value.y);
    static object Vector(Vector3 value) => Map("x", value.x, "y", value.y, "z", value.z);

    static object Rect(RectTransform rect)
    {
        if (rect == null) return null;
        return Map("name", rect.name, "activeSelf", rect.gameObject.activeSelf,
            "activeInHierarchy", rect.gameObject.activeInHierarchy,
            "anchorMin", Vector(rect.anchorMin), "anchorMax", Vector(rect.anchorMax),
            "pivot", Vector(rect.pivot),
            "anchoredPosition", Vector(rect.anchoredPosition), "sizeDelta", Vector(rect.sizeDelta),
            "size", Vector(rect.rect.size), "localScale", Vector(rect.localScale),
            "worldScale", Vector(rect.lossyScale), "worldPosition", Vector(rect.position));
    }

    static object Group(CanvasGroup group) => group == null ? null : Map(
        "name", group.name, "alpha", group.alpha, "interactable", group.interactable,
        "blocksRaycasts", group.blocksRaycasts, "ignoreParentGroups", group.ignoreParentGroups);

    static object SliderState(Slider slider)
    {
        if (slider == null) return null;
        var components = new List<string>();
        foreach (var component in slider.GetComponents<Component>())
            components.Add(component == null ? "missing-script" : component.GetType().FullName);
        return Map("type", slider.GetType().FullName, "enabled", slider.enabled,
            "active", slider.isActiveAndEnabled, "interactable", slider.interactable,
            "value", slider.value, "min", slider.minValue, "max", slider.maxValue,
            "rect", Rect(slider.transform as RectTransform), "components", components);
    }

    internal static object Snapshot()
    {
        var nav = GUIOrbitDraw.Instance;
        if (nav == null) return Map("present", false);
        var modules = new List<object>();
        int examined = 0;
        foreach (Transform child in nav.transform)
        {
            if (++examined > 256 || modules.Count == Limit) break;
            var module = child.GetComponent<NavModBase>();
            if (module == null) continue;
            var container = Container(module);
            var groups = new List<object>();
            Transform parent = container;
            for (int depth = 0; parent != null && depth < 8; depth++, parent = parent.parent)
            {
                foreach (var group in parent.GetComponents<CanvasGroup>()) groups.Add(Group(group));
                if (parent == nav.transform) break;
            }
            var co = Owner(module);
            string position = null;
            if (co?.mapGUIPropMaps != null && co.mapGUIPropMaps.TryGetValue("NavModConfig", out var config))
                config.TryGetValue(module.name, out position);
            var row = Map("name", child.name, "type", module.GetType().FullName,
                "activeSelf", child.gameObject.activeSelf, "activeInHierarchy", child.gameObject.activeInHierarchy,
                "enabled", module.enabled, "state", State?.GetValue(module)?.ToString(),
                "root", Rect(child as RectTransform), "container", Rect(container),
                "canvasGroups", groups, "consoleId", co?.strID, "savedPosition", position,
                "draggablePresent", module.DraggableRef != null,
                "draggableEnabled", module.DraggableRef == null ? null : (object)module.DraggableRef.enabled,
                "fits", module.DraggableRef == null ? null : (object)module.DraggableRef.Fits);
            if (module is NavModCoursePlot course)
            {
                row["accelerationSlider"] = SliderState(course.slidCrsLim);
                row["driftSlider"] = SliderState(Drift(course));
            }
            modules.Add(row);
        }
        var wrappers = new List<object>();
        var menu = nav.GetComponentInChildren<EditMenu>(true);
        if (menu != null)
        {
            foreach (var wrapper in menu.GetComponentsInChildren<NavModWrapper>(true))
            {
                if (wrappers.Count == Limit) break;
                wrappers.Add(Map("module", WrapperName(wrapper), "active", wrapper.gameObject.activeInHierarchy,
                    "draggedModule", Dragged(wrapper)?.transform.parent?.name,
                    "rect", Rect(wrapper.transform as RectTransform)));
            }
        }
        return Map("present", true, "active", nav.gameObject.activeInHierarchy,
            "navRect", Rect(nav.transform as RectTransform), "nativeModuleCount", nav.NavModCount,
            "editMenuActive", menu != null && menu.gameObject.activeInHierarchy,
            "nativeBeingDragged", NavDraggable.IsBeingDragged, "modules", modules,
            "modulesTruncated", examined > 256 || modules.Count == Limit, "palette", wrappers,
            "selectedObject", EventSystem.current?.currentSelectedGameObject?.name,
            "note", "Read-only current console subtree, including inactive modules. No resource loading, fit evaluation, layout writes or input events.");
    }
}
