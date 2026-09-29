using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.BaseComponentSystem;
using Timberborn.EntitySystem;
using UnityEngine;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// The status icons over beavers and buildings (hungry, thirsty, unstaffed…) show only over this player's own colony.
    /// The alert list already counts only this colony (ColonyView); the icons are drawn by each thing's own
    /// StatusIconCycler, which the alert filter never reached. On a computer that shows one colony, the cycler of another
    /// colony's thing does not update, and the icon it holds is not drawn (its renderers switched off, and switched on
    /// again if the thing becomes this colony's, through a Trading Post). Things of nobody's keep their icons. Display
    /// only: nothing simulated reads an icon.
    /// </summary>
    public static class StatusIconView
    {
        private const string CyclerName = "Timberborn.StatusSystem.StatusIconCycler";
        private const int RecheckFrames = 30;

        private static readonly Dictionary<object, Seen> seen = new Dictionary<object, Seen>();
        // How to reach what the cycler draws: its fields that hold a renderer, a game object or a transform.
        private static FieldInfo[] iconFields = new FieldInfo[0];
        private static bool failed;

        private class Seen
        {
            public int Frame;
            public bool Other;
            public readonly List<Renderer> Hidden = new List<Renderer>();
        }

        internal static void Install(Harmony harmony)
        {
            try
            {
                Type cycler = AccessTools.TypeByName(CyclerName);
                MethodInfo update = cycler == null ? null : AccessTools.DeclaredMethod(cycler, "IntervalUpdate");
                if (update == null)
                {
                    Plugin.LogWarning("Status icons: " + (cycler == null ? CyclerName + " is gone" : "StatusIconCycler.IntervalUpdate is gone") + "; the other colony's icons stay drawn");
                    return;
                }
                iconFields = IconFields(cycler).ToArray();
                harmony.Patch(update, prefix: new HarmonyMethod(typeof(StatusIconView), nameof(SkipOtherColony)));
                Plugin.Log("Status icons show only over your own colony (StatusIconCycler.IntervalUpdate; icon held in "
                    + (iconFields.Length == 0 ? "no field" : string.Join(", ", iconFields.Select(f => f.Name + ":" + f.FieldType.Name))) + ")");
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Status icons: the other colony's stay drawn: " + error.Message);
            }
        }

        /// <summary>The cycler's fields that hold what it draws (a renderer, a game object or a transform).</summary>
        internal static IEnumerable<FieldInfo> IconFields(Type cycler) =>
            cycler.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => typeof(Renderer).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(GameObject) || f.FieldType == typeof(Transform));

        // Harmony: false skips the cycler's interval update (another colony's thing: no icon is picked or shown).
        private static bool SkipOtherColony(object __instance)
        {
            if (failed || !(__instance is BaseComponent component)) return true;
            try
            {
                if (!seen.TryGetValue(__instance, out Seen state))
                {
                    state = new Seen { Frame = int.MinValue };
                    seen[__instance] = state;
                    if (seen.Count > 4096) Forget();
                }
                int frame = Time.frameCount;
                if (frame - state.Frame >= RecheckFrames)
                {
                    state.Frame = frame;
                    bool other = component && ColonyViewService.ActiveThisFrame(out _) && !ColonyViewService.IsOwn(component);
                    if (other != state.Other || other) { state.Other = other; if (other) Hide(__instance, state); else Show(state); }
                }
                return !state.Other;
            }
            catch (Exception error)
            {
                // Display only: if the game changed, every icon is drawn as the game draws it.
                failed = true;
                Plugin.LogWarning("Status icons: could not hide the other colony's, so all are drawn: " + error.Message);
                foreach (Seen state in seen.Values) Show(state);
                seen.Clear();
                return true;
            }
        }

        private static void Hide(object cycler, Seen state)
        {
            // Never the thing itself: only what the cycler holds apart from it (the icon).
            GameObject self = (cycler as BaseComponent)?.GetComponent<EntityComponent>()?.GameObject;
            foreach (FieldInfo field in iconFields)
            {
                object held = field.GetValue(cycler);
                GameObject heldObject = held is GameObject g ? g : held is Component c && c ? c.gameObject : null;
                if (heldObject && self && (heldObject == self || self.transform.IsChildOf(heldObject.transform))) continue;
                IEnumerable<Renderer> renderers =
                    held is Renderer renderer ? new[] { renderer }
                    : held is GameObject gameObject && gameObject ? gameObject.GetComponentsInChildren<Renderer>(true)
                    : held is Transform transform && transform ? transform.GetComponentsInChildren<Renderer>(true)
                    : Enumerable.Empty<Renderer>();
                foreach (Renderer r in renderers)
                {
                    if (!r || !r.enabled) continue;
                    r.enabled = false;
                    state.Hidden.Add(r);
                }
            }
        }

        private static void Show(Seen state)
        {
            foreach (Renderer r in state.Hidden) if (r) r.enabled = true;
            state.Hidden.Clear();
        }

        // Things that are gone keep nothing here.
        private static void Forget()
        {
            foreach (object gone in seen.Keys.Where(k => !(k is BaseComponent c) || !c).ToList()) seen.Remove(gone);
        }
    }
}
