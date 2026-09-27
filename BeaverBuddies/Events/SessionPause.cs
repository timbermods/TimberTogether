using BeaverBuddies.Colonies;
using BeaverBuddies.Util;
using System;
using Timberborn.QuickNotificationSystem;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace BeaverBuddies.Events
{
    /// <summary>
    /// A player paused or resumed the co-op game with the connection panel's Pause button: the only way a co-op game
    /// pauses. The game's pause button and keys, menus and dialogs pause nobody (SpeedChangePatcher, SpeedLockPatcher).
    /// Everyone plays it at the start of a tick, like a speed change. The picked speed is kept: resuming runs at it.
    /// </summary>
    [Serializable]
    public class SessionPauseEvent : ReplayEvent
    {
        public override ColonyScope GetColonyScope() => ColonyScope.Global;
        // Like a speed change: it changes when ticks run, not what is in them.
        public override bool ChangesGame() => false;

        public bool paused;

        public override void Replay(IReplayContext context)
        {
            ReplayService replayService = context.GetSingleton<ReplayService>();
            // Before the first tick the game has not run yet: players can still join, and a joiner is not sent this.
            // Every computer plays it on the same tick, so every computer skips it alike.
            if (replayService.TicksSinceLoad == 0)
            {
                Plugin.Log("Event: Pause from the connection panel before the game has started: ignored");
                return;
            }
            if (replayService.IsPausedByPlayer == paused) return;
            // Stopped at once, as a picked speed is set at once (SpeedSetEvent). Setting the pause then lets the speed
            // follow: a guest still behind the tick the host paused on runs on to it (CatchUpSpeed).
            SpeedManager sm = context.GetSingleton<SpeedManager>();
            if (paused && sm.CurrentSpeed != 0) SpeedChangePatcher.SetSpeedSilentlyNow(sm, 0);
            replayService.SetPausedByPlayer(paused, player);
            Plugin.Log($"Event: {(paused ? "Paused" : "Resumed")} from the connection panel by player {player}; " +
                       $"the game runs at {replayService.TargetSpeed}");
        }

        public override string ToActionString() => paused ? "Doing: pause from the connection panel" : "Doing: resume from the connection panel";
    }

    /// <summary>The connection panel's Pause button: asks the session to pause, or to resume.</summary>
    public static class SessionPauseRequest
    {
        /// <summary>Whether the button is offered: in a live session, once the game has started.</summary>
        public static bool CanAsk(ReplayService replayService) =>
            replayService != null && !ReplayService.HasReplayFailure && replayService.TicksSinceLoad > 0;

        /// <summary>Returns false if there is no session to ask, or the game has not started.</summary>
        public static bool Send(bool paused)
        {
            ReplayService replayService = ReplayEvent.GetReplayServiceIfReady();
            if (!CanAsk(replayService)) return false;
            Plugin.Log(paused ? "Pause asked from the connection panel" : "Resume asked from the connection panel");
            ReplayEvent.DoPrefix(() => new SessionPauseEvent { paused = paused });
            return true;
        }
    }

    /// <summary>
    /// Tells a player who tried to pause the co-op game some other way (the pause button, the space or period key) where
    /// its Pause button is, and one who picked a speed while it is paused where to resume it.
    /// </summary>
    public class CoopPauseNotice : RegisteredSingleton, ILoadableSingleton
    {
        // A held or repeated key would stack the same notice.
        private const float RepeatSeconds = 2f;

        private readonly QuickNotificationService _quickNotificationService;
        private float lastShown = -100f;

        public CoopPauseNotice(QuickNotificationService quickNotificationService)
        {
            _quickNotificationService = quickNotificationService;
        }

        // Loadable only so the game creates it with the map, and the patches can find it.
        public void Load() { }

        public static void ShowPauseFromPanel() => SingletonManager.GetSingleton<CoopPauseNotice>()?.Show("BeaverBuddies.Pause.UsePanel");

        public static void ShowResumeFromPanel() => SingletonManager.GetSingleton<CoopPauseNotice>()?.Show("BeaverBuddies.Pause.ResumeFromPanel");

        private void Show(string key)
        {
            float now = Time.unscaledTime;
            if (now - lastShown < RepeatSeconds) return;
            lastShown = now;
            try
            {
                _quickNotificationService.SendWarningNotification(RegisteredLocalizationService.T(key));
            }
            catch (Exception error)
            {
                Plugin.LogWarning("Could not show the pause notice: " + error.Message);
            }
        }
    }
}
