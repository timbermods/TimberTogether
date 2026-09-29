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
    }
}
