using BeaverBuddies.Colonies;

/// <summary>
/// 1.4.0-rc27, from rc26 play: another colony's trees marked for cutting are not drawn. Checks that need no game: they
/// read the source (RuntimeChecks lists which of the game's interface methods get the filter).
/// </summary>
static class Rc27Checks
{
    static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }

    static string Source(params string[] parts)
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "BeaverBuddies.sln"))) root = Path.GetDirectoryName(root)!;
        Check(root != null, "could not find the repository root");
        return File.ReadAllText(Path.Combine(new[] { root! }.Concat(parts).ToArray())).Replace("\r\n", "\n");
    }

    public static IEnumerable<(string Name, Action Run)> Tests()
    {
        yield return ("rc27: another colony's cutting marks are hidden only from the game's interface, never from the simulation", () =>
        {
            string view = Source("BeaverBuddies", "Colonies", "ColonyCuttingView.cs");
            // The filter reads only while an interface method runs, on a computer that shows one colony.
            Check(view.Contains("if (reading <= 0 || !ColonyViewService.ActiveThisFrame(out localSlot)) return false;"), "the filter does not wait for the interface");
            Check(view.Contains("!name.EndsWith(\"UI\", StringComparison.Ordinal)) continue;"), "readers are looked for outside the game's interface assemblies");
            Check(view.Contains("finalizer: new HarmonyMethod(typeof(ColonyCuttingView), nameof(Leave))"), "a reader that throws would leave the filter on");
            // Nobody's marks and this colony's own stay drawn.
            Check(view.Contains("return owner != null && owner.Value != localSlot;"), "the wrong marks are hidden");
            // The owner is read without asking the game again (that would call the filter from inside itself).
            Check(view.Contains("ColonyMarks.Instance?.RecordedCuttingOwner(tile)") && !System.Text.RegularExpressions.Regex.IsMatch(view, @"[^d]CuttingOwner\("), "the filter asks the game from inside itself");
            // The redraw never posts the game's change event (the simulation hears it): the drawer is told to draw again.
            Check(!view.Contains("_eventBus.Post(") && view.Contains("_treeCuttingAreaVisualizer.UpdateOrMarkForUpdate();"), "the redraw could reach the simulation");
            string plugin = Source("BeaverBuddies", "Plugin.cs");
            Check(plugin.Contains("Colonies.ColonyCuttingView.Install(harmony);") && !plugin.Contains("Install(nameof(ColonyCuttingView)"),
                "the filter is not installed, or refuses co-op when it fails");
            Check(Source("BeaverBuddies", "Colonies", "ColonyConfigurator.cs").Contains("Bind<ColonyCuttingViewRefresher>().AsSingleton();"), "the redraw is not bound");
        });
        yield return ("rc28: achievements skip another colony's things, never run in the simulation, and never throw", () =>
        {
            string achievements = Source("BeaverBuddies", "Colonies", "ColonyAchievements.cs");
            Check(achievements.Contains("name.IndexOf(\"Achievement\", StringComparison.Ordinal) < 0) continue;"), "methods outside the game's achievement assemblies are patched");
            Check(achievements.Contains("method.ReturnType != typeof(void)"), "a method whose answer is used could be skipped");
            Check(achievements.Contains("if (!ColonyViewService.ActiveThisFrame(out _)) return true;"), "achievements are filtered alone or in a shared game");
            Check(achievements.Contains("!ColonyViewService.IsOwn(thing)) return false;") && achievements.Contains("!ColonyViewService.IsOwn(self)) return false;"),
                "another colony's thing still counts");
            Check(achievements.Contains("!Lifecycle.Contains(method.Name)"), "a part's set-up or clean-up could be skipped");
            string plugin = Source("BeaverBuddies", "Plugin.cs");
            Check(plugin.Contains("Colonies.ColonyAchievements.Install(harmony);") && !plugin.Contains("Install(nameof(ColonyAchievements)"),
                "achievements are not kept apart, or refuse co-op when they can't be");
        });
        yield return ("rc29: status icons only over your own colony; the eye hides only construction and has the game's tooltip", () =>
        {
            string icons = Source("BeaverBuddies", "Colonies", "StatusIconView.cs");
            Check(icons.Contains("ColonyViewService.ActiveThisFrame(out _) && !ColonyViewService.IsOwn(component)"), "icons are judged alone or by the wrong colony");
            Check(icons.Contains("heldObject == self || self.transform.IsChildOf(heldObject.transform)"), "the icon hider could hide the thing itself");
            Check(Source("BeaverBuddies", "Plugin.cs").Contains("Colonies.StatusIconView.Install(harmony);"), "the icon filter is not installed");
            string construction = Source("BeaverBuddies", "Colonies", "ConstructionVisibility.cs");
            Check(construction.Contains("blockObject.IsFinished ||") && construction.Contains("|| IsFinished(entity))"), "the eye hides finished buildings");
            string view = Source("BeaverBuddies", "Panel", "ConnectionPanelView.cs");
            Check(view.Contains("Tooltips?.Register(element, text);"), "the panel's buttons have no tooltip in the game");
            string csv = Source("BeaverBuddies", "Localizations", "enUS_BeaverBuddie.csv");
            Check(csv.Contains("BeaverBuddies.Panel.EyeOnTooltip,\"Hide construction\"") && csv.Contains("BeaverBuddies.Panel.EyeOffTooltip,\"Show construction\""),
                "the eye's tooltip is not Hide construction / Show construction");
        });
        yield return ("rc29: the wellbeing window counts only your own colony's beavers, and nothing simulated is changed", () =>
        {
            string view = Source("BeaverBuddies", "Colonies", "ColonyView.cs");
            // The need counts: this player's districts, each counted as the game counts a selected one.
            Check(view.Contains("[HarmonyPatch(typeof(WellbeingService), nameof(WellbeingService.GlobalAppliedNeeds))]")
                && view.Contains("WellbeingService.AppliedNeeds(districtCenter.DistrictPopulation.GetEnabledCharacters<NeedManager>(), appliedNeeds);"),
                "the window's need counts are not this colony's");
            // The two figures the simulation also reads are left alone: the window's own getter and update are patched.
            Check(view.Contains("[HarmonyPatch(typeof(PopulationWellbeingBox), nameof(PopulationWellbeingBox.ContextualPopulationData), MethodType.Getter)]")
                && view.Contains("[HarmonyPatch(typeof(PopulationWellbeingBox), nameof(PopulationWellbeingBox.UpdateAverageWellbeing))]"),
                "the window's beaver count or average is not this colony's");
            Check(!view.Contains("nameof(WellbeingService.AverageGlobalWellbeing)") && !view.Contains("nameof(PopulationService.GlobalPopulationData)"),
                "a global figure the simulation reads is patched");
            // A selected district keeps the game's figures.
            Check(view.Contains("int wellbeing = ColonyViewService.Instance.ColonyWellbeing() ?? 0;")
                && System.Text.RegularExpressions.Regex.Matches(view, @"if \(!ColonyViewService\.Active \|\| __instance\._districtContextService\.SelectedDistrict\) return").Count >= 5,
                "a selected district no longer shows the game's figures");
            // The faction goal's progress is the colony's too; the game still unlocks by the whole map.
            Check(view.Contains("[HarmonyPatch(typeof(GoalRowFactory), nameof(GoalRowFactory.UpdateProgress),")
                && view.Contains("__instance.UpdateProgress($\"{wellbeing} / {unlockableFactionSpec.AverageWellbeingToUnlock}\", goalRowElement);"),
                "the faction goal's progress is not this colony's");
            string checks = Source("RuntimeChecks", "ColonyRuntimeChecks.cs");
            Check(checks.Contains("\"GlobalAppliedNeeds\"),") && checks.Contains("\"get_ContextualPopulationData\"),") && checks.Contains("\"UpdateAverageWellbeing\"),")
                && checks.Contains("(\"Timberborn.WellbeingUI.GoalRowFactory\", \"Timberborn.WellbeingUI\", \"UpdateProgress\"),"),
                "RuntimeChecks does not list the window's methods");
        });
        yield return ("rc30: the connection panel is drawn under the game's windows and hidden with its corner, and its slot has an order", () =>
        {
            string lift = Source("BeaverBuddies", "Panel", "CornerLift.cs");
            // Just before the entity panel and the windows, never after them.
            Check(lift.Contains("const string FrontName = \"Absolute-items\";") && lift.Contains("host.Insert(host.IndexOf(front), layer);")
                && lift.Contains("if (host.IndexOf(layer) != host.IndexOf(front) - 1) layer.PlaceBehind(front);") && !lift.Contains("BringToFront"),
                "the panel is drawn over the game's windows, menus and dialogs");
            Check(lift.Contains("bool shown = Displayed(slot, host);") && lift.Contains("layer.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;")
                && lift.Contains("if (e.style.display == DisplayStyle.None || e.resolvedStyle.display == DisplayStyle.None) return false;"),
                "the panel stays on screen while the game hides its corner");
            // Another mod adding to the corner later must find an order for every panel in it.
            Check(lift.Contains("layout._elementOrder[slot] = order;") && lift.Contains("layout?._elementOrder.Remove(slot);"),
                "the slot has no order in its corner, or keeps it once gone");
        });
        yield return ("rc30: the panel's rows and facts are rebuilt only when they change, and its tooltips are the game's", () =>
        {
            string view = Source("BeaverBuddies", "Panel", "ConnectionPanelView.cs");
            Check(!view.Contains("rows.Clear();\n            foreach (var row in model.Rows)") && view.Contains("if (signature.ToString() != rowsShown)")
                && view.Contains("if (ping.text != shown[i].PingText) ping.text = shown[i].PingText;"), "the rows are built again at every refresh");
            Check(view.Contains("if (!same)\n            {\n                facts.Clear(); factLines.Clear();"), "the facts are built again at every refresh");
            // No tooltip set only on the element (never shown in the game) except in the helper that also registers it.
            Check(System.Text.RegularExpressions.Regex.Matches(view, @"\.tooltip = ").Count == 1 && view.Contains("element.tooltip = text;\n            try { Tooltips?.Register(element, text); }"),
                "a panel tooltip is not the game's");
            Check(view.Contains("Tooltip(line, loc.T(labelKey + \".Tooltip\"));") && view.Contains("Tooltips?.RegisterUpdatable(element, text);"),
                "the frame rate floor or the paused line has no tooltip in the game");
            string chat = Source("BeaverBuddies", "Panel", "ChatView.cs");
            Check(System.Text.RegularExpressions.Regex.Matches(chat, @"\.tooltip = ").Count == 1 && chat.Contains("try { tooltips?.Register(element, text); }")
                && chat.Contains("Tooltip(badge, loc.T(\"BeaverBuddies.Chat.Unseen.Tooltip\"));") && chat.Contains("Tooltip(row, loc.T(\"BeaverBuddies.Chat.Boost.Tooltip\"));"),
                "a chat tooltip is not the game's");
        });
        yield return ("rc30: Enter that leaves a text box never opens the chat, and names are never split over lines", () =>
        {
            string chat = Source("BeaverBuddies", "Panel", "ChatView.cs");
            int boost = chat.IndexOf("void OnBoostKeyDown(KeyDownEvent e)", StringComparison.Ordinal);
            Check(boost >= 0 && chat.IndexOf("keyFrame = Time.frameCount;", boost, StringComparison.Ordinal) is int at && at > boost
                && at < chat.IndexOf("else if (e.keyCode == KeyCode.Escape)", boost, StringComparison.Ordinal), "Enter in the speed boost box can open the chat");
            string service = Source("BeaverBuddies", "Panel", "ConnectionPanelService.cs");
            Check(service.Contains("root.RegisterCallback<UnityEngine.UIElements.KeyDownEvent>(OnAnyKeyDown, UnityEngine.UIElements.TrickleDown.TrickleDown);")
                && service.Contains("up is UnityEngine.UIElements.TextField || up is UnityEngine.UIElements.IntegerField || up is UnityEngine.UIElements.FloatField")
                && service.Contains("if (Time.frameCount - textBoxEnterFrame <= 2) return;"), "Enter in a Trading Post's box can open the chat");
            Check(service.Contains("watchedRoot?.UnregisterCallback<UnityEngine.UIElements.KeyDownEvent>(OnAnyKeyDown"), "the Enter watch outlives the scene");
            // The splitter itself, run here: a sentence per line, but not after a short capitalised word.
            string native = Source("BeaverBuddies", "Util", "NativeElements.cs");
            var pattern = System.Text.RegularExpressions.Regex.Match(native, "Regex\\.Replace\\(text, @\"([^\"]+)\", \"\\\\n\"\\)");
            Check(pattern.Success, "the sentence splitter is not where it was");
            string Split(string text) => System.Text.RegularExpressions.Regex.Replace(text, pattern.Groups[1].Value, "\n");
            Check(Split("Dr. Beaver sent logs. Then they left.") == "Dr. Beaver sent logs.\nThen they left.", "a name is split over two lines: " + Split("Dr. Beaver sent logs. Then they left."));
            Check(Split("The trade is done! Colony 2 has it. Next round starts.") == "The trade is done!\nColony 2 has it.\nNext round starts.", "sentences are no longer one per line");
        });
        yield return ("rc30: another colony's construction: the game redraws a released site, and sites come from its state events", () =>
        {
            string construction = Source("BeaverBuddies", "Colonies", "ConstructionVisibility.cs");
            // A site that finished while hidden: its scaffold is not switched back on over the building.
            Check(construction.Contains("IBlockObjectModel model = entity.GetComponent<IBlockObjectModel>();")
                && construction.Contains("model.UpdateModelVisibility();") && construction.Contains("            Redraw(entity);"),
                "a released site keeps the renderers as they were recorded, not as the game draws it now");
            // Only sites being built are walked, found from the game's state events (and once from the map).
            Check(construction.Contains("public void OnEnteredUnfinishedState(EnteredUnfinishedStateEvent enteredUnfinishedStateEvent)")
                && construction.Contains("public void OnExitedUnfinishedState(ExitedUnfinishedStateEvent exitedUnfinishedStateEvent)")
                && construction.Contains("public void Load() => _eventBus.Register(this);"), "sites are not followed from the game's events");
            Check(construction.Contains("foreach (BlockObject blockObject in sites)"), "every entity is still walked on each pass");
        });
        yield return ("rc30: achievements whose progress is saved, and clean-ups, are never skipped", () =>
        {
            string achievements = Source("BeaverBuddies", "Colonies", "ColonyAchievements.cs");
            Check(achievements.Contains("type.ContainsGenericParameters || IsSaved(type)) continue;")
                && achievements.Contains("typeof(ISaveableSingleton).IsAssignableFrom(type) || typeof(IPersistentEntity).IsAssignableFrom(type)"),
                "an achievement kept in the world's save could count differently on each computer");
            Check(achievements.Contains("|| method.GetMethodBody() == null || IsCleanUp(method)) continue;")
                && achievements.Contains("if (called.Name.StartsWith(\"add_\", StringComparison.Ordinal)) return false;"),
                "a clean-up that stops listening could be skipped and leave its listener behind");
        });
        yield return ("rc30: cutting marks: trees and new trees filtered, the marking preview left to the game, redrawn without the event", () =>
        {
            string view = Source("BeaverBuddies", "Colonies", "ColonyCuttingView.cs");
            Check(view.Contains("\"get_\" + nameof(TreeCuttingArea.YieldersInArea)") && view.Contains("nameof(HideTrees)"),
                "the other colony's marked trees are highlighted while the tool is open");
            Check(view.Contains("nameof(TreeCuttingAreaVisualizer.OnTreeAddedToCuttingArea)") && view.Contains("nameof(SkipOtherTree)")
                && view.Contains("return __0 == null || !ColonyViewService.ActiveThisFrame(out int slot) || !HidesTree(__0.TreeComponent, slot);"),
                "a new tree on the other colony's mark is highlighted");
            Check(view.Contains("private const string MarkingPreview = nameof(TreeCuttingAreaSelectionTool) + \".PreviewCallback\";")
                && view.Contains("if (type.Name + \".\" + method.Name == MarkingPreview) continue;"),
                "the marking tool's preview draws the other colony's marks as markable");
            Check(view.Contains("public ColonyCuttingViewRefresher(TreeCuttingAreaVisualizer treeCuttingAreaVisualizer)"), "the redraw does not use the game's drawer");
        });
        yield return ("rc30: status icons: colliders too, hidden once, and judged as a status comes on", () =>
        {
            string icons = Source("BeaverBuddies", "Colonies", "StatusIconView.cs");
            Check(icons.Contains("collider.enabled = false;") && icons.Contains("foreach (Collider c in state.Disabled) if (c) c.enabled = true;"),
                "a hidden icon can still be pointed at");
            Check(icons.Contains("if (other && !state.Applied) Hide(__instance, state);"), "the icon is looked for again on every recheck");
            Check(icons.Contains("AccessTools.DeclaredMethod(cycler, \"UpdateIcon\")") && icons.Contains("private static void AfterUpdateIcon(object __instance) => IsOther(__instance);"),
                "a new status's icon shows until the next interval update");
        });
        yield return ("rc30: the top bar's wellbeing with no beavers of your own, the goods chart and district lists are your colony's", () =>
        {
            string view = Source("BeaverBuddies", "Colonies", "ColonyView.cs");
            Check(!view.Contains("if (wellbeing == null) return;")
                && view.Contains("button.AddToClassList(bots ? BasicStatisticsPanel.BeaversPerishedClass : BasicStatisticsPanel.AllPerishedClass);"),
                "with no beavers of their own a player sees the whole map's wellbeing");
            Check(view.Contains("[HarmonyPatch(typeof(GoodStockpilesTooltipFactory), nameof(GoodStockpilesTooltipFactory.GetGoodSamplingRegistry))]")
                && view.Contains("__result = ColonyViewService.Instance.ColonyGoodSamplingRegistry(__result);"), "the goods chart is the whole map's");
            Check(view.Contains("[HarmonyPatch(typeof(DistrictDropdownProvider), nameof(DistrictDropdownProvider.UpdateDistrictsList))]")
                && view.Contains("[HarmonyPatch(typeof(ManualMigrationDistrictDropdownProvider), nameof(ManualMigrationDistrictDropdownProvider.UpdateDistrictsList))]")
                && view.Contains("districts[index] != shown && !ColonyViewService.IsOwnDistrict(districts[index])"), "the district lists offer the other colony's districts");
            string checks = Source("RuntimeChecks", "ColonyRuntimeChecks.cs");
            foreach (string method in new[] { "\"GetGoodSamplingRegistry\"),", "\"OnTreeAddedToCuttingArea\"),", "\"UpdateOrMarkForUpdate\"),",
                "\"get_YieldersInArea\"),", "\"UpdateIcon\"),", "\"UpdateModelVisibility\"),", "\"UpdateDistrictsList\")," })
                Check(checks.Contains(method), "RuntimeChecks does not list " + method);
        });
        yield return ("rc30: a colony's first wellbeing record is kept but not announced, on every computer alike", () =>
        {
            Check(!WellbeingRecords.Announces(0) && WellbeingRecords.Announces(1), "the first record is announced, or a later one is not");
            int[] records = new int[ColonySlotTable.MaxSlots];
            Check(WellbeingRecords.Raise(records, new int?[] { 3, null, null, null }).SequenceEqual(new[] { 0 }) && records[0] == 3, "the first record is not kept");
            string source = Source("BeaverBuddies", "Colonies", "ColonyWellbeingRecords.cs");
            Check(source.Contains("int[] before = (int[])records.Clone();") && source.Contains("&& WellbeingRecords.Announces(before[slot])) Announce(records[slot]);"),
                "the first record is announced");
        });
        yield return ("rc31: the Global history graphs (F9, F10) show your colony, built for the display and never saved", () =>
        {
            string history = Source("BeaverBuddies", "Colonies", "ColonyHistoryView.cs");
            // The graphs are handed the colony's history in place of the whole map's, only while one colony is shown.
            Check(history.Contains("[HarmonyPatch(typeof(PopulationStatisticsGraphFactory), nameof(PopulationStatisticsGraphFactory.Create), new[] { typeof(PopulationSampleHistory) })]")
                && history.Contains("ColonyHistoryView.Instance?.SwapPopulation(ref populationSampleHistory);"), "the population graphs are the whole map's");
            Check(history.Contains("[HarmonyPatch(typeof(GoodStatisticsGroupFactory), nameof(GoodStatisticsGroupFactory.Create))]")
                && history.Contains("ColonyHistoryView.Instance?.SwapGoods(ref goodSamplingRegistry);"), "the goods charts are the whole map's");
            Check(System.Text.RegularExpressions.Regex.Matches(history, @"if \(!ColonyViewService\.Active\) return;\n            ColonyHistoryView\.Instance\?\.Swap").Count == 2,
                "the graphs are swapped alone or in a shared game");
            // Only the whole map's history is swapped: a district's graphs stay the game's.
            Check(history.Contains("if (global == null || !ReferenceEquals(history, global)) return;")
                && history.Contains("if (global == null || !ReferenceEquals(registry, global)) return;"), "a district's graphs could be swapped");
            // The game's registries are only read: nothing is added to them, and the colony's lists are this view's own.
            Check(!history.Contains(".AddSample(") && !history.Contains(".Add(sample") && !history.Contains(".PopulationSampleHistory =")
                && !history.Contains(".GoodSamplingRegistry ="), "a game registry could be written");
            Check(history.Contains("PopulationSampleHistory.CreateFromSave(populationSamples)") && history.Contains("GoodSampleHistory.CreateFromSave(all.GoodId, samples)"),
                "the colony's history is not a list of its own");
            // Summed day by day from the latest; wellbeing weighted by beavers, as the top bar's.
            Check(history.Contains("if (back >= samples.Count) continue;") && history.Contains("if (back < district.Count) sum += district[district.Count - 1 - back];")
                && history.Contains("wellbeing += (long)sample.Wellbeing * count;"), "the colony's days are not lined up, or wellbeing is not weighted by beavers");
            // Rebuilt only for a new day or a change of districts, and only when a graph of the colony's asks.
            Check(history.Contains("if (!populationBuilt.Changed(days.Count, active, own)) return;") && history.Contains("if (!goodsBuilt.Changed(samples, active, own)) return;")
                && history.Contains("ReferenceEquals(history, populationHistory)) RefreshPopulation();") && history.Contains("goodsHistories.Contains(history)) RefreshGoods();"),
                "the colony's histories are rebuilt on every redraw");
            Check(Source("BeaverBuddies", "Colonies", "ColonyConfigurator.cs").Contains("containerDefinition.Bind<ColonyHistoryView>().AsSingleton();"), "the history view is not bound");
            string checks = Source("RuntimeChecks", "ColonyRuntimeChecks.cs");
            foreach (string method in new[] { "PopulationStatisticsGraphFactory\", \"Timberborn.PopulationStatisticsBatchControl\", \"Create\"),",
                "PopulationStatisticsGraph\", \"Timberborn.PopulationStatisticsBatchControl\", \"UpdateItem\"),",
                "GoodStatisticsGroupFactory\", \"Timberborn.GoodStatisticsBatchControl\", \"Create\"),",
                "GoodSampleHistoryElement\", \"Timberborn.GoodStatisticsUI\", \"Update\"),", "\"_populationSampleHistory\"),", "\"_goodSampleHistory\"),",
                "\"populationSampleHistory\", \"Timberborn.PopulationStatisticsSampling.PopulationSampleHistory\"),",
                "\"goodSamplingRegistry\", \"Timberborn.GoodsSampling.GoodSamplingRegistry\")," })
                Check(checks.Contains(method), "RuntimeChecks does not list " + method);
            string rules = System.Text.RegularExpressions.Regex.Replace(Source("TWO-COLONIES.md"), @"\s+", " ");
            Check(!rules.Contains("*Global* history graphs") && rules.Contains("Faction unlocks still cover the whole map."), "TWO-COLONIES.md says the Global graphs are the whole map's");
        });
    }
}
