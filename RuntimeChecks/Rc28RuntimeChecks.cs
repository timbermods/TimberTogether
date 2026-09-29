#nullable enable
using System.Reflection;

// 1.4.0-rc28, checks against the compiled mod and the game's assemblies: each player's achievements are their own
// colony's. ColonyAchievements.Install logs "Achievements count only your own colony's things in …" as the game loads;
// this finds the same methods without the game, with the mod's own search (Handlers), and prints them for review.
internal static class Rc28RuntimeChecks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Run(Assembly mod, string managedPath, Action<string, Action> test)
    {
        test("rc28: achievements hear of at least one of the game's things, and skip another colony's", () =>
        {
            var loaded = new List<Assembly>();
            foreach (string file in Directory.GetFiles(managedPath, "Timberborn.*Achievement*.dll"))
                loaded.Add(Assembly.Load(Path.GetFileNameWithoutExtension(file)));
            Console.WriteLine("      Achievement assemblies: " + (loaded.Count == 0 ? "none" : string.Join(", ", loaded.Select(a => a.GetName().Name))));
            Type achievements = mod.GetType("BeaverBuddies.Colonies.ColonyAchievements", true)!;
            MethodInfo handlers = achievements.GetMethod("Handlers", All) ?? throw new Exception("ColonyAchievements.Handlers is gone");
            var methods = ((IEnumerable<MethodInfo>)handlers.Invoke(null, new object[] { loaded })!).ToList();
            // rc30: an achievement whose progress is in the world's save counts every colony, so the save is the same everywhere.
            Type saveable = Assembly.Load("Timberborn.WorldPersistence").GetType("Timberborn.WorldPersistence.ISaveableSingleton", true)!;
            var saved = methods.Where(m => saveable.IsAssignableFrom(m.DeclaringType!)).Select(m => m.DeclaringType!.Name + "." + m.Name).ToList();
            if (saved.Count > 0) throw new Exception("achievements kept in the save would skip another colony's things: " + string.Join(", ", saved));
            var found = methods
                .Select(m => m.DeclaringType!.Name + "." + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")").ToList();
            Console.WriteLine("      Achievements skip another colony's things in: " + (found.Count == 0 ? "nothing" : string.Join(", ", found)));
            if (found.Count == 0) throw new Exception("no achievement method hears of a game thing: another colony's buildings would still unlock achievements");
        });
    }
}
