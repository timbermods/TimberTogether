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
            // The redraw event is posted only when nothing but the interface hears it.
            Check(view.Contains("return safe && listeners.Count > 0;"), "the redraw could reach the simulation");
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
    }
}
