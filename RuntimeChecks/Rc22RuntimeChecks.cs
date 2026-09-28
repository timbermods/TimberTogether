#nullable enable
using System.Reflection;

// 1.4.0-rc22, checks against the compiled mod and the game's assemblies: the per-player mute finds the game's sounds to
// skip. RemoteSounds.Install logs "Muting a player's sounds covers …" as the game loads; this finds the same methods
// without the game, with the mod's own search (ImplementersOf) and Install's rule, and prints them.
internal static class Rc22RuntimeChecks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Run(Assembly mod, string managedPath, Action<string, Action> test)
    {
        test("rc22: muting a player's sounds covers at least one of the game's ISoundSystem Play methods", () =>
        {
            Type contract = Assembly.Load("Timberborn.SoundSystem").GetType("Timberborn.SoundSystem.ISoundSystem", true)!;
            Type remote = mod.GetType("BeaverBuddies.Activity.RemoteSounds", true)!;
            MethodInfo implementers = remote.GetMethod("ImplementersOf", All) ?? throw new Exception("RemoteSounds.ImplementersOf is gone");
            var covered = new List<string>();
            foreach (Type type in (IEnumerable<Type>)implementers.Invoke(null, new object[] { contract })!)
            {
                InterfaceMapping map = type.GetInterfaceMap(contract);
                for (int i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    MethodInfo target = map.TargetMethods[i];
                    // Install's rule: a Play method that returns nothing and has a body.
                    if (map.InterfaceMethods[i].Name.StartsWith("Play", StringComparison.Ordinal) && target.ReturnType == typeof(void) && !target.IsAbstract)
                        covered.Add(type.Name + "." + target.Name + "(" + string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name)) + ")");
                }
            }
            Console.WriteLine("      Muting a player's sounds covers: " + (covered.Count == 0 ? "nothing" : string.Join(", ", covered)));
            if (covered.Count == 0) throw new Exception("no ISoundSystem Play method to skip: the panel's sound button would do nothing");
        });
    }
}
