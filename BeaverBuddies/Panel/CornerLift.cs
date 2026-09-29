using System;
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
    /// together, so they can't change places. Instead the panel is drawn from a layer of its own, the last thing in the
    /// part of the interface that holds both its corner and the alerts' corner ("Bottom-left"), so it is drawn after both.
    /// An empty slot the panel's size keeps its place in its corner, so the corner is laid out exactly as before, and the
    /// panel is placed over that slot each frame. The layer ignores the pointer; only the panel receives it. The layer
    /// carries the style sheets and font the panel had from the containers it left, so it looks the same.
    /// </remarks>
    internal sealed class CornerLift
    {
        VisualElement slot, layer, floated;
        // The panel's own margins while it floats (the slot takes them), to give back.
        StyleLength marginTop, marginBottom, marginLeft, marginRight;
        float nextSearch;
        bool loggedMissing, logged, failed;

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

            layer = new VisualElement { name = "BeaverBuddiesFrontLayer", pickingMode = PickingMode.Ignore };
            var l = layer.style;
            l.position = Position.Absolute;
            l.left = 0; l.top = 0; l.right = 0; l.bottom = 0;
            // What the panel had from the containers between the host and it: their style sheets, and the font.
            for (VisualElement e = corner; e != null && e != host; e = e.parent)
                for (int i = 0; i < e.styleSheets.count; i++) layer.styleSheets.Add(e.styleSheets[i]);
            l.unityFontDefinition = corner.resolvedStyle.unityFontDefinition;
            host.Add(layer);

            IResolvedStyle resolved = panel.resolvedStyle;
            slot = new VisualElement { name = "BeaverBuddiesConnectionPanelSlot", pickingMode = PickingMode.Ignore };
            var s = slot.style;
            s.marginTop = resolved.marginTop; s.marginBottom = resolved.marginBottom;
            s.marginLeft = resolved.marginLeft; s.marginRight = resolved.marginRight;
            s.flexShrink = 0;
            s.alignSelf = panel.style.alignSelf;
            s.width = panel.layout.width; s.height = panel.layout.height;
            corner.Insert(corner.IndexOf(panel), slot);

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
            if (panel.parent != layer || slot.parent == null || layer.parent == null) { Drop(); return; }
            VisualElement host = layer.parent;
            // Something added to the host after the layer would be drawn over the panel.
            if (host.IndexOf(layer) != host.childCount - 1) layer.BringToFront();
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
            slot?.RemoveFromHierarchy();
            layer?.RemoveFromHierarchy();
            slot = null; layer = null; floated = null;
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
