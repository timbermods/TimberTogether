using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.BaseComponentSystem;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// Each player earns achievements for their own colony. Every computer plays the whole map, so the game's
    /// achievements heard every colony's buildings and beavers: a beehive the other player built unlocked it here too.
    /// In a game that shows one colony, the game's achievement code (the Timberborn assemblies named "…Achievement…")
    /// skips any event about another colony's thing: each of its methods that returns nothing is given the event, and
    /// when that event (or a field or property of it), or the part the method belongs to, is another colony's building,
    /// beaver or other entity, the method is skipped. Things of nobody's, and events about no thing, count for everyone. Achievements are this computer's
    /// own (Steam's), so nothing simulated changes.
    /// </summary>
    public static class ColonyAchievements
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly HashSet<string> Lifecycle = new HashSet<string> { "OnDestroy", "OnEnable", "OnDisable", "OnValidate" };

        // Per argument type: its fields and properties that hold a game thing, found once.
        private static readonly Dictionary<Type, Func<object, object>[]> readers = new Dictionary<Type, Func<object, object>[]>();

        /// <summary>Display only: if nothing is found or a method can't be patched, the log says so and co-op goes on.</summary>
        internal static void Install(Harmony harmony)
        {
            try
            {
                var prefix = new HarmonyMethod(typeof(ColonyAchievements), nameof(SkipOtherColony));
                var patched = new List<string>();
                foreach (MethodInfo handler in Handlers(AppDomain.CurrentDomain.GetAssemblies()))
                {
                    try
                    {
                        harmony.Patch(handler, prefix: prefix);
                        patched.Add(handler.DeclaringType.Name + "." + handler.Name);
                    }
                    catch (Exception error)
                    {
                        Plugin.LogWarning($"Achievements: {handler.DeclaringType.Name}.{handler.Name} still counts every colony: {error.Message}");
                    }
                }
                if (patched.Count == 0) Plugin.LogWarning("Achievements: nothing found to keep to your own colony in this game version");
                else Plugin.Log("Achievements count only your own colony's things in " + string.Join(", ", patched));
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Achievements count every colony: " + error.Message);
            }
        }

        /// <summary>
        /// The game's achievement methods that are told about something: in a Timberborn assembly whose name holds
        /// "Achievement", every method with a body that returns nothing and takes at least one argument that is, or
        /// holds, a game thing (a component, or an event with one in a field or property); and every such method of an
        /// achievement part that sits on a building or beaver itself.
        /// </summary>
        internal static List<MethodInfo> Handlers(IEnumerable<Assembly> assemblies)
        {
            var handlers = new List<MethodInfo>();
            foreach (Assembly assembly in assemblies)
            {
                string name = assembly.GetName().Name ?? "";
                if (!name.StartsWith("Timberborn.", StringComparison.Ordinal) || name.IndexOf("Achievement", StringComparison.Ordinal) < 0) continue;
                foreach (Type type in TypesOf(assembly))
                {
                    if (!type.IsClass || type.ContainsGenericParameters) continue;
                    foreach (MethodInfo method in type.GetMethods(Declared))
                    {
                        if (method.IsAbstract || method.ContainsGenericParameters || method.ReturnType != typeof(void)
                            || method.GetMethodBody() == null) continue;
                        // A part's own handlers (OnEnterFinishedState, say), never its set-up and clean-up, which must run.
                        bool ownThing = !method.IsStatic && typeof(BaseComponent).IsAssignableFrom(type)
                            && method.Name.StartsWith("On", StringComparison.Ordinal) && !Lifecycle.Contains(method.Name);
                        if (ownThing || method.GetParameters().Any(p => ReadersFor(p.ParameterType).Length > 0)) handlers.Add(method);
                    }
                }
            }
            return handlers;
        }

        private static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error) { return error.Types.Where(t => t != null); }
        }

        /// <summary>How to reach the game things an argument of this type is, or holds.</summary>
        private static Func<object, object>[] ReadersFor(Type type)
        {
            lock (readers)
            {
                if (readers.TryGetValue(type, out var found)) return found;
                var list = new List<Func<object, object>>();
                if (typeof(BaseComponent).IsAssignableFrom(type)) list.Add(value => value);
                else if (!type.IsPrimitive && type != typeof(string) && !type.IsEnum)
                {
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        if (typeof(BaseComponent).IsAssignableFrom(field.FieldType)) list.Add(field.GetValue);
                    foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (typeof(BaseComponent).IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0
                            && property.GetGetMethod() != null)
                            list.Add(value => property.GetValue(value));
                }
                found = list.ToArray();
                readers[type] = found;
                return found;
            }
        }

        // Harmony: false skips the game's method (the achievement doesn't hear of another colony's thing).
        private static bool SkipOtherColony(object __instance, object[] __args)
        {
            if (!ColonyViewService.ActiveThisFrame(out _)) return true;
            // Runs in the game's tick (an achievement hears a building finish): it must never throw.
            try
            {
                // An achievement part of a building or beaver itself.
                if (__instance is BaseComponent self && self && !ColonyViewService.IsOwn(self)) return false;
                if (__args == null) return true;
                foreach (object argument in __args)
                {
                    if (argument == null) continue;
                    foreach (Func<object, object> read in ReadersFor(argument.GetType()))
                        if (read(argument) is BaseComponent thing && thing && !ColonyViewService.IsOwn(thing)) return false;
                }
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Achievements: could not tell whose thing an achievement heard of, so it counts: " + error.Message);
            }
            return true;
        }
    }
}
