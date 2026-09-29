using HarmonyLib;
using System;
using System.Collections.Generic;
using Timberborn.Common;
using Timberborn.GameDistricts;
using Timberborn.GoodsSampling;
using Timberborn.GoodStatisticsBatchControl;
using Timberborn.GoodStatisticsUI;
using Timberborn.PopulationStatisticsBatchControl;
using Timberborn.PopulationStatisticsSampling;
using Timberborn.SingletonSystem;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// The batch control window's history graphs with no district chosen (goods statistics, F9; population statistics,
    /// F10): the game draws the whole map's history there, both colonies added together. In a separate-colonies co-op
    /// game (<see cref="ColonyViewService.Active"/>) they draw this player's colony instead: their own districts' saved
    /// histories added together day by day, on the whole map's days (a district counts from the day it was first
    /// sampled; the days before it, it simply isn't there).
    ///
    /// Display only. The game's registries (whole map and districts) are only read, never changed: the colony's
    /// histories are this computer's own lists, handed to the graphs in place of the whole map's, never saved, and rebuilt
    /// only when the graphs ask for them after the game has taken a new day's sample (or this player's districts change).
    /// Nothing simulated reads them.
    /// </summary>
    public class ColonyHistoryView : RegisteredSingleton, ILoadableSingleton
    {
        private readonly GlobalPopulationSamplesRegistry _globalPopulationSamplesRegistry;
        private readonly GlobalGoodSamplingRegistry _globalGoodSamplingRegistry;
        private readonly ColonyViewService _colonyViewService;

        public ColonyHistoryView(GlobalPopulationSamplesRegistry globalPopulationSamplesRegistry,
            GlobalGoodSamplingRegistry globalGoodSamplingRegistry, ColonyViewService colonyViewService)
        {
            _globalPopulationSamplesRegistry = globalPopulationSamplesRegistry;
            _globalGoodSamplingRegistry = globalGoodSamplingRegistry;
            _colonyViewService = colonyViewService;
        }

        public void Load() { }

        public static ColonyHistoryView Instance => SingletonManager.GetSingleton<ColonyHistoryView>();

        /// <summary>What a history was last built from: rebuilt only when one of these changes.</summary>
        private class Built
        {
            private int samples = -1;
            private bool active;
            private readonly List<DistrictCenter> districts = new List<DistrictCenter>();

            public bool Changed(int samples, bool active, List<DistrictCenter> own)
            {
                bool same = samples == this.samples && active == this.active && (!active || Same(own));
                if (same) return false;
                this.samples = samples;
                this.active = active;
                districts.Clear();
                if (active) districts.AddRange(own);
                return true;
            }

            private bool Same(List<DistrictCenter> own)
            {
                if (own.Count != districts.Count) return false;
                for (int i = 0; i < own.Count; i++)
                    if (own[i] != districts[i]) return false;
                return true;
            }
        }

        // ---- population (F10) ----

        private readonly List<PopulationSample> populationSamples = new List<PopulationSample>();
        private PopulationSampleHistory populationHistory;
        private readonly Built populationBuilt = new Built();
        private readonly List<PopulationSampleHistory> ownPopulation = new List<PopulationSampleHistory>();

        /// <summary>The whole map's history is swapped for the colony's; any other (a district's) is left as it is.</summary>
        public void SwapPopulation(ref PopulationSampleHistory history)
        {
            PopulationSampleHistory global = _globalPopulationSamplesRegistry.PopulationSampleHistory;
            if (global == null || !ReferenceEquals(history, global)) return;
            if (populationHistory == null) populationHistory = PopulationSampleHistory.CreateFromSave(populationSamples);
            RefreshPopulation();
            history = populationHistory;
        }

        /// <summary>Before a graph reads its history: the colony's is brought up to date (other histories are the game's).</summary>
        public void BeforePopulationGraphReads(PopulationSampleHistory history)
        {
            if (populationHistory != null && ReferenceEquals(history, populationHistory)) RefreshPopulation();
        }

        private void RefreshPopulation()
        {
            ReadOnlyList<PopulationSample> days = _globalPopulationSamplesRegistry.PopulationSampleHistory.PopulationSamples;
            bool active = ColonyViewService.Active;
            List<DistrictCenter> own = active ? _colonyViewService.OwnDistricts() : null;
            if (!populationBuilt.Changed(days.Count, active, own)) return;
            populationSamples.Clear();
            if (!active)
            {
                // No longer showing one colony (the session ended with the window open): the whole map's, as the game.
                for (int i = 0; i < days.Count; i++) populationSamples.Add(days[i]);
                return;
            }
            ownPopulation.Clear();
            foreach (DistrictCenter districtCenter in own)
            {
                PopulationSampleHistory history = districtCenter.GetComponent<DistrictPopulationSamplesRegistry>()?.PopulationSampleHistory;
                if (history != null) ownPopulation.Add(history);
            }
            for (int i = 0; i < days.Count; i++)
            {
                // A district is sampled with the whole map on every day it stands, so its latest sample is the map's
                // latest day: its days line up from the end.
                int back = days.Count - 1 - i;
                PopulationSample sum = new PopulationSample(days[i].Day, days[i].Cycle, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                long wellbeing = 0;
                int beavers = 0;
                foreach (PopulationSampleHistory history in ownPopulation)
                {
                    ReadOnlyList<PopulationSample> samples = history.PopulationSamples;
                    if (back >= samples.Count) continue;
                    PopulationSample sample = samples[samples.Count - 1 - back];
                    sum += sample;
                    // Each district's average weighted by its beavers, as the top bar's (ColonyViewService.ColonyWellbeing).
                    int count = sample.Adults + sample.Children;
                    wellbeing += (long)sample.Wellbeing * count;
                    beavers += count;
                }
                sum.SetWellbeing(beavers == 0 ? 0 : (int)Math.Round((double)wellbeing / beavers));
                populationSamples.Add(sum);
            }
        }

        // ---- goods (F9) ----

        private GoodSamplingRegistry goodsRegistry;
        private readonly Dictionary<string, List<GoodSample>> goodsSamples = new Dictionary<string, List<GoodSample>>();
        private readonly HashSet<GoodSampleHistory> goodsHistories = new HashSet<GoodSampleHistory>();
        private readonly Built goodsBuilt = new Built();
        private readonly List<GoodSamplingRegistry> ownGoods = new List<GoodSamplingRegistry>();

        /// <summary>The whole map's goods registry is swapped for the colony's; any other (a district's) is left as it is.</summary>
        public void SwapGoods(ref GoodSamplingRegistry registry)
        {
            GoodSamplingRegistry global = _globalGoodSamplingRegistry.GoodSamplingRegistry;
            if (global == null || !ReferenceEquals(registry, global)) return;
            if (goodsRegistry == null)
            {
                // One history per good the whole map has, each on a list of this view's own.
                var histories = new List<GoodSampleHistory>();
                foreach (GoodSampleHistory all in global.GoodSampleHistories)
                {
                    var samples = new List<GoodSample>();
                    goodsSamples[all.GoodId] = samples;
                    GoodSampleHistory history = GoodSampleHistory.CreateFromSave(all.GoodId, samples);
                    goodsHistories.Add(history);
                    histories.Add(history);
                }
                goodsRegistry = GoodSamplingRegistry.CreateFromSave(histories, new List<string>());
            }
            RefreshGoods();
            registry = goodsRegistry;
        }

        /// <summary>Before a chart reads its history: the colony's are brought up to date (other histories are the game's).</summary>
        public void BeforeGoodChartReads(GoodSampleHistory history)
        {
            if (goodsRegistry != null && history != null && goodsHistories.Contains(history)) RefreshGoods();
        }

        private void RefreshGoods()
        {
            ReadOnlyList<GoodSampleHistory> all = _globalGoodSamplingRegistry.GoodSamplingRegistry.GoodSampleHistories;
            // Every good gains a sample each day: the total changes with each new day.
            int samples = 0;
            foreach (GoodSampleHistory history in all) samples += history.GoodSamples.Count;
            bool active = ColonyViewService.Active;
            List<DistrictCenter> own = active ? _colonyViewService.OwnDistricts() : null;
            if (!goodsBuilt.Changed(samples, active, own)) return;
            ownGoods.Clear();
            if (active)
            {
                foreach (DistrictCenter districtCenter in own)
                {
                    GoodSamplingRegistry registry = districtCenter.GetComponent<DistrictGoodSamplingRegistry>()?.GoodSamplingRegistry;
                    if (registry != null) ownGoods.Add(registry);
                }
            }
            foreach (GoodSampleHistory global in all)
            {
                if (!goodsSamples.TryGetValue(global.GoodId, out List<GoodSample> summed)) continue;
                summed.Clear();
                ReadOnlyList<GoodSample> days = global.GoodSamples;
                if (!active)
                {
                    for (int i = 0; i < days.Count; i++) summed.Add(days[i]);
                    continue;
                }
                for (int i = 0; i < days.Count; i++)
                {
                    // Lined up from the latest day, as the population's (and the top bar's goods chart).
                    int back = days.Count - 1 - i;
                    GoodSample sum = new GoodSample(days[i].Cycle, days[i].Day, 0, 0, 0, 0);
                    foreach (GoodSamplingRegistry registry in ownGoods)
                    {
                        if (!registry._goodSampleHistoryMap.TryGetValue(global.GoodId, out GoodSampleHistory history)) continue;
                        ReadOnlyList<GoodSample> district = history.GoodSamples;
                        if (back < district.Count) sum += district[district.Count - 1 - back];
                    }
                    summed.Add(sum);
                }
            }
        }
    }

    // The population statistics tab (F10) builds its graphs for the whole map from the game's saved global history.
    // Hand them the colony's instead; a district's graphs are untouched.
    [HarmonyPatch(typeof(PopulationStatisticsGraphFactory), nameof(PopulationStatisticsGraphFactory.Create), new[] { typeof(PopulationSampleHistory) })]
    static class ColonyHistoryPopulationGraphsPatcher
    {
        static void Prefix(ref PopulationSampleHistory populationSampleHistory)
        {
            if (!ColonyViewService.Active) return;
            ColonyHistoryView.Instance?.SwapPopulation(ref populationSampleHistory);
        }
    }

    // A graph redraws after each day's sample: the colony's history catches up first.
    [HarmonyPatch(typeof(PopulationStatisticsGraph), nameof(PopulationStatisticsGraph.UpdateItem))]
    static class ColonyHistoryPopulationRefreshPatcher
    {
        static void Prefix(PopulationStatisticsGraph __instance)
        {
            ColonyHistoryView.Instance?.BeforePopulationGraphReads(__instance._populationSampleHistory);
        }
    }

    // The goods statistics tab (F9) builds its charts for the whole map from the game's saved global registry. Hand them
    // the colony's instead; a district's charts are untouched.
    [HarmonyPatch(typeof(GoodStatisticsGroupFactory), nameof(GoodStatisticsGroupFactory.Create))]
    static class ColonyHistoryGoodsGroupsPatcher
    {
        static void Prefix(ref GoodSamplingRegistry goodSamplingRegistry)
        {
            if (!ColonyViewService.Active) return;
            ColonyHistoryView.Instance?.SwapGoods(ref goodSamplingRegistry);
        }
    }

    // A chart redraws after each day's sample: the colony's histories catch up first (only a chart of theirs asks).
    [HarmonyPatch(typeof(GoodSampleHistoryElement), nameof(GoodSampleHistoryElement.Update))]
    static class ColonyHistoryGoodsRefreshPatcher
    {
        static void Prefix(GoodSampleHistoryElement __instance)
        {
            ColonyHistoryView.Instance?.BeforeGoodChartReads(__instance._goodSampleHistory);
        }
    }
}
