using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BlockObjectModelSystem;
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
    public class ConstructionVisibility : RegisteredSingleton, ILoadableSingleton, IUpdatableSingleton
    {
        private const float RefreshSeconds = .5f;

        private static readonly HashSet<int> hiddenSlots = new HashSet<int>();
        private static bool changed;

        private readonly EntityRegistry _entityRegistry;
        private readonly EventBus _eventBus;
        private readonly Dictionary<EntityComponent, List<Renderer>> hidden = new Dictionary<EntityComponent, List<Renderer>>();
        private readonly List<EntityComponent> scratch = new List<EntityComponent>();
        // What is being built now, from the game's own state events: a pass walks these, not every entity on the map.
        // Filled from the whole map once, the first time anything is hidden (sites loaded before this listened).
        private readonly HashSet<BlockObject> unfinished = new HashSet<BlockObject>();
        private readonly List<BlockObject> sites = new List<BlockObject>();
        private bool seeded;
        private float nextRefresh;
        private bool failed;

        public ConstructionVisibility(EntityRegistry entityRegistry, EventBus eventBus)
        {
            _entityRegistry = entityRegistry;
            _eventBus = eventBus;
        }

        public void Load() => _eventBus.Register(this);

        // The game posts these in its tick, on every computer; here they only fill a list this computer draws from.
        [OnEvent]
        public void OnEnteredUnfinishedState(EnteredUnfinishedStateEvent enteredUnfinishedStateEvent)
        {
            BlockObject blockObject = enteredUnfinishedStateEvent?.BlockObject;
            if (blockObject != null) unfinished.Add(blockObject);
        }

        [OnEvent]
        public void OnExitedUnfinishedState(ExitedUnfinishedStateEvent exitedUnfinishedStateEvent)
        {
            BlockObject blockObject = exitedUnfinishedStateEvent?.BlockObject;
            if (blockObject != null) unfinished.Remove(blockObject);
        }

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
                if (!entity || owner == null || !hiddenSlots.Contains(owner.Value) || TradingPosts.IsTradingPostBuilding(entity)
                    || PowerExports.IsFacilityBuilding(entity) || IsFinished(entity))
                    scratch.Add(entity);
            }
            foreach (EntityComponent entity in scratch) Release(entity);

            if (hiddenSlots.Count == 0) return;
            if (!seeded)
            {
                seeded = true;
                foreach (EntityComponent entity in _entityRegistry.Entities)
                {
                    BlockObject blockObject = entity ? entity.GetComponent<BlockObject>() : null;
                    if (blockObject != null && blockObject.IsUnfinished) unfinished.Add(blockObject);
                }
            }
            int me = ColonySession.LocalSlot;
            sites.Clear();
            sites.AddRange(unfinished);
            foreach (BlockObject blockObject in sites)
            {
                // Only what is still being built: a finished building stays drawn.
                EntityComponent entity = blockObject ? blockObject.GetComponent<EntityComponent>() : null;
                if (!entity) { unfinished.Remove(blockObject); continue; }
                if (blockObject.IsFinished || TradingPosts.IsTradingPostBuilding(entity) || PowerExports.IsFacilityBuilding(entity)) continue;
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

        // The game switches a building's models on and off itself (the site's scaffold off and the finished model on as
        // it finishes): what was switched off here is switched on again, then the game decides again which of its models
        // are drawn, from its own state. That redraw only reads the building's state and draws it, the same call the game
        // makes when the view changes (map layers, the underground view).
        private void Release(EntityComponent entity)
        {
            if (hidden.TryGetValue(entity, out List<Renderer> renderers))
                foreach (Renderer renderer in renderers) if (renderer) renderer.enabled = true;
            hidden.Remove(entity);
            Redraw(entity);
        }

        private void Restore()
        {
            foreach (var pair in hidden)
            {
                foreach (Renderer renderer in pair.Value) if (renderer) renderer.enabled = true;
                try { Redraw(pair.Key); } catch (Exception) { }
            }
            hidden.Clear();
        }

        private static void Redraw(EntityComponent entity)
        {
            if (!entity) return;
            IBlockObjectModel model = entity.GetComponent<IBlockObjectModel>();
            if (model != null) model.UpdateModelVisibility();
        }
    }
}
