using BeaverBuddies.Events;
using BeaverBuddies.IO;
using BeaverBuddies.Util;
using HarmonyLib;
using System;
using Timberborn.QuickNotificationSystem;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;

namespace BeaverBuddies.Fixes
{
    /// <summary>
    /// The speed panel's "pause or tick once" key (period by default, not dev mode only) calls Ticker.TickOnce when
    /// the game is paused. That runs a whole tick through TickableBucketService.TickOnce, which never goes through
    /// TickBuckets, so the ReplayService does not tick: the tick counter and the shared actions are skipped, and the
    /// tick happens on that computer only, desyncing the game. In a co-op game it is refused with a notice, or, on a
    /// computer held at speed 0 while the shared game runs, with the notice saying where to pause (see the patch
    /// below); in single player the game's own method runs.
    /// </summary>
    public class TickOnceCoopNotice : RegisteredSingleton, ILoadableSingleton
    {
        private readonly QuickNotificationService _quickNotificationService;

        public TickOnceCoopNotice(QuickNotificationService quickNotificationService)
        {
            _quickNotificationService = quickNotificationService;
        }

        // Loadable only so the game creates it with the map, and the patch below can find it.
        public void Load() { }

        public void Show()
        {
            try
            {
                _quickNotificationService.SendWarningNotification(RegisteredLocalizationService.T("BeaverBuddies.TickOnce.CoopBlocked"));
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Could not show the tick once notice: " + error.Message);
            }
        }
    }

    // Priority.First: it replaces the original in a co-op game, so it runs before any other mod's prefix on
    // Ticker.TickOnce. In a co-op game no other mod's prefix then runs on this computer alone when the key is pressed,
    // because this one returns false and Harmony skips the prefixes after it.
    [HarmonyPatch(typeof(Ticker), nameof(Ticker.TickOnce))]
    static class TickerTickOncePatcher
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix()
        {
            // The same hard stop as TickBuckets: after a failed multiplayer action the session is gone (EventIO is
            // null) but the game must not tick on. The dialog already said why, so no notice.
            if (ReplayService.HasReplayFailure) return false;
            if (EventIO.IsNull) return true;

            // In co-op this computer's speed is often held at 0 while the shared game runs: a guest waiting for the
            // host's next tick, a host waiting for a slow guest. The speed panel then sees "paused" and calls tick
            // once, but the player pressed the key on a running game and asked to pause it. Only the connection
            // panel's Pause button pauses a co-op game (SessionPauseEvent), so say so, as the pause key does.
            ReplayService replayService = ReplayEvent.GetReplayServiceIfReady();
            if (replayService != null && replayService.TargetSpeed != 0)
            {
                Plugin.Log("Tick once pressed while the co-op game runs and this computer waits: refused, pause is in the connection panel");
                CoopPauseNotice.ShowPauseFromPanel();
                return false;
            }

            Plugin.Log("Refused tick once in a co-op game: it would tick this computer only");
            SingletonManager.GetSingleton<TickOnceCoopNotice>()?.Show();
            return false;
        }
    }
}
