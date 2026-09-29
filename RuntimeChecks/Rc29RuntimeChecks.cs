#nullable enable
using System.Reflection;

// 1.4.0-rc29, checks against the compiled mod and the game's assemblies: the status icons over another colony's
// beavers and buildings are not drawn. StatusIconView patches StatusIconCycler.IntervalUpdate and switches off the renderers
// the cycler holds in its fields; this checks both exist and prints the fields for review. And the wellbeing window
// shows only this player's colony (ColonyView).
internal static class Rc29RuntimeChecks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Run(Assembly mod, string managedPath, Action<string, Action> test)
    {
        test("rc29: another colony's status icons: StatusIconCycler.IntervalUpdate exists and holds its icon in a field", () =>
        {
            Type cycler = Assembly.Load("Timberborn.StatusSystem").GetType("Timberborn.StatusSystem.StatusIconCycler", true)!;
            if (cycler.GetMethod("IntervalUpdate", All) == null) throw new Exception("StatusIconCycler.IntervalUpdate is gone");
            Type view = mod.GetType("BeaverBuddies.Colonies.StatusIconView", true)!;
            MethodInfo iconFields = view.GetMethod("IconFields", All) ?? throw new Exception("StatusIconView.IconFields is gone");
            var fields = ((IEnumerable<FieldInfo>)iconFields.Invoke(null, new object[] { cycler })!).Select(f => f.Name + ":" + f.FieldType.Name).ToList();
            Console.WriteLine("      StatusIconCycler fields: " + string.Join(", ", cycler.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.Name + ":" + f.FieldType.Name)));
            Console.WriteLine("      Icon held in: " + (fields.Count == 0 ? "no field" : string.Join(", ", fields)));
            if (fields.Count == 0) throw new Exception("the cycler holds no renderer, game object or transform: an icon already shown would stay drawn");
        });

        // The wellbeing window shows this player's colony. ColonyView patches WellbeingService.GlobalAppliedNeeds itself,
        // which is safe only while the window is its one caller; the two global figures the simulation also reads
        // (AverageGlobalWellbeing, GlobalPopulationData) are left alone and the window's own methods are patched.
        test("rc29: the wellbeing window's need counts are asked for only by the window", () =>
        {
            byte[] name = System.Text.Encoding.ASCII.GetBytes("GlobalAppliedNeeds");
            var naming = Directory.GetFiles(managedPath, "Timberborn.*.dll")
                .Where(file => File.ReadAllBytes(file).AsSpan().IndexOf(name) >= 0)
                .Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
            if (!naming.SequenceEqual(new[] { "Timberborn.Wellbeing", "Timberborn.WellbeingUI" }))
                throw new Exception("GlobalAppliedNeeds is now named in " + string.Join(", ", naming));
            Type box = Assembly.Load("Timberborn.WellbeingUI").GetType("Timberborn.WellbeingUI.PopulationWellbeingBox", true)!;
            var callers = IlScan.Of(box).Where(pair => IlScan.Names(pair.Value, "Timberborn.Wellbeing.WellbeingService", "GlobalAppliedNeeds"))
                .Select(pair => pair.Key.Name).ToList();
            if (!callers.SequenceEqual(new[] { "UpdateAppliedNeedsCount" }))
                throw new Exception("GlobalAppliedNeeds is now called from PopulationWellbeingBox." + string.Join(", ", callers));
            foreach (string figure in new[] { "get_AverageGlobalWellbeing", "get_GlobalPopulationData" })
            {
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(figure);
                Console.WriteLine($"      {figure} is read in: " + string.Join(", ", Directory.GetFiles(managedPath, "Timberborn.*.dll")
                    .Where(file => File.ReadAllBytes(file).AsSpan().IndexOf(bytes) >= 0).Select(Path.GetFileNameWithoutExtension).OrderBy(n => n)));
            }
        });
    }
}
