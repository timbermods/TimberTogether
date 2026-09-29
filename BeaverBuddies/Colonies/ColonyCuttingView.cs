using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Timberborn.Forestry;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// Each player sees only their own colony's trees marked for cutting (and marks of nobody's). The game keeps one
    /// cutting area for the whole map, and its interface draws all of it: the other colony's marked trees and the
    /// outline of its area. Here, while the game's interface reads the cutting area (a method of a Timberborn "...UI"
    /// assembly that asks TreeCuttingArea directly), the other colony's marks read as not marked. Everything else,
    /// the lumberjacks and every other part of the simulation, reads the whole area as ever, so nothing simulated
    /// changes and each computer may draw something different.
    /// </summary>
    public static class ColonyCuttingView
    {
        // How many of the interface's readers are running now (they may call each other). Main thread only.
        private static int reading;
        private static bool logged;

        private static readonly string[] Reads = { nameof(TreeCuttingArea.IsInCuttingArea), "get_" + nameof(TreeCuttingArea.CuttingArea) };

        /// <summary>
        /// Patches the two ways to read the area and every interface method that uses them. Display only: if something
        /// cannot be patched, the log says so, every mark stays drawn, and co-op goes on.
        /// </summary>
        internal static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(TreeCuttingArea), nameof(TreeCuttingArea.IsInCuttingArea)),
                    postfix: new HarmonyMethod(typeof(ColonyCuttingView), nameof(HideTile)));
                MethodInfo getter = AccessTools.PropertyGetter(typeof(TreeCuttingArea), nameof(TreeCuttingArea.CuttingArea));
                string listPostfix = ListPostfixFor(getter.ReturnType);
                if (listPostfix != null) harmony.Patch(getter, postfix: new HarmonyMethod(typeof(ColonyCuttingView), listPostfix));
                else Plugin.LogWarning("Another colony's cutting marks: the whole area is read as " + getter.ReturnType.Name + ", which is not filtered");

                var patched = new List<string>();
                foreach (MethodInfo reader in Readers())
                {
                    try
                    {
                        harmony.Patch(reader, prefix: new HarmonyMethod(typeof(ColonyCuttingView), nameof(Enter)),
                            finalizer: new HarmonyMethod(typeof(ColonyCuttingView), nameof(Leave)));
                        patched.Add(reader.DeclaringType.Name + "." + reader.Name);
                    }
                    catch (Exception error)
                    {
                        Plugin.LogWarning($"Another colony's cutting marks can't be hidden from {reader.DeclaringType.Name}.{reader.Name}: {error.Message}");
                    }
                }
                if (patched.Count == 0) Plugin.LogWarning("Another colony's cutting marks: no part of the interface reads the cutting area in this game version, so they stay drawn");
                else Plugin.Log("Another colony's cutting marks are hidden from " + string.Join(", ", patched));
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Another colony's cutting marks stay drawn: " + error.Message);
            }
        }

        /// <summary>
        /// The game's interface methods that read the cutting area directly: every method with a body, in a loaded
        /// Timberborn assembly whose name ends in "UI" and that uses Timberborn.Forestry, that calls IsInCuttingArea or
        /// the CuttingArea getter. Compiler-made methods (lambdas, iterators) are included, as their own types.
        /// </summary>
        internal static List<MethodInfo> Readers()
        {
            var readers = new List<MethodInfo>();
            Assembly forestry = typeof(TreeCuttingArea).Assembly;
            string forestryName = forestry.GetName().Name;
            // The Forestry interface may not be loaded yet: load it by name if it exists.
            try { Assembly.Load("Timberborn.ForestryUI"); } catch (Exception) { }
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name ?? "";
                if (!name.StartsWith("Timberborn.", StringComparison.Ordinal) || !name.EndsWith("UI", StringComparison.Ordinal)) continue;
                if (!assembly.GetReferencedAssemblies().Any(r => r.Name == forestryName)) continue;
                foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
                {
                    if (type.ContainsGenericParameters) continue;
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                        | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                        if (CallsTheArea(method)) readers.Add(method);
                    }
                }
            }
            return readers;
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)).ToDictionary(code => code.Value);

        /// <summary>
        /// A method's instructions with each method or constructor operand resolved, read from its IL. Harmony's own
        /// reader needs MonoMod's runtime emit helpers, which the .NET host RuntimeChecks runs in does not allow.
        /// </summary>
        internal static List<KeyValuePair<OpCode, object>> Instructions(MethodBase method)
        {
            var found = new List<KeyValuePair<OpCode, object>>();
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return found;
            Type[] typeArguments = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            int at = 0;
            while (at < il.Length)
            {
                short value = il[at++];
                if (value == 0xFE) value = (short)(0xFE00 | il[at++]);
                if (!OpCodesByValue.TryGetValue(value, out OpCode code)) break;
                object operand = null;
                int size;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, at); break;
                    default: size = 4; break;
                }
                if (code.OperandType == OperandType.InlineMethod || code.OperandType == OperandType.InlineTok)
                {
                    try { operand = method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments); }
                    catch (Exception) { }
                }
                at += size;
                found.Add(new KeyValuePair<OpCode, object>(code, operand));
            }
            return found;
        }

        private static bool CallsTheArea(MethodInfo method)
        {
            try
            {
                foreach (KeyValuePair<OpCode, object> instruction in ColonyCuttingView.Instructions(method))
                    if (instruction.Value is MethodInfo called && called.DeclaringType == typeof(TreeCuttingArea) && Reads.Contains(called.Name))
                        return true;
            }
            catch (Exception) { }
            return false;
        }

        private static string ListPostfixFor(Type type)
        {
            if (type == typeof(IEnumerable<Vector3Int>)) return nameof(HideEnumerable);
            if (type == typeof(IReadOnlyCollection<Vector3Int>)) return nameof(HideReadOnlyCollection);
            if (type == typeof(IReadOnlyList<Vector3Int>)) return nameof(HideReadOnlyList);
            return null;
        }

        /// <summary>Whether a mark is hidden from this computer's interface now: another colony's, while the interface reads.</summary>
        private static bool Hides(Vector3Int tile, int localSlot)
        {
            int? owner = ColonyMarks.Instance?.RecordedCuttingOwner(tile);
            return owner != null && owner.Value != localSlot;
        }

        private static bool Filtering(out int localSlot)
        {
            localSlot = -1;
            if (reading <= 0 || !ColonyViewService.ActiveThisFrame(out localSlot)) return false;
            if (!logged)
            {
                logged = true;
                Plugin.Log($"[Colony] Trees marked for cutting: colony {localSlot + 1}'s and nobody's are drawn here");
            }
            return true;
        }

        private static void Enter() => reading++;

        private static void Leave() => reading--;

        // The tile by position (__0), whatever the game names it.
        private static void HideTile(Vector3Int __0, ref bool __result)
        {
            if (__result && Filtering(out int slot) && Hides(__0, slot)) __result = false;
        }

        private static List<Vector3Int> Mine(IEnumerable<Vector3Int> area, int slot) => area.Where(tile => !Hides(tile, slot)).ToList();

        private static void HideEnumerable(ref IEnumerable<Vector3Int> __result)
        {
            if (__result != null && Filtering(out int slot)) __result = Mine(__result, slot);
        }

        private static void HideReadOnlyCollection(ref IReadOnlyCollection<Vector3Int> __result)
        {
            if (__result != null && Filtering(out int slot)) __result = Mine(__result, slot);
        }

        private static void HideReadOnlyList(ref IReadOnlyList<Vector3Int> __result)
        {
            if (__result != null && Filtering(out int slot)) __result = Mine(__result, slot);
        }
    }

    /// <summary>
    /// Draws the marks again when whose colony this computer shows changes: a guest is seated only after the save has
    /// loaded, when the interface has already drawn every mark. The game redraws its marks when the cutting area
    /// changes (the event TreeCuttingArea posts); that event is posted here too, but only if nothing but the game's
    /// interface listens to it, so nothing simulated can notice. Otherwise the marks are redrawn at the next change.
    /// </summary>
    public class ColonyCuttingViewRefresher : RegisteredSingleton, IUpdatableSingleton
    {
        private readonly EventBus _eventBus;
        private int shownFor = -1;
        private Type changed;
        private bool looked, usable;

        public ColonyCuttingViewRefresher(EventBus eventBus) => _eventBus = eventBus;

        public void UpdateSingleton()
        {
            int view = ColonyViewService.Active ? ColonySession.LocalSlot : -1;
            if (view == shownFor) return;
            shownFor = view;
            try
            {
                if (!looked) { looked = true; usable = FindEvent(); }
                if (usable) _eventBus.Post(Activator.CreateInstance(changed));
            }
            catch (Exception error)
            {
                usable = false;
                Plugin.LogWarning("[Colony] Could not redraw the trees marked for cutting: " + error.Message);
            }
        }

        private bool FindEvent()
        {
            foreach (string method in new[] { nameof(TreeCuttingArea.AddCoordinates), nameof(TreeCuttingArea.RemoveCoordinates) })
            {
                foreach (KeyValuePair<OpCode, object> instruction in ColonyCuttingView.Instructions(AccessTools.Method(typeof(TreeCuttingArea), method)))
                    if (instruction.Key == OpCodes.Newobj && instruction.Value is ConstructorInfo ctor
                        && ctor.DeclaringType.Name.EndsWith("Event", StringComparison.Ordinal) && ctor.GetParameters().Length == 0)
                    {
                        changed = ctor.DeclaringType;
                        break;
                    }
                if (changed != null) break;
            }
            if (changed == null)
            {
                Plugin.Log("[Colony] Trees marked for cutting are redrawn at the next change: the game's change event was not found");
                return false;
            }
            var listeners = new List<string>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name ?? "";
                if (!name.StartsWith("Timberborn.", StringComparison.Ordinal) && assembly != typeof(Plugin).Assembly) continue;
                foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == changed)
                            listeners.Add(name + ":" + type.Name + "." + method.Name);
                    }
            }
            bool safe = listeners.All(l => l.Split(':')[0].EndsWith("UI", StringComparison.Ordinal));
            Plugin.Log($"[Colony] {changed.Name} is heard by {(listeners.Count == 0 ? "nothing" : string.Join(", ", listeners))}: "
                + (safe ? "it is posted to redraw the trees marked for cutting" : "not posted, the marks are redrawn at the next change"));
            return safe && listeners.Count > 0;
        }
    }
}
