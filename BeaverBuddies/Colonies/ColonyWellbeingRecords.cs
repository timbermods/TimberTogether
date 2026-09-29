using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using Timberborn.GameDistricts;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using Timberborn.Wellbeing;
using Timberborn.WorldPersistence;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// Each colony's own wellbeing high score (the rule: WellbeingRecords). The game keeps one high score for the whole
    /// map and announces a new one (NewWellbeingHighscoreEvent) on every computer, so in co-op the other colony's record
    /// reached this player too. In a separate-colonies co-op game the game's announcement is dropped
    /// (ColonyWellbeingHighscorePatcher); instead every computer raises every colony's record as each day starts, from
    /// the same simulated figures, and posts the game's own event only for its own colony's new record (never its first,
    /// from 0 on its first day with beavers, as the game skips its own first), with that
    /// colony's wellbeing as the top bar shows it (ColonyViewService.ColonyWellbeing). The records are saved so every
    /// computer keeps the same ones. Display only: nothing simulated reads them.
    /// </summary>
    public class ColonyWellbeingRecords : RegisteredSingleton, ILoadableSingleton, ISaveableSingleton
    {
        private static readonly SingletonKey RecordsKey = new SingletonKey("BeaverBuddies.ColonyWellbeingRecords");
        private static readonly PropertyKey<string> ValuesKey = new PropertyKey<string>("Records");

        private readonly ISingletonLoader _singletonLoader;
        private readonly EventBus _eventBus;
        private readonly DistrictCenterRegistry _districtCenterRegistry;
        private readonly WellbeingService _wellbeingService;

        private readonly int[] records = new int[ColonySlotTable.MaxSlots];

        /// <summary>The game's high-score event, found by name as the game loads; null if the game no longer has it.</summary>
        internal static Type HighscoreEvent { get; private set; }
        private static ConstructorInfo highscoreEventConstructor;
        /// <summary>True while this computer posts its own colony's high score, which the patch lets through.</summary>
        internal static bool Announcing { get; private set; }
        private static bool warned;

        public static ColonyWellbeingRecords Instance => SingletonManager.GetSingleton<ColonyWellbeingRecords>();

        public ColonyWellbeingRecords(ISingletonLoader singletonLoader, EventBus eventBus,
            DistrictCenterRegistry districtCenterRegistry, WellbeingService wellbeingService)
        {
            _singletonLoader = singletonLoader;
            _eventBus = eventBus;
            _districtCenterRegistry = districtCenterRegistry;
            _wellbeingService = wellbeingService;
        }

        public void Load()
        {
            if (HighscoreEvent == null)
            {
                HighscoreEvent = AccessTools.GetTypesFromAssembly(typeof(WellbeingService).Assembly).FirstOrDefault(t => t.Name == "NewWellbeingHighscoreEvent")
                    ?? AccessTools.TypeByName("NewWellbeingHighscoreEvent");
                highscoreEventConstructor = HighscoreEvent?.GetConstructor(new[] { typeof(int) });
            }
            if (_singletonLoader.TryGetSingleton(RecordsKey, out IObjectLoader loader) && loader.Has(ValuesKey))
                WellbeingRecords.Decode(loader.Get(ValuesKey), records);
            _eventBus.Register(this);
        }

        public void Save(ISingletonSaver singletonSaver)
        {
            // Separate colonies only: a shared game's save holds only what the Stability Fork's does.
            if (!ColonyModeService.IsSeparateColonies) return;
            string saved = WellbeingRecords.Encode(records);
            if (saved.Length > 0) singletonSaver.GetSingleton(RecordsKey).Set(ValuesKey, saved);
        }

        [OnEvent]
        public void OnDaytimeStart(DaytimeStartEvent daytimeStartEvent)
        {
            if (!ColonyModeService.IsSeparateColonies) return;
            // This runs in the tick, on every computer: whatever happens here, the game carries on.
            try
            {
                int?[] wellbeing = ColoniesWellbeing();
                int[] before = (int[])records.Clone();
                foreach (int slot in WellbeingRecords.Raise(records, wellbeing))
                {
                    // Every computer records it; only this player's own is announced, and never a colony's first.
                    if (slot == ColonySession.LocalSlot && ColonyViewService.Active && WellbeingRecords.Announces(before[slot])) Announce(records[slot]);
                }
            }
            catch (Exception error)
            {
                Plugin.LogWarning("[Colony] Could not update the colonies' wellbeing high scores: " + error.Message);
            }
        }

        /// <summary>Each colony's average wellbeing: each district's average, weighted by its beavers. Null with no beavers.</summary>
        private int?[] ColoniesWellbeing()
        {
            long[] sums = new long[ColonySlotTable.MaxSlots];
            int[] beavers = new int[ColonySlotTable.MaxSlots];
            foreach (DistrictCenter districtCenter in _districtCenterRegistry.FinishedDistrictCenters)
            {
                int? owner = DistrictOwner.OwnerOfDistrict(districtCenter);
                if (owner == null || owner.Value < 0 || owner.Value >= ColonySlotTable.MaxSlots) continue;
                int count = districtCenter.DistrictPopulation.NumberOfAdults + districtCenter.DistrictPopulation.NumberOfChildren;
                if (count == 0) continue;
                sums[owner.Value] += (long)_wellbeingService.GetAverageDistrictWellbeing(districtCenter) * count;
                beavers[owner.Value] += count;
            }
            var wellbeing = new int?[ColonySlotTable.MaxSlots];
            for (int slot = 0; slot < wellbeing.Length; slot++)
                if (beavers[slot] > 0) wellbeing[slot] = (int)Math.Round((double)sums[slot] / beavers[slot]);
            return wellbeing;
        }

        /// <summary>Posts the game's high-score event for this player's colony, on this computer only.</summary>
        private void Announce(int record)
        {
            // Without the patch that drops the game's own announcement, this would be a second one.
            if (!ColonyWellbeingHighscorePatcher.Applied || highscoreEventConstructor == null)
            {
                if (!warned) Plugin.LogWarning("[Colony] This game version's wellbeing high score can't be shown per colony");
                warned = true;
                return;
            }
            Announcing = true;
            try
            {
                _eventBus.Post(highscoreEventConstructor.Invoke(new object[] { record }));
            }
            finally
            {
                Announcing = false;
            }
        }
    }

    // The game's high score is the whole map's, announced on every computer: in a separate-colonies co-op game it would
    // tell each player the other colony's news. There it is dropped, and ColonyWellbeingRecords announces each player's
    // own. The event only shows the message and counts toward this player's own unlocks (RuntimeChecks list what hears
    // it), so a computer that skips it changes nothing simulated. Alone, one person plays every colony: the game's own.
    [HarmonyPatch]
    static class ColonyWellbeingHighscorePatcher
    {
        /// <summary>True once the game's EventBus.Post was found to patch.</summary>
        internal static bool Applied { get; private set; }

        static MethodBase Target() => AccessTools.Method(typeof(EventBus), nameof(EventBus.Post), new[] { typeof(object) });

        static bool Prepare()
        {
            Applied = Target() != null;
            if (!Applied) Plugin.LogWarning("[Colony] EventBus.Post is not what this version expects: the wellbeing high score stays the map's");
            return Applied;
        }

        static MethodBase TargetMethod() => Target();

        static bool Prefix(object __0)
        {
            // Every event of the game comes through here: only the high score is judged.
            Type highscore = ColonyWellbeingRecords.HighscoreEvent;
            if (highscore == null || __0 == null || __0.GetType() != highscore || ColonyWellbeingRecords.Announcing) return true;
            return !ColonyViewService.Active;
        }
    }
}
