#nullable enable
using System.Reflection;

// 1.4.0-rc29, checks against the compiled mod and the game's assemblies: the status icons over another colony's
// beavers and buildings are not drawn. StatusIconView patches StatusIconCycler.IntervalUpdate and switches off the renderers
// the cycler holds in its fields; this checks both exist and prints the fields for review.
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
    }
}
