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
    /// colony's thing does not update, and the icon it holds is not drawn and can't be pointed at (its renderers and
    /// colliders switched off once, and switched on again if the thing becomes this colony's, through a Trading Post).
    /// An icon the game shows at once, as a status comes on (UpdateIcon, between two interval updates), is judged
    /// then too. Things of nobody's keep their icons. Display only: nothing simulated reads an icon.
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
            public readonly List<Collider> Disabled = new List<Collider>();
            public bool Applied => Hidden.Count > 0 || Disabled.Count > 0;
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
                // A status coming on shows its icon at once, without waiting for the interval update.
                MethodInfo show = AccessTools.DeclaredMethod(cycler, "UpdateIcon");
                if (show != null) harmony.Patch(show, postfix: new HarmonyMethod(typeof(StatusIconView), nameof(AfterUpdateIcon)));
                else Plugin.LogWarning("Status icons: StatusIconCycler.UpdateIcon is gone; another colony's new icon shows until the next update");
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
        private static bool SkipOtherColony(object __instance) => !IsOther(__instance);

        // Also runs as a status comes on, in the game's tick: IsOther never throws.
        private static void AfterUpdateIcon(object __instance) => IsOther(__instance);

        /// <summary>Whether this cycler's thing is another colony's (its icon then hidden). Rechecked every 30 frames.</summary>
        private static bool IsOther(object __instance)
        {
            if (failed || !(__instance is BaseComponent component)) return false;
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
                    // Hidden once as it becomes another colony's (again only while nothing was found to hide yet).
                    if (other && !state.Applied) Hide(__instance, state);
                    else if (!other && state.Other) Show(state);
                    state.Other = other;
                }
                return state.Other;
            }
            catch (Exception error)
            {
                // Display only: if the game changed, every icon is drawn as the game draws it.
                failed = true;
                Plugin.LogWarning("Status icons: could not hide the other colony's, so all are drawn: " + error.Message);
                foreach (Seen state in seen.Values) Show(state);
                seen.Clear();
                return false;
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
                    : heldObject ? heldObject.GetComponentsInChildren<Renderer>(true)
                    : Enumerable.Empty<Renderer>();
                foreach (Renderer r in renderers)
                {
                    if (!r || !r.enabled) continue;
                    r.enabled = false;
                    state.Hidden.Add(r);
                }
                // The icon's collider is what the pointer finds: hidden, it must not be found either.
                if (held is Renderer || !heldObject) continue;
                foreach (Collider collider in heldObject.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider || !collider.enabled) continue;
                    collider.enabled = false;
                    state.Disabled.Add(collider);
                }
            }
        }

        private static void Show(Seen state)
        {
            foreach (Renderer r in state.Hidden) if (r) r.enabled = true;
            foreach (Collider c in state.Disabled) if (c) c.enabled = true;
            state.Hidden.Clear();
            state.Disabled.Clear();
        }

        // Things that are gone keep nothing here.
        private static void Forget()
        {
            foreach (object gone in seen.Keys.Where(k => !(k is BaseComponent c) || !c).ToList()) seen.Remove(gone);
        }
    }
}
