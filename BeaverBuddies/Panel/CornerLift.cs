using System.Collections.Generic;
using UnityEngine.UIElements;

namespace BeaverBuddies.Panel
{
    /// <summary>
    /// Keeps the panel in front of the rest of the game's interface while it is on screen, so the alerts at the bottom
    /// of the screen cannot draw over it and its chat, and puts everything back when it is hidden or moved.
    /// </summary>
    /// <remarks>
    /// The game's interface is a set of corner containers ("Top-left", "Bottom-left", and so on) that are drawn in the
    /// order they were defined; the alerts live in "Bottom-left", which comes after "Top-left", so they cover a tall
    /// panel in the top-left corner. Nothing here can change what is clickable: the game defines every container as
    /// ignoring the pointer, and only what is inside them receives it.
    /// </remarks>
    internal sealed class CornerLift
    {
        static readonly HashSet<string> Corners = new HashSet<string>
        {
            "Top-left", "Top-right", "Top-bar", "Bottom-left", "Bottom-right", "Bottom-bar", "Absolute-items"
        };

        VisualElement lifted, wasBehind;
        // The containers above the corner that were moved to the front too, each with what was behind it.
        readonly List<(VisualElement Element, VisualElement Behind)> raised = new List<(VisualElement, VisualElement)>();
        bool warned;

        public bool IsLifted => lifted != null || raised.Count > 0;

        /// <summary>Moves the panel's corner in front of the other corners. Does nothing if it already is, or if it is not safe.</summary>
        public void Lift(VisualElement panel)
        {
            if (lifted != null || raised.Count > 0) { RaiseContainers(lifted?.parent ?? panel.parent?.parent); return; }
            VisualElement corner = panel.parent, holder = corner?.parent;
            if (corner == null || holder == null || !Corners.Contains(corner.name)) return;
            // A corner that is laid out on its own can change places without moving anything else. If the game ever
            // changes that, leave the order alone rather than shuffle the whole interface.
            if (corner.resolvedStyle.position != Position.Absolute)
            {
                if (!warned) { warned = true; Plugin.Log($"Chat: '{corner.name}' is not positioned on its own, so it was not moved in front of the alerts."); }
                return;
            }
            int mine = holder.IndexOf(corner);
            VisualElement front = null;
            for (int i = 0; i < holder.childCount; i++)
                if (holder[i] != corner && Corners.Contains(holder[i].name)) front = holder[i];
            // Already drawn after every other corner: only the containers above may still be behind something.
            if (front != null && holder.IndexOf(front) >= mine && mine + 1 < holder.childCount)
            {
                wasBehind = holder[mine + 1];
                corner.PlaceInFront(front);
                lifted = corner;
                if (!warned) { warned = true; Plugin.Log($"Chat: '{corner.name}' is drawn in front of '{front.name}' while the panel is on screen."); }
            }
            RaiseContainers(holder);
        }

        // The alerts may live in another container of the interface that is drawn after the whole holder of the
        // corners: then the corner alone is not enough, so each container above it is also drawn last among its
        // siblings (nothing is clickable that was not: the game's containers ignore the pointer).
        void RaiseContainers(VisualElement holder)
        {
            if (holder == null) return;
            for (VisualElement element = holder; element?.parent != null; element = element.parent)
            {
                VisualElement parent = element.parent;
                int index = parent.IndexOf(element);
                if (index < 0 || index == parent.childCount - 1) continue;
                // Something was added after it since: draw it last again, keeping what was recorded to restore.
                bool known = raised.Exists(r => r.Element == element);
                if (!known) raised.Add((element, parent[index + 1]));
                element.BringToFront();
                if (!warned && !known) Plugin.Log($"Chat: '{element.name}' was moved to the front of '{parent.name}' so nothing is drawn over the panel.");
            }
            warned = true;
        }

        /// <summary>Puts the corner back exactly where it was in the drawing order. Safe to call at any time.</summary>
        public void Restore()
        {
            VisualElement corner = lifted, next = wasBehind;
            lifted = null; wasBehind = null;
            // Outermost first was raised last; put them back in the opposite order.
            for (int i = raised.Count - 1; i >= 0; i--)
            {
                var (element, behind) = raised[i];
                if (behind != null && behind.parent != null && behind.parent == element.parent) element.PlaceBehind(behind);
            }
            raised.Clear();
            if (corner == null) return;
            // If either has been moved or removed since, there is nothing sensible to restore to.
            if (next != null && next.parent != null && next.parent == corner.parent) corner.PlaceBehind(next);
        }
    }
}
