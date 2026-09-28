using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Panel
{
    /// <summary>
    /// Keeps the game's alerts off the panel while it is on screen, so they cannot draw over it and its chat, and puts
    /// everything back when it is hidden or moved. Two ways: the panel's corner is drawn in front where that is safe,
    /// and the alerts are moved to the right of the panel wherever their rows would meet it.
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

        // The game's alert list moved aside (display only: a translation, which changes no one's layout), and by how much.
        VisualElement alerts, shifted;
        float shift, nextSearch;
        bool alertsLogged, shiftLogged, alertsFailed;

        public bool IsLifted => lifted != null || raised.Count > 0;

        /// <summary>Moves the panel's corner in front of the other corners. Does nothing if it already is, or if it is not safe.</summary>
        public void Lift(VisualElement panel)
        {
            KeepAlertsClear(panel);
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

        // The drawing order alone did not keep the alerts off the panel in play (1.4.0-rc24, rc25): the corners may not
        // be laid out on their own, or the alerts may be drawn elsewhere. So wherever the alert rows would meet the panel,
        // the alerts are moved to the right of it, measured each frame (a new alert makes the list taller).
        void KeepAlertsClear(VisualElement panel)
        {
            if (alertsFailed) return;
            try
            {
                VisualElement found = FindAlerts(panel);
                if (found != shifted) Unshift();
                if (found == null || panel.resolvedStyle.display == DisplayStyle.None) { Unshift(); return; }
                Rect box = panel.worldBound;
                // The rows as the game lays them out: what is measured includes the move as last drawn, which the
                // resolved style gives (the one just asked for may not be drawn yet).
                float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
                Measure(found, ref left, ref top, ref right, ref bottom);
                float drawn = shifted == found ? found.resolvedStyle.translate.x : 0;
                float needed = left == float.MaxValue ? 0
                    : PanelLayout.AlertShift(box.xMin, box.yMin, box.xMax, box.yMax, left - drawn, top, right - drawn, bottom);
                if (Mathf.Abs(needed - shift) < 1) return;
                if (needed <= 0) { Unshift(); return; }
                found.style.translate = new Translate(needed, 0, 0);
                shifted = found;
                shift = needed;
                if (!shiftLogged)
                {
                    shiftLogged = true;
                    Plugin.Log($"Chat: the alerts in '{found.name}' are moved {needed:0} to the right, beside the connection panel.");
                }
            }
            catch (Exception error)
            {
                // Display only: if the game's interface is not what is expected, leave the alerts where the game puts them.
                alertsFailed = true;
                Plugin.LogWarning("Chat: could not move the alerts beside the panel: " + error.Message);
                try { Unshift(); } catch (Exception) { }
            }
        }

        // The game's alerts live in its bottom-left corner. Never the panel's own corner, nor anything holding the panel.
        VisualElement FindAlerts(VisualElement panel)
        {
            if (alerts != null && alerts.panel == panel.panel && !alerts.Contains(panel)) return alerts;
            alerts = null;
            VisualElement root = panel.panel?.visualTree;
            // Looking through the whole interface is not done every frame.
            if (root == null || Time.unscaledTime < nextSearch) return null;
            nextSearch = Time.unscaledTime + 2;
            VisualElement found = root.Q<VisualElement>("Bottom-left")
                ?? root.Query<VisualElement>().Where(e => Plain(e.name) == "bottomleft").First();
            if (found != null && !found.Contains(panel)) alerts = found;
            else if (!alertsLogged)
            {
                alertsLogged = true;
                string chain = "";
                for (VisualElement e = panel.parent; e != null; e = e.parent) chain += (chain.Length > 0 ? " < " : "") + (e.name ?? "");
                Plugin.Log($"Chat: the game's alert corner was {(found == null ? "not found" : "the panel's own")}; the panel sits in {chain}.");
            }
            return alerts;
        }

        static string Plain(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var letters = new System.Text.StringBuilder(name.Length);
            foreach (char c in name) if (char.IsLetter(c)) letters.Append(char.ToLowerInvariant(c));
            return letters.ToString();
        }

        // The screen area of what is drawn: the innermost elements on screen (a row's icon and text), skipping anything
        // hidden, so a container stretched down the whole corner does not count as rows.
        static void Measure(VisualElement element, ref float left, ref float top, ref float right, ref float bottom)
        {
            IResolvedStyle style = element.resolvedStyle;
            if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden || style.opacity <= 0) return;
            int children = element.hierarchy.childCount;
            if (children == 0)
            {
                Rect r = element.worldBound;
                if (float.IsNaN(r.width) || float.IsNaN(r.height) || r.width < 1 || r.height < 1) return;
                left = Mathf.Min(left, r.xMin); top = Mathf.Min(top, r.yMin);
                right = Mathf.Max(right, r.xMax); bottom = Mathf.Max(bottom, r.yMax);
                return;
            }
            for (int i = 0; i < children; i++) Measure(element.hierarchy[i], ref left, ref top, ref right, ref bottom);
        }

        void Unshift()
        {
            VisualElement moved = shifted;
            shifted = null;
            shift = 0;
            if (moved != null) moved.style.translate = StyleKeyword.Null;
        }

        /// <summary>Puts the corner back exactly where it was in the drawing order. Safe to call at any time.</summary>
        public void Restore()
        {
            Unshift();
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
