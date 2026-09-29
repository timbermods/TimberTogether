using BeaverBuddies.IO;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.BatchControl;
using Timberborn.Common;
using Timberborn.CoreUI;
using Timberborn.EntitySystem;
using Timberborn.FactionSystem;
using Timberborn.GameDistricts;
using Timberborn.GameDistrictsMigrationBatchControl;
using Timberborn.GoodsSampling;
using Timberborn.NeedSystem;
using Timberborn.NotificationSystem;
using Timberborn.NotificationSystemUI;
using Timberborn.Population;
using Timberborn.PopulationUI;
using Timberborn.ResourceCountingSystem;
using Timberborn.ResourceCountingSystemUI;
using Timberborn.SingletonSystem;
using Timberborn.StatusSystem;
using Timberborn.StockpilesUI;
using Timberborn.Wellbeing;
using Timberborn.WellbeingUI;
using UnityEngine.UIElements;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// What each player sees: their own colony, never both added together. Timberborn's interface shows the whole
    /// settlement whenever no district is selected (the top bar's goods and their stock history, population, wellbeing,
    /// the wellbeing window, the batch control window's lists and district choices, the alert panel, the notification
    /// journal); in a separate-colonies session "the whole
    /// settlement" becomes "your colony". Display only: everything here reads the game and changes nothing that is
    /// simulated, so each computer may show something different without any risk to the shared game.
    /// </summary>
    public class ColonyViewService : RegisteredSingleton, ILoadableSingleton
    {
        private readonly DistrictCenterRegistry _districtCenterRegistry;
        private readonly ResourceCountingService _resourceCountingService;
        private readonly PopulationService _populationService;
        private readonly WellbeingService _wellbeingService;

        private readonly PopulationData colonyPopulation = new PopulationData();
        private readonly PopulationData districtPopulation = new PopulationData();

        public ColonyViewService(DistrictCenterRegistry districtCenterRegistry,
            ResourceCountingService resourceCountingService, PopulationService populationService,
            WellbeingService wellbeingService)
        {
            _districtCenterRegistry = districtCenterRegistry;
            _resourceCountingService = resourceCountingService;
            _populationService = populationService;
            _wellbeingService = wellbeingService;
        }

        public void Load() { }

        public static ColonyViewService Instance => SingletonManager.GetSingleton<ColonyViewService>();

        /// <summary>
        /// True when this computer shows only its own colony: a separate-colonies game in a co-op session, once this
        /// player is seated. Alone, one person plays every colony and sees them all, as in the game.
        /// </summary>
        public static bool Active =>
            ColonyModeService.IsSeparateColonies && !EventIO.IsNull && ColonySession.LocalSlot >= 0 && Instance != null;

        public static bool IsOwnDistrict(DistrictCenter districtCenter)
        {
            int? owner = DistrictOwner.OwnerOfDistrict(districtCenter);
            return owner == null || owner.Value == ColonySession.LocalSlot;
        }

        /// <summary>
        /// Whether a thing belongs to this player's colony: through its district (a building's, a beaver's), else by the
        /// colony it was last in (ColonyJournal: a dead beaver, which the game has taken out of its district, or one cut
        /// off from it). Something in no district, with no colony recorded, is shown to everyone.
        /// </summary>
        public static bool IsOwn(BaseComponent component)
        {
            if (!component) return true;
            int? owner = DistrictOwner.OwnerOf(component);
            return JournalFilter.IsOwn(ColonySession.LocalSlot, owner, owner == null ? ColonyJournal.Instance?.RecordedOwnerOf(component) : null);
        }

        /// <summary><see cref="IsOwn(BaseComponent)"/>, with this player's slot already read (ActiveThisFrame).</summary>
        public static bool IsOwnFor(BaseComponent component, int localSlot)
        {
            if (!component) return true;
            int? owner = DistrictOwner.OwnerOf(component);
            return JournalFilter.IsOwn(localSlot, owner, owner == null ? ColonyJournal.Instance?.RecordedOwnerOf(component) : null);
        }

        // The alert list asks, for every alert on every frame, whether this computer filters and whose the alert is.
        // Whether it filters and this player's seat (several lookups each) are read once a frame instead of for every
        // alert (1.4.0-rc1 review, D-S9). Display only: nothing simulated reads them.
        private static int filterFrame = -1;
        private static bool filtersThisFrame;
        private static int slotThisFrame;
        internal static readonly ColonyProfiler.Spot Alerts = ColonyProfiler.DeclareSampled("Alerts shown only for this colony (per alert per frame)");

        /// <summary><see cref="Active"/>, and this player's slot, as of the start of this frame.</summary>
        public static bool ActiveThisFrame(out int localSlot)
        {
            localSlot = -1;
            // Single player and shared games stop at two static reads, as before.
            if (!ColonyModeService.IsSeparateColonies || EventIO.IsNull) return false;
            int frame = UnityEngine.Time.frameCount;
            if (frame != filterFrame)
            {
                filterFrame = frame;
                bool filters = Active;
                int slot = ColonySession.LocalSlot;
                // Written when it changes, so a log shows whose alerts this computer listed and from when.
                if (filters != filtersThisFrame || slot != slotThisFrame)
                    Plugin.Log(filters ? $"[Colony] Alerts and the journal show colony {slot + 1} only" : "[Colony] Alerts and the journal show every colony (slot " + slot + ")");
                filtersThisFrame = filters;
                slotThisFrame = slot;
            }
            localSlot = slotThisFrame;
            return filtersThisFrame;
        }

        // The top bar asks once per good it shows, and the population panel and wellbeing again: one list, filled at
        // most once per frame (a district's owner and this player's seat change rarely, and never within a frame).
        private readonly List<DistrictCenter> ownDistricts = new List<DistrictCenter>();
        private int ownDistrictsFrame = -1, ownDistrictsSlot = int.MinValue;

        /// <summary>This player's finished districts. The same list each time within a frame: read it, never keep it.</summary>
        public List<DistrictCenter> OwnDistricts()
        {
            int frame = UnityEngine.Time.frameCount;
            int slot = ColonySession.LocalSlot;
            if (frame != ownDistrictsFrame || slot != ownDistrictsSlot)
            {
                ownDistrictsFrame = frame;
                ownDistrictsSlot = slot;
                ownDistricts.Clear();
                foreach (DistrictCenter districtCenter in _districtCenterRegistry.FinishedDistrictCenters)
                {
                    int? owner = DistrictOwner.OwnerOfDistrict(districtCenter);
                    if (owner == null || owner.Value == slot) ownDistricts.Add(districtCenter);
                }
            }
            return ownDistricts;
        }

        /// <summary>
        /// This player's biggest district: where the batch control window opens, and where the Home key goes. Outside a
        /// separate-colonies co-op game (a shared game, or alone) every district is everyone's: the biggest of all.
        /// </summary>
        public DistrictCenter MainDistrict() =>
            (Active ? OwnDistricts() : (IEnumerable<DistrictCenter>)_districtCenterRegistry.FinishedDistrictCenters)
                .OrderByDescending(dc => (dc.DistrictPopulation.NumberOfAdults + dc.DistrictPopulation.NumberOfChildren)).FirstOrDefault();

        public ResourceCount ColonyResourceCount(string goodId)
        {
            ResourceCount total = ResourceCount.Empty;
            foreach (DistrictCenter districtCenter in OwnDistricts())
                total += _resourceCountingService.GetDistrictResourceCounter(districtCenter).GetResourceCount(goodId);
            return total;
        }

        /// <summary>The population panel's figures for this player's districts, summed the way the game sums them.</summary>
        public PopulationData ColonyPopulationData()
        {
            int adults = 0, children = 0, bots = 0;
            int beaverEmployable = 0, beaverUnemployable = 0, botEmployable = 0, botUnemployable = 0;
            int occupiedBeds = 0, freeBeds = 0, homeless = 0;
            int beaverOccupied = 0, beaverFree = 0, beaverUnemployed = 0;
            int botOccupied = 0, botFree = 0, botUnemployed = 0;
            int contaminatedAdults = 0, contaminatedChildren = 0;
            foreach (DistrictCenter districtCenter in OwnDistricts())
            {
                _populationService._populationDataCollector.CollectData(districtCenter, districtPopulation);
                PopulationData d = districtPopulation;
                adults += d.NumberOfAdults;
                children += d.NumberOfChildren;
                bots += d.NumberOfBots;
                beaverEmployable += d.BeaverWorkforceData.Employable;
                beaverUnemployable += d.BeaverWorkforceData.Unemployable;
                botEmployable += d.BotWorkforceData.Employable;
                botUnemployable += d.BotWorkforceData.Unemployable;
                occupiedBeds += d.BedData.OccupiedBeds;
                freeBeds += d.BedData.FreeBeds;
                homeless += d.BedData.Homeless;
                beaverOccupied += d.BeaverWorkplaceData.OccupiedWorkslots;
                beaverFree += d.BeaverWorkplaceData.FreeWorkslots;
                beaverUnemployed += d.BeaverWorkplaceData.Unemployed;
                botOccupied += d.BotWorkplaceData.OccupiedWorkslots;
                botFree += d.BotWorkplaceData.FreeWorkslots;
                botUnemployed += d.BotWorkplaceData.Unemployed;
                contaminatedAdults += d.ContaminationData.ContaminatedAdults;
                contaminatedChildren += d.ContaminationData.ContaminatedChildren;
            }
            colonyPopulation.Update(adults, children, bots,
                new WorkforceData(beaverEmployable, beaverUnemployable),
                new WorkforceData(botEmployable, botUnemployable),
                new BedData(occupiedBeds, freeBeds, homeless),
                new WorkplaceData(beaverOccupied, beaverFree, beaverUnemployed),
                new WorkplaceData(botOccupied, botFree, botUnemployed),
                new ContaminationData(contaminatedAdults, contaminatedChildren));
            return colonyPopulation;
        }

        /// <summary>
        /// This player's goods history, for the top bar's stock chart: their one district's, or their districts' added
        /// together day by day (a younger district's days line up with the latest ones). A new registry is built for
        /// the tooltip; the game's own are only read.
        /// </summary>
        public GoodSamplingRegistry ColonyGoodSamplingRegistry(GoodSamplingRegistry global)
        {
            var registries = new List<GoodSamplingRegistry>();
            foreach (DistrictCenter districtCenter in OwnDistricts())
            {
                GoodSamplingRegistry registry = districtCenter.GetComponent<DistrictGoodSamplingRegistry>()?.GoodSamplingRegistry;
                if (registry != null) registries.Add(registry);
            }
            if (registries.Count == 1) return registries[0];
            var histories = new List<GoodSampleHistory>();
            foreach (GoodSampleHistory all in global.GoodSampleHistories)
            {
                // Latest day first while adding, then turned round.
                var summed = new List<GoodSample>();
                foreach (GoodSamplingRegistry registry in registries)
                {
                    if (!registry._goodSampleHistoryMap.TryGetValue(all.GoodId, out GoodSampleHistory history)) continue;
                    ReadOnlyList<GoodSample> samples = history.GoodSamples;
                    for (int back = 0; back < samples.Count; back++)
                    {
                        GoodSample sample = samples[samples.Count - 1 - back];
                        if (back < summed.Count) summed[back] = summed[back] + sample;
                        else summed.Add(sample);
                    }
                }
                summed.Reverse();
                histories.Add(GoodSampleHistory.CreateFromSave(all.GoodId, summed));
            }
            return GoodSamplingRegistry.CreateFromSave(histories, new List<string>());
        }

        /// <summary>The colony's average wellbeing: each district's average, weighted by its beavers. Null with no beavers.</summary>
        public int? ColonyWellbeing()
        {
            long sum = 0;
            int beavers = 0;
            foreach (DistrictCenter districtCenter in OwnDistricts())
            {
                int count = districtCenter.DistrictPopulation.NumberOfAdults + districtCenter.DistrictPopulation.NumberOfChildren;
                if (count == 0) continue;
                sum += (long)_wellbeingService.GetAverageDistrictWellbeing(districtCenter) * count;
                beavers += count;
            }
            if (beavers == 0) return null;
            return (int)Math.Round((double)sum / beavers);
        }
    }

    // ---- which district the interface is showing ----

    // Selecting one of the other colony's buildings would switch the top bar, population and wellbeing to their
    // district. Keep showing your own colony instead (their panels still open).
    [HarmonyPatch(typeof(DistrictContextService), nameof(DistrictContextService.SelectDistrict))]
    static class ColonyViewSelectDistrictPatcher
    {
        static bool Prefix(DistrictContextService __instance, DistrictCenter districtCenter)
        {
            if (!ColonyViewService.Active || !districtCenter || ColonyViewService.IsOwnDistrict(districtCenter)) return true;
            __instance.UnselectDistrict();
            return false;
        }
    }

    // ---- top bar ----

    [HarmonyPatch(typeof(ContextualResourceCountingService), nameof(ContextualResourceCountingService.GetContextualResourceCount))]
    static class ColonyViewResourceCountPatcher
    {
        static bool Prefix(ContextualResourceCountingService __instance, string goodId, ref ResourceCount __result)
        {
            if (!ColonyViewService.Active || __instance._districtContextService.SelectedDistrict) return true;
            __result = ColonyViewService.Instance.ColonyResourceCount(goodId);
            return false;
        }
    }

    [HarmonyPatch(typeof(PopulationPanel), nameof(PopulationPanel.GetContextualPopulationData))]
    static class ColonyViewPopulationPatcher
    {
        static bool Prefix(PopulationPanel __instance, ref PopulationData __result)
        {
            if (!ColonyViewService.Active || __instance._districtContextService.SelectedDistrict) return true;
            __result = ColonyViewService.Instance.ColonyPopulationData();
            return false;
        }
    }

    [HarmonyPatch(typeof(BasicStatisticsPanel), nameof(BasicStatisticsPanel.UpdateWellbeing))]
    static class ColonyViewWellbeingPatcher
    {
        static void Postfix(BasicStatisticsPanel __instance)
        {
            if (!ColonyViewService.Active || __instance._districtContextService.SelectedDistrict) return;
            ColonyViewService colonyView = ColonyViewService.Instance;
            int? wellbeing = colonyView.ColonyWellbeing();
            VisualElement button = __instance._wellbeingButton;
            button.RemoveFromClassList(BasicStatisticsPanel.BeaversPerishedClass);
            button.RemoveFromClassList(BasicStatisticsPanel.AllPerishedClass);
            if (wellbeing != null)
            {
                __instance._wellbeingCount.text = wellbeing.Value.ToString();
                button.EnableInClassList(BasicStatisticsPanel.NegativeWellbeingClass, wellbeing.Value < 0);
                return;
            }
            // No beavers of their own: blank, as the game shows a settlement whose beavers perished (only bots left, or
            // no one). Before this player has a colony at all, just blank.
            __instance._wellbeingCount.text = "";
            if (colonyView.OwnDistricts().Count == 0)
            {
                button.RemoveFromClassList(BasicStatisticsPanel.NegativeWellbeingClass);
                return;
            }
            bool bots = colonyView.ColonyPopulationData().NumberOfBots > 0;
            button.AddToClassList(bots ? BasicStatisticsPanel.BeaversPerishedClass : BasicStatisticsPanel.AllPerishedClass);
            button.EnableInClassList(BasicStatisticsPanel.NegativeWellbeingClass, !bots);
        }
    }

    // The goods tooltip's chart of the last days' stock (hover a good on the top bar). With no district selected the
    // game charts the whole map's; chart this player's colony's instead. Display only: a registry made for the chart.
    [HarmonyPatch(typeof(GoodStockpilesTooltipFactory), nameof(GoodStockpilesTooltipFactory.GetGoodSamplingRegistry))]
    static class ColonyViewGoodHistoryPatcher
    {
        static void Postfix(GoodStockpilesTooltipFactory __instance, ref GoodSamplingRegistry __result)
        {
            if (!ColonyViewService.Active || __instance._districtContextService.SelectedDistrict || __result == null) return;
            __result = ColonyViewService.Instance.ColonyGoodSamplingRegistry(__result);
        }
    }

    // ---- the wellbeing window (opened from the top bar's wellbeing) ----

    // With no district selected, the window counts each need over every beaver on the map: the other colony's Agora
    // showed progress in this player's window. Count this player's districts' beavers instead, each district as the
    // game counts a selected one. Only the window asks for this count (RuntimeChecks: nothing simulated calls it).
    [HarmonyPatch(typeof(WellbeingService), nameof(WellbeingService.GlobalAppliedNeeds))]
    static class ColonyViewAppliedNeedsPatcher
    {
        static bool Prefix(Dictionary<string, int> appliedNeeds)
        {
            if (!ColonyViewService.Active) return true;
            foreach (DistrictCenter districtCenter in ColonyViewService.Instance.OwnDistricts())
                WellbeingService.AppliedNeeds(districtCenter.DistrictPopulation.GetEnabledCharacters<NeedManager>(), appliedNeeds);
            return false;
        }
    }

    // The beavers each need's count is out of. PopulationService.GlobalPopulationData is also read by the simulation
    // (automation sensors), so the window's own getter is patched, as the population panel's is.
    [HarmonyPatch(typeof(PopulationWellbeingBox), nameof(PopulationWellbeingBox.ContextualPopulationData), MethodType.Getter)]
    static class ColonyViewWellbeingBoxPopulationPatcher
    {
        static bool Prefix(PopulationWellbeingBox __instance, ref PopulationData __result)
        {
            if (!ColonyViewService.Active || __instance._districtContextService.SelectedDistrict) return true;
            __result = ColonyViewService.Instance.ColonyPopulationData();
            return false;
        }
    }

    // The window's average. WellbeingService.AverageGlobalWellbeing is also read by the simulation (faction goals,
    // population graphs), so the window's own update is adjusted, as the top bar's is. With no beavers of their own a
    // player sees 0, as the game shows a district with none.
    [HarmonyPatch(typeof(PopulationWellbeingBox), nameof(PopulationWellbeingBox.UpdateAverageWellbeing))]
    static class ColonyViewWellbeingBoxAveragePatcher
    {
        static void Postfix(PopulationWellbeingBox __instance)
        {
            if (!ColonyViewService.Active || __instance._districtContextService.SelectedDistrict) return;
            int wellbeing = ColonyViewService.Instance.ColonyWellbeing() ?? 0;
            __instance._averageWellbeing.text = wellbeing.ToString();
            __instance._averageWellbeing.EnableInClassList(PopulationWellbeingBox.NegativeWellbeingClass, wellbeing < 0);
        }
    }

    // The window's faction goals ("Progress: 12 / 15") show the whole map's average. Show this player's colony's
    // instead, like every other figure they see. Display only: the game still unlocks by the whole map
    // (FactionGoalsUnlocker reads AverageGlobalWellbeing, which is left alone).
    [HarmonyPatch(typeof(GoalRowFactory), nameof(GoalRowFactory.UpdateProgress),
        new[] { typeof(FactionSpec), typeof(UnlockableFactionSpec), typeof(VisualElement) })]
    static class ColonyViewGoalProgressPatcher
    {
        static void Postfix(GoalRowFactory __instance, FactionSpec factionSpec, UnlockableFactionSpec unlockableFactionSpec, VisualElement goalRowElement)
        {
            if (!ColonyViewService.Active) return;
            // Only the row the game shows a number on: a locked faction this one leads to.
            if (!__instance._factionUnlockingService.IsLocked(factionSpec)
                || __instance._factionService.Current.Id != unlockableFactionSpec.PrerequisiteFaction) return;
            int wellbeing = ColonyViewService.Instance.ColonyWellbeing() ?? 0;
            __instance.UpdateProgress($"{wellbeing} / {unlockableFactionSpec.AverageWellbeingToUnlock}", goalRowElement);
        }
    }

    // ---- batch control window (F1 to F10) ----

    [ManualMethodOverwrite]
    /*
     * 9/21/2026 (Timberborn 1.1.2.4)
		VisibleChildrenCount = 0;
		foreach (BatchControlRow row in _rows)
		{
			EntityComponent entity = row.Entity;
			bool flag = (!selectedDistrict || !entity || BelongsToDistrict(entity, selectedDistrict)) && row.VisibilityGetter();
			row.Root.ToggleDisplayStyle(flag);
			if (flag)
			{
				VisibleChildrenCount++;
			}
		}
		bool flag2 = VisibleChildrenCount > 0;
		_headerRow.Root.ToggleDisplayStyle(flag2);
		return flag2;
     */
    // With no district chosen (and in the tabs that ignore the choice: mechanical, migration) the window lists every
    // district's beavers and buildings. List only this player's colony instead.
    [HarmonyPatch(typeof(BatchControlRowGroup), nameof(BatchControlRowGroup.UpdateVisibleRows))]
    static class ColonyViewBatchControlRowsPatcher
    {
        static bool Prefix(BatchControlRowGroup __instance, DistrictCenter selectedDistrict, ref bool __result)
        {
            if (!ColonyViewService.Active || selectedDistrict) return true;
            int visible = 0;
            foreach (BatchControlRow row in __instance._rows)
            {
                EntityComponent entity = row.Entity;
                bool show = (!entity || ColonyViewService.IsOwn(entity)) && row.VisibilityGetter();
                row.Root.ToggleDisplayStyle(show);
                if (show) visible++;
            }
            __instance.VisibleChildrenCount = visible;
            bool any = visible > 0;
            __instance._headerRow.Root.ToggleDisplayStyle(any);
            __result = any;
            return false;
        }
    }

    // The window opens on the district the interface shows; with none, it would open on the whole settlement, whose
    // graphs (goods, population) add both colonies together. Open on this player's biggest district instead.
    [HarmonyPatch(typeof(BatchControlBoxDistrictController), nameof(BatchControlBoxDistrictController.Show))]
    static class ColonyViewBatchControlShowPatcher
    {
        static void Postfix(BatchControlBoxDistrictController __instance)
        {
            if (!ColonyViewService.Active || __instance._batchControlDistrict.SelectedDistrict) return;
            DistrictCenter main = ColonyViewService.Instance.MainDistrict();
            if (!main) return;
            __instance._batchControlDistrict.SetDistrict(main);
            __instance.UpdateDropdown();
        }
    }

    // The window's district list (and the manual migration panel's) offers every district on the map. Offer this
    // player's own instead (and the one already shown, whoever's it is, so the list can show it). Only the lists are
    // changed: choosing from them is this player's own action, judged by the rules as before (ColonyRules).
    [HarmonyPatch(typeof(DistrictDropdownProvider), nameof(DistrictDropdownProvider.UpdateDistrictsList))]
    static class ColonyViewDistrictDropdownPatcher
    {
        static void Postfix(DistrictDropdownProvider __instance)
        {
            if (!ColonyViewService.Active) return;
            ColonyViewDropdowns.KeepOwn(__instance._districtKeys, __instance._districtCenterRegistry, __instance._batchControlDistrict.SelectedDistrict);
        }
    }

    [HarmonyPatch(typeof(ManualMigrationDistrictDropdownProvider), nameof(ManualMigrationDistrictDropdownProvider.UpdateDistrictsList))]
    static class ColonyViewMigrationDropdownPatcher
    {
        static void Postfix(ManualMigrationDistrictDropdownProvider __instance)
        {
            if (!ColonyViewService.Active) return;
            ColonyViewDropdowns.KeepOwn(__instance._districtKeys, __instance._districtCenterRegistry, __instance._selectedDistrict);
        }
    }

    static class ColonyViewDropdowns
    {
        /// <summary>
        /// The game's district lists key each district by its place in the registry's finished districts; any other
        /// key (the whole settlement's) is kept.
        /// </summary>
        internal static void KeepOwn(List<string> keys, DistrictCenterRegistry registry, DistrictCenter shown)
        {
            ReadOnlyList<DistrictCenter> districts = registry.FinishedDistrictCenters;
            keys.RemoveAll(key => int.TryParse(key, out int index) && index >= 0 && index < districts.Count
                && districts[index] != shown && !ColonyViewService.IsOwnDistrict(districts[index]));
        }
    }

    // ---- alerts and notifications ----

    [HarmonyPatch(typeof(StatusAggregator), nameof(StatusAggregator.IsVisible))]
    static class ColonyViewStatusPatcher
    {
        static void Postfix(StatusInstance statusInstance, ref bool __result)
        {
            if (!__result || !ColonyViewService.ActiveThisFrame(out int localSlot)) return;
            long started = ColonyProfiler.StartSampled(ColonyViewService.Alerts);
            if (!ColonyViewService.IsOwnFor(statusInstance.StatusSubject, localSlot)) __result = false;
            ColonyProfiler.StopSampled(ColonyViewService.Alerts, started);
        }
    }

    [HarmonyPatch(typeof(DynamicStatusAggregator), nameof(DynamicStatusAggregator.IsVisible))]
    static class ColonyViewDynamicStatusPatcher
    {
        static void Postfix(StatusInstance statusInstance, ref bool __result)
        {
            if (!__result || !ColonyViewService.ActiveThisFrame(out int localSlot)) return;
            long started = ColonyProfiler.StartSampled(ColonyViewService.Alerts);
            if (!ColonyViewService.IsOwnFor(statusInstance.StatusSubject, localSlot)) __result = false;
            ColonyProfiler.StopSampled(ColonyViewService.Alerts, started);
        }
    }

    // A notifying status (a tragic death) makes its alert row blink as it comes on, whoever's it is: the other colony's
    // death would blink this player's row while one of their own lies there. The event it posts is only for the alert
    // panel (RuntimeChecks: nothing else in the game listens), so skipping it for the other colony changes nothing
    // simulated.
    [HarmonyPatch(typeof(NotifyingStatusMonitor), nameof(NotifyingStatusMonitor.OnStatusToggled))]
    static class ColonyViewNotifyingStatusPatcher
    {
        static bool Prefix(StatusInstance statusInstance)
        {
            // Every status of every building and beaver comes through here as it changes; the game only acts on a
            // notifying one, so only that one is judged.
            if (!statusInstance.IsNotifying || !ColonyViewService.Active) return true;
            // This runs in the tick, as the status comes on: it must never throw.
            try
            {
                return ColonyViewService.IsOwn(statusInstance.StatusSubject);
            }
            catch (Exception error)
            {
                Plugin.LogWarning("[Colony] Could not decide whose an alert is, so it is shown: " + error.Message);
                return true;
            }
        }
    }

    // Births, deaths and the like of the other colony stay out of this player's journal (ColonyJournal: a dead beaver
    // is judged by the colony it was last in, and the panel is listed again once this player is seated). The saved
    // journal is untouched.
    [HarmonyPatch(typeof(NotificationPanel), nameof(NotificationPanel.AddNotification))]
    [HarmonyPriority(Priority.Last)]
    static class ColonyViewNotificationPatcher
    {
        static bool Prefix(Notification notification)
        {
            if (!ColonyViewService.Active) return true;
            // This runs inside NotificationBus.Post, in the tick, and only on computers that filter: it must never throw.
            try
            {
                return ColonyJournal.Instance?.ShouldShow(notification) ?? true;
            }
            catch (Exception error)
            {
                Plugin.LogWarning("[Colony] Could not decide whose a journal entry is, so it is shown: " + error.Message);
                return true;
            }
        }
    }
}
