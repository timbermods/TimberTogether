#nullable enable
using System.Reflection;

// 1.4.0-rc27, checks against the compiled mod and the game's assemblies: another colony's trees marked for cutting are
// hidden from the game's interface. ColonyCuttingView.Install logs "Another colony's cutting marks are hidden from …" as
// the game loads; this finds the same methods without the game, with the mod's own search (Readers), and prints them.
internal static class Rc27RuntimeChecks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Run(Assembly mod, string managedPath, Action<string, Action> test)
    {
        test("rc27: another colony's cutting marks are hidden from at least one of the game's interface methods", () =>
        {
            Assembly.Load("Timberborn.ForestryUI");
            Type view = mod.GetType("BeaverBuddies.Colonies.ColonyCuttingView", true)!;
            MethodInfo readers = view.GetMethod("Readers", All) ?? throw new Exception("ColonyCuttingView.Readers is gone");
            var found = ((IEnumerable<MethodInfo>)readers.Invoke(null, null)!)
                .Select(m => m.DeclaringType!.Assembly.GetName().Name + ":" + m.DeclaringType.Name + "." + m.Name).ToList();
            Console.WriteLine("      Cutting marks are hidden from: " + (found.Count == 0 ? "nothing" : string.Join(", ", found)));
            if (found.Count == 0) throw new Exception("no interface method reads the cutting area: the other colony's marks would stay drawn");
            Type area = Assembly.Load("Timberborn.Forestry").GetType("Timberborn.Forestry.TreeCuttingArea", true)!;
            Type returned = area.GetProperty("CuttingArea")!.PropertyType;
            Console.WriteLine("      TreeCuttingArea.CuttingArea is " + returned);
        });
    }
}
