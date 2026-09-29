using BeaverBuddies.Colonies;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.SoundSystem;

namespace BeaverBuddies.Activity
{
    /// <summary>
    /// Another player's sounds, heard or not: the connection panel's sound button on a player's row stops this computer
    /// from playing the sounds their actions make as they are played here (a building deleted, placed or changed). The
    /// game plays those sounds while it plays the action, so every sound the game starts while an action of a muted
    /// player is being played is skipped (ReplayService marks which player's action that is). The player's own actions,
    /// and everything the game plays between actions (the colonies at work, the interface), are heard as ever. Display
    /// only: a sound changes nothing that is simulated, so each computer may hear something different. Kept for the
    /// session: a player's number belongs to it.
    /// </summary>
    public static class RemoteSounds
    {
        // Connection numbers of the players whose actions are not heard here.
        private static readonly HashSet<int> muted = new HashSet<int>();
        // The player whose action is being played now, or -1 between actions.
        private static int playing = -1;

        public static bool IsMuted(int player) => muted.Contains(player);

        public static void SetMuted(int player, bool mute)
        {
            if (mute) muted.Add(player);
            else muted.Remove(player);
            Plugin.Log(mute ? $"Sounds of player {player}'s actions are off on this computer" : $"Sounds of player {player}'s actions are on again");
        }

        public static void ClearMuted() => muted.Clear();

        /// <summary>An action of <paramref name="player"/> is being played; returns what was marked before, for <see cref="Exit"/>.</summary>
        internal static int Enter(int player)
        {
            int previous = playing;
            playing = player;
            return previous;
        }

        internal static void Exit(int previous) => playing = previous;

        /// <summary>A sound starting now belongs to an action of a player this computer has muted.</summary>
        private static bool Silenced => playing >= 0 && muted.Count > 0 && muted.Contains(playing) && playing != ColonySession.LocalPlayer;

        /// <summary>
        /// Puts the check in front of every way the game's sound system starts a sound (each ISoundSystem method named
        /// Play..., as found in this game version). Display only: if none is found, or one cannot be patched, the button
        /// does nothing and the log says so; co-op is never refused for it.
        /// </summary>
        internal static void Install(Harmony harmony)
        {
            try
            {
                Type sound = typeof(ISoundSystem);
                MethodInfo prefix = typeof(RemoteSounds).GetMethod(nameof(SkipIfMuted), BindingFlags.NonPublic | BindingFlags.Static);
                var patched = new List<string>();
                var seen = new HashSet<MethodInfo>();
                foreach (Type type in ImplementersOf(sound))
                {
                    InterfaceMapping map = type.GetInterfaceMap(sound);
                    for (int i = 0; i < map.InterfaceMethods.Length; i++)
                    {
                        MethodInfo target = map.TargetMethods[i];
                        // Only a method that starts a sound and returns nothing can be skipped without its caller noticing;
                        // one inherited by several implementations is patched once. One that calls back when the sound ends
                        // (a speaker, the music) is left alone: skipping it would drop the callback, and no player's action
                        // plays one.
                        if (!map.InterfaceMethods[i].Name.StartsWith("Play", StringComparison.Ordinal) || target.ReturnType != typeof(void)
                            || target.IsAbstract || target.GetParameters().Any(p => typeof(Delegate).IsAssignableFrom(p.ParameterType))
                            || !seen.Add(target)) continue;
                        try
                        {
                            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                            patched.Add(type.Name + "." + target.Name);
                        }
                        catch (Exception error)
                        {
                            Plugin.LogWarning($"Muting a player's sounds can't cover {type.Name}.{target.Name}: {error.Message}");
                        }
                    }
                }
                if (patched.Count == 0) Plugin.LogWarning("Muting a player's sounds found no sound to mute in this game version: the panel's sound button does nothing");
                else Plugin.Log("Muting a player's sounds covers " + string.Join(", ", patched));
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Muting a player's sounds is off: " + error.Message);
            }
        }

        private static IEnumerable<Type> ImplementersOf(Type contract)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a == contract.Assembly || (a.GetName().Name ?? "").StartsWith("Timberborn.", StringComparison.Ordinal));
            foreach (Assembly assembly in assemblies)
                foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
                    if (type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters && contract.IsAssignableFrom(type)) yield return type;
        }

        // Harmony: false skips the game's method (the sound is not played).
        private static bool SkipIfMuted() => !Silenced;
    }
}
