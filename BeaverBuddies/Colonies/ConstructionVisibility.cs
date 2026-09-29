using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// Another player's construction, shown or hidden on this computer (the eye button on the connection panel): what
    /// they are still building, never their finished buildings. Hiding only switches off the drawing of those sites; they stay in the game, simulated as ever, so nothing
    /// differs between computers. Shown by default. Kept for the session, by colony slot.
    /// </summary>
    public class ConstructionVisibility : RegisteredSingleton, IUpdatableSingleton
    {
        private const float RefreshSeconds = .5f;

        private static readonly HashSet<int> hiddenSlots = new HashSet<int>();
        private static bool changed;

        private readonly EntityRegistry _entityRegistry;
        private readonly Dictionary<EntityComponent, List<Renderer>> hidden = new Dictionary<EntityComponent, List<Renderer>>();
        private readonly List<EntityComponent> scratch = new List<EntityComponent>();
        private float nextRefresh;
        private bool failed;

        public ConstructionVisibility(EntityRegistry entityRegistry) => _entityRegistry = entityRegistry;

        public static bool IsHidden(int slot) => hiddenSlots.Contains(slot);

        public static void SetHidden(int slot, bool hide)
        {
            if (hide ? !hiddenSlots.Add(slot) : !hiddenSlots.Remove(slot)) return;
            changed = true;
            Plugin.Log(hide ? $"Colony {slot + 1}'s construction is hidden on this computer" : $"Colony {slot + 1}'s construction is shown again");
        }

        /// <summary>A new session starts with every colony's construction shown.</summary>
        public static void ShowAll()
        {
            if (hiddenSlots.Count == 0) return;
            hiddenSlots.Clear();
            changed = true;
        }

        public void UpdateSingleton()
        {
            if (failed) return;
            try
            {
                if (hiddenSlots.Count == 0 && hidden.Count == 0) { changed = false; return; }
                float now = Time.unscaledTime;
                if (!changed && now < nextRefresh) return;
                changed = false;
                nextRefresh = now + RefreshSeconds;
                Apply();
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Hiding another player's construction stopped working: " + error.Message);
                failed = true;
                try { Restore(); } catch (Exception) { }
            }
        }

        private void Apply()
        {
            // Give back what no longer applies: the colony is shown again, the building is gone, or it is now a trading post.
            scratch.Clear();
            foreach (var pair in hidden)
            {
                EntityComponent entity = pair.Key;
                int? owner = entity ? DistrictOwner.OwnerOf(entity) : null;
                if (!entity || owner == null || !hiddenSlots.Contains(owner.Value) || TradingPosts.IsTradingPostBuilding(entity) || IsFinished(entity))
                    scratch.Add(entity);
            }
            foreach (EntityComponent entity in scratch) Release(entity);

            if (hiddenSlots.Count == 0) return;
            int me = ColonySession.LocalSlot;
            foreach (EntityComponent entity in _entityRegistry.Entities)
            {
                // Only what is still being built: a finished building stays drawn.
                BlockObject blockObject = entity ? entity.GetComponent<BlockObject>() : null;
                if (blockObject == null || blockObject.IsFinished || TradingPosts.IsTradingPostBuilding(entity)) continue;
                int? owner = DistrictOwner.OwnerOf(entity);
                if (owner == null || owner.Value == me || !hiddenSlots.Contains(owner.Value)) continue;
                // Drawn parts the game switches on again (a finished building's new model) are caught on the next pass.
                if (!hidden.TryGetValue(entity, out List<Renderer> renderers)) hidden[entity] = renderers = new List<Renderer>();
                foreach (Renderer renderer in entity.GameObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled) continue;
                    renderer.enabled = false;
                    renderers.Add(renderer);
                }
            }
        }

        private static bool IsFinished(EntityComponent entity)
        {
            BlockObject blockObject = entity.GetComponent<BlockObject>();
            return blockObject == null || blockObject.IsFinished;
        }

        private void Release(EntityComponent entity)
        {
            if (hidden.TryGetValue(entity, out List<Renderer> renderers))
                foreach (Renderer renderer in renderers) if (renderer) renderer.enabled = true;
            hidden.Remove(entity);
        }

        private void Restore()
        {
            foreach (var pair in hidden)
                foreach (Renderer renderer in pair.Value) if (renderer) renderer.enabled = true;
            hidden.Clear();
        }
    }
}
