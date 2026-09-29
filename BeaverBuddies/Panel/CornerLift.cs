using System;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Panel
{
    /// <summary>
    /// Draws the panel in front of the game's alerts while it is on screen, and puts it back when it is hidden or moved.
    /// The alerts themselves are never moved.
    /// </summary>
    /// <remarks>
    /// Changing the order of the game's corner containers did not work in play (1.4.0-rc24, rc25): they are laid out
    /// together, so they can't change places. Instead the panel is drawn from a layer of its own, in the part of the
    /// interface that holds both its corner and the alerts' corner ("Bottom-left"), just before "Absolute-items". So it
    /// is drawn after the corners, and still under what the game draws over them: the entity panel and its messages
    /// ("Absolute-items"), then the windows, menus and dialogs ("Panels"). The layer is hidden while its corner is (the
    /// Batch Control window hides the left corners). An empty slot the panel's size keeps its place in its corner, so
    /// the corner is laid out exactly as before, and the panel is placed over that slot each frame. The layer ignores
    /// the pointer; only the panel receives it. The layer carries the style sheets and font the panel had from the
    /// containers it left, so it looks the same.
    /// </remarks>
    internal sealed class CornerLift
    {
        // What the game draws over the corners; the layer stays just before it.
        const string FrontName = "Absolute-items";

        readonly UILayout layout;
        VisualElement slot, layer, floated, front;
        // The panel's own margins while it floats (the slot takes them), to give back.
        StyleLength marginTop, marginBottom, marginLeft, marginRight;
        float nextSearch;
        bool loggedMissing, logged, failed;

        /// <param name="layout">The game's corners, which must know the slot's place among their panels.</param>
        public CornerLift(UILayout layout) { this.layout = layout; }

        public bool IsLifted => slot != null;

        /// <summary>Where the panel stands in its corner while it is drawn in front (the slot), or null.</summary>
        public VisualElement Anchor => slot;

        /// <summary>Draws the panel in front, or keeps it placed over its slot. Every frame while it is on screen.</summary>
        public void Lift(VisualElement panel)
        {
            if (failed) return;
            try
            {
                if (slot == null && !Float(panel)) return;
                Follow(panel);
            }
            catch (Exception error)
            {
                // Display only: if the game's interface is not what is expected, the panel stays where the game puts it.
                failed = true;
                Plugin.LogWarning("Chat: could not draw the panel in front of the alerts: " + error.Message);
                try { Restore(); } catch (Exception) { }
            }
        }

        bool Float(VisualElement panel)
        {
            VisualElement corner = panel.parent, root = panel.panel?.visualTree;
            if (corner == null || root == null || Time.unscaledTime < nextSearch) return false;
            nextSearch = Time.unscaledTime + 2;
            VisualElement alerts = root.Q<VisualElement>("Bottom-left");
            if (alerts == null || alerts == corner || alerts.Contains(panel) || panel.Contains(alerts))
            {
                if (!loggedMissing)
                {
                    loggedMissing = true;
                    Plugin.Log($"Chat: the panel is not drawn in front ('Bottom-left' {(alerts == null ? "not found" : "holds the panel")}); it sits in {Chain(corner)}.");
                }
                return false;
            }
            VisualElement host = CommonAncestor(corner, alerts);
            if (host == null) return false;
            // Drawn after the alerts' corner and before what the game draws over the corners, or not lifted at all.
            VisualElement found = host.Q<VisualElement>(FrontName);
            if (found == null || found.parent != host || host.IndexOf(found) < host.IndexOf(ChildHolding(host, alerts)))
            {
                if (!loggedMissing)
                {
                    loggedMissing = true;
                    Plugin.Log($"Chat: the panel is not drawn in front ('{FrontName}' is not after 'Bottom-left' in '{host.name}'); it sits in {Chain(corner)}.");
                }
                return false;
            }
            front = found;

            layer = new VisualElement { name = "BeaverBuddiesFrontLayer", pickingMode = PickingMode.Ignore };
            var l = layer.style;
            l.position = Position.Absolute;
            l.left = 0; l.top = 0; l.right = 0; l.bottom = 0;
            // What the panel had from the containers between the host and it: their style sheets, and the font.
            for (VisualElement e = corner; e != null && e != host; e = e.parent)
                for (int i = 0; i < e.styleSheets.count; i++) layer.styleSheets.Add(e.styleSheets[i]);
            l.unityFontDefinition = corner.resolvedStyle.unityFontDefinition;
            host.Insert(host.IndexOf(front), layer);

            IResolvedStyle resolved = panel.resolvedStyle;
            slot = new VisualElement { name = "BeaverBuddiesConnectionPanelSlot", pickingMode = PickingMode.Ignore };
            var s = slot.style;
            s.marginTop = resolved.marginTop; s.marginBottom = resolved.marginBottom;
            s.marginLeft = resolved.marginLeft; s.marginRight = resolved.marginRight;
            s.flexShrink = 0;
            s.alignSelf = panel.style.alignSelf;
            s.width = panel.layout.width; s.height = panel.layout.height;
            corner.Insert(corner.IndexOf(panel), slot);
            // The game places a panel added later to this corner by the order of those already in it (another mod's
            // would otherwise fail on the slot); the slot takes the panel's.
            if (layout != null && layout._elementOrder.TryGetValue(panel, out int order)) layout._elementOrder[slot] = order;

            var p = panel.style;
            marginTop = p.marginTop; marginBottom = p.marginBottom; marginLeft = p.marginLeft; marginRight = p.marginRight;
            p.marginTop = 0; p.marginBottom = 0; p.marginLeft = 0; p.marginRight = 0;
            p.position = Position.Absolute;
            layer.Add(panel);
            floated = panel;
            if (!logged)
            {
                logged = true;
                Plugin.Log($"Chat: the panel is drawn in front, from a layer at the end of '{host.name}' (its corner '{corner.name}', the alerts' '{alerts.name}').");
            }
            return true;
        }

        void Follow(VisualElement panel)
        {
            // Moved by something else since: forget the slot and the layer, leave the panel where it is.
            if (panel.parent != layer || slot.parent == null || layer.parent == null || front.parent != layer.parent) { Drop(); return; }
            VisualElement host = layer.parent;
            // Something added to the host between the layer and what is drawn over the corners would be drawn over the panel.
            if (host.IndexOf(layer) != host.IndexOf(front) - 1) layer.PlaceBehind(front);
            // The panel goes with its corner when the game hides it.
            bool shown = Displayed(slot, host);
            if ((layer.style.display == DisplayStyle.None) == shown) layer.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown) return;
            Rect size = panel.layout;
            if (!float.IsNaN(size.width) && !float.IsNaN(size.height))
            {
                if (Mathf.Abs(slot.resolvedStyle.width - size.width) > .5f) slot.style.width = size.width;
                if (Mathf.Abs(slot.resolvedStyle.height - size.height) > .5f) slot.style.height = size.height;
            }
            if (slot.style.alignSelf != panel.style.alignSelf) slot.style.alignSelf = panel.style.alignSelf;
            Vector2 at = layer.WorldToLocal(slot.worldBound.position);
            if (float.IsNaN(at.x) || float.IsNaN(at.y)) return;
            if (Mathf.Abs(panel.resolvedStyle.left - at.x) > .5f) panel.style.left = at.x;
            if (Mathf.Abs(panel.resolvedStyle.top - at.y) > .5f) panel.style.top = at.y;
        }

        /// <summary>Puts the panel back in its corner, where its slot is, as it was. Safe to call at any time.</summary>
        public void Restore()
        {
            VisualElement panel = floated;
            if (panel != null && panel.parent == layer && slot?.parent != null)
            {
                var p = panel.style;
                p.position = StyleKeyword.Null;
                p.left = StyleKeyword.Null; p.top = StyleKeyword.Null;
                p.marginTop = marginTop; p.marginBottom = marginBottom; p.marginLeft = marginLeft; p.marginRight = marginRight;
                slot.parent.Insert(slot.parent.IndexOf(slot), panel);
            }
            Drop();
        }

        void Drop()
        {
            if (slot != null) layout?._elementOrder.Remove(slot);
            slot?.RemoveFromHierarchy();
            layer?.RemoveFromHierarchy();
            slot = null; layer = null; floated = null; front = null;
        }

        // Hidden by the game: the element or one of its containers up to the host (the game hides a corner by its style).
        static bool Displayed(VisualElement element, VisualElement host)
        {
            for (VisualElement e = element; e != null && e != host; e = e.parent)
                if (e.style.display == DisplayStyle.None || e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        // The host's child that holds the element (or is it).
        static VisualElement ChildHolding(VisualElement host, VisualElement element)
        {
            VisualElement e = element;
            while (e != null && e.parent != host) e = e.parent;
            return e;
        }

        static VisualElement CommonAncestor(VisualElement a, VisualElement b)
        {
            for (VisualElement up = a; up != null; up = up.parent)
                if (up.Contains(b)) return up;
            return null;
        }

        static string Chain(VisualElement element)
        {
            string chain = "";
            for (VisualElement e = element; e != null; e = e.parent) chain += (chain.Length > 0 ? " < " : "") + (e.name ?? "");
            return chain;
        }
    }
}
