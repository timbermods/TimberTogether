using BeaverBuddies.Colonies;
using BeaverBuddies.IO;
using HarmonyLib;
using System;
using System.Collections.Generic;
using Timberborn.TimeSpeedButtonSystem;
using Timberborn.TimeSystem;
using Timberborn.TimeSystemUI;
using Timberborn.UILayoutSystem;
using static BeaverBuddies.SingletonManager;

namespace BeaverBuddies.Events
{
    [Serializable]
    public class SpeedSetEvent : ReplayEvent
    {
        public override ColonyScope GetColonyScope() => ColonyScope.Global;
        // Unpausing starts the first tick, which closes joining itself.
        public override bool ChangesGame() => false;

        // The speed the player picked (the game's buttons: 1, 3, 7; 0 paused). The game runs at it plus the
        // session's boost (SpeedBoost), so with a boost of +0.5 a pick of 3 runs at 3.5.
        public float speed;

        public override void Replay(IReplayContext context)
        {
            SpeedManager sm = context.GetSingleton<SpeedManager>();
            ReplayService replayService = context.GetSingleton<ReplayService>();
            // Paused from the connection panel, a pick is the speed the game resumes at.
            float target = replayService.IsPausedByPlayer ? 0 : SpeedBoost.Apply(speed, replayService.Boost);
            Plugin.Log($"Event: Changing speed from {sm.CurrentSpeed} to {target}"
                + (replayService.IsPausedByPlayer ? $" (speed {speed} once resumed)"
                    : target != speed ? $" (speed {speed} with a boost of {SpeedBoost.Format(replayService.Boost)})" : ""));
            if (sm.CurrentSpeed != target) SpeedChangePatcher.SetSpeedSilentlyNow(sm, target);

            if (speed != replayService.ChosenSpeed || target != replayService.TargetSpeed)
            {
                Plugin.Log($"Event: Changing target speed from {replayService.TargetSpeed} to {target}");
                replayService.SetChosenSpeed(speed);
            }
        }
    }

    /// <summary>
    /// The session's speed boost changed (SpeedBoost): from now on the game runs at the picked speed plus this. Any
    /// player may ask, from the chat box; everyone plays the answer, the asker included, like a speed change.
    /// </summary>
    [Serializable]
    public class SpeedBoostEvent : ReplayEvent
    {
        public override ColonyScope GetColonyScope() => ColonyScope.Global;
        // Like a speed change: it changes how fast ticks are worked through, not what is in them.
        public override bool ChangesGame() => false;

        public float boost;

        public override void Replay(IReplayContext context)
        {
            ReplayService replayService = context.GetSingleton<ReplayService>();
            float before = replayService.Boost;
            replayService.SetBoost(boost);
            Plugin.Log($"Event: Speed boost {SpeedBoost.Format(before)} -> {SpeedBoost.Format(replayService.Boost)}: " +
                       $"speed {replayService.ChosenSpeed} runs at {replayService.TargetSpeed}");
        }
    }

    /// <summary>Asks the session for a boost, from the chat box.</summary>
    public static class SpeedBoostRequest
    {
        /// <summary>Returns false if there is no session to ask, or it has failed; the box then shows the old value again.</summary>
        public static bool Send(float boost)
        {
            ReplayService replayService = ReplayEvent.GetReplayServiceIfReady();
            if (replayService == null || ReplayService.HasReplayFailure) return false;
            float wanted = SpeedBoost.Clamp(boost);
            if (wanted == replayService.Boost) return true;
            Plugin.Log($"Speed boost asked: {SpeedBoost.Format(wanted)} (was {SpeedBoost.Format(replayService.Boost)})");
            ReplayEvent.DoPrefix(() => new SpeedBoostEvent { boost = wanted });
            return true;
        }
    }

    [ManualMethodOverwrite]
    /*
        02/22/2026
        if (!_isLocked)
        {
            _nextSpeed = speed;
        }
     */
    [HarmonyPatch(typeof(SpeedManager), nameof(SpeedManager.ChangeSpeed), typeof(float))]
    public class SpeedChangePatcher
    {
        private static bool silently = false;

        /**
         * Sets the CurrentSpeed immediately without triggering and event.
         */
        public static void SetSpeedSilentlyNow(SpeedManager speedManager, float speed)
        {
            silently = true;
            speedManager.ChangeSpeed(speed);
            silently = false;

            // Have to call ChangeSpeed again to immediate update it.
            speedManager.ChangeSpeed();
        }

        [HarmonyPriority(Priority.First)]
        static bool Prefix(SpeedManager __instance, ref float speed)
        {
            if (!ReplayService.IsLoaded) return true;
            // No need to log speed changes to current speed
            if (__instance.CurrentSpeed == speed) return true;
            // Also don't log if we're silent
            if (silently) return true;

            var replayService = ReplayEvent.GetReplayServiceIfReady();
            if (replayService == null) return true;

            // In a co-op game only the connection panel's Pause button pauses (SessionPauseEvent). The game's pause
            // button and key, the tick once key, a menu or a dialog asking for speed 0: refused, and the player is told
            // where to pause. Silent changes (above) are the mod's own: waiting for the host, a slow guest, a pause.
            if (speed == 0)
            {
                CoopPauseNotice.ShowPauseFromPanel();
                return false;
            }

            // A request for the speed the players already picked changes nothing. With a boost the game runs at that
            // speed plus the boost (and while catching up, above it), so the check against the current speed above
            // does not catch it; without this the game would run at the bare speed for a frame and record a no-op.
            // Paused from the panel, the game's speed buttons and keys show paused, and the game's return from a pause
            // asks for the picked speed: say where the game resumes.
            if (speed == replayService.ChosenSpeed)
            {
                if (replayService.IsPausedByPlayer) CoopPauseNotice.ShowResumeFromPanel();
                return false;
            }

            replayService.RecordEvent(new SpeedSetEvent()
            {
                speed = speed
            });
            // Picked while paused: it is the speed the game resumes at, and the game stays paused until then.
            if (replayService.IsPausedByPlayer) CoopPauseNotice.ShowResumeFromPanel();

            if (EventIO.ShouldPlayPatchedEvents)
            {
                // If this will actually change the speed, make sure
                // we shouldn't pause instead.
                if (EventIO.ShouldPauseTicking || replayService.IsPausedByPlayer) speed = 0;
                return true;
            }
            return false;
        }
    }

    [ManualMethodOverwrite]
    /*
        04/19/2025
    	if (!_isLocked)
		{
			_speedBefore = CurrentSpeed;
			ChangeSpeed(value);
			_isLocked = true;
			_eventBus.Post(new SpeedLockChangedEvent(_isLocked));
		}
     */
    [HarmonyPatch(typeof(SpeedManager), nameof(SpeedManager.ChangeAndLockSpeed))]
    public class SpeedLockPatcher
    {
        static bool Prefix(SpeedManager __instance, float value)
        {
            // In a co-op game nobody freezes for menus, dialogs or panels: only the connection panel's Pause button
            // pauses (SessionPauseEvent). The host freezing here stopped the game for everyone.
            if (!EventIO.IsNull)
            {
                return false;
            }

            if (!__instance._isLocked)
            {
                __instance._speedBefore = __instance.CurrentSpeed;
                SpeedChangePatcher.SetSpeedSilentlyNow(__instance, value);
                __instance._isLocked = true;
                __instance._eventBus.Post(new SpeedLockChangedEvent(__instance._isLocked));
            }
            return false;
        }
    }

    [ManualMethodOverwrite]
    /*
     	04/19/2025
        if (_isLocked)
		{
			_isLocked = false;
			ChangeSpeed(_speedBefore);
			_eventBus.Post(new SpeedLockChangedEvent(_isLocked));
		}
     */
    [HarmonyPatch(typeof(SpeedManager), nameof(SpeedManager.UnlockSpeed))]
    public class SpeedUnlockPatcher
    {
        static bool Prefix(SpeedManager __instance)
        {
            // A co-op game takes no lock (see above), but one taken before the session began (the host's room opened
            // over a single-player game) is still let go: a lock left behind would hold the game at speed 0.
            if (__instance._isLocked)
            {
                __instance._isLocked = false;
                SpeedChangePatcher.SetSpeedSilentlyNow(__instance, __instance._speedBefore);
                __instance._eventBus.Post(new SpeedLockChangedEvent(__instance._isLocked));
            }
            return false;
        }
    }

    // OverlayPanelSpeedLocker pauses the game under some panels, through ChangeAndLockSpeed. In a co-op game it
    // pauses nobody (only the connection panel's Pause button does). Acting on a panel's stale view is safe: the host
    // refuses what no longer applies, and a guest's actions always arrive a moment later anyway.
    [HarmonyPatch(typeof(OverlayPanelSpeedLocker), nameof(OverlayPanelSpeedLocker.OnPanelShown))]
    public class OverlayPanelSpeedLockerShowPatcher
    {
        public static bool Prefix()
        {
            return EventIO.IsNull;
        }
    }

    [ManualMethodOverwrite]
    /*
        2026-09-22 (Timberborn 1.1.2.4, SpeedControlPanel.SetSpeed; in a co-op game a pause is refused in its place)
        if (timeSpeed == 0f)
        {
            float currentSpeed = _speedManager.CurrentSpeed;
            if (currentSpeed == 0f) { _speedManager.ChangeSpeed(_speedBeforePause); return; }
            _speedBeforePause = currentSpeed;
            _speedManager.ChangeSpeed(0f);
        }
        else _speedManager.ChangeSpeed(timeSpeed);
     */
    // The game's pause button and pause key toggle: pause, or return to the speed before the pause. In a co-op game
    // neither pauses (only the connection panel's Pause button does, SessionPauseEvent), so the player is told where
    // to pause, or, while it is paused, where to resume. The game's return from a pause is skipped too: it would ask
    // for whatever speed the game last ran at before its own pause, a stale pick (or the picked speed plus the boost,
    // which would add the boost again).
    [HarmonyPatch(typeof(SpeedControlPanel), "SetSpeed")]
    static class SpeedControlPanelSetSpeedPatcher
    {
        static bool Prefix(float timeSpeed)
        {
            if (timeSpeed != 0) return true;
            ReplayService replayService = ReplayEvent.GetReplayServiceIfReady();
            if (replayService == null) return true;
            // Before the game has started, the key starts it as in single player (the speed it asks for is recorded).
            if (replayService.ChosenSpeed == 0 && !replayService.IsPausedByPlayer) return true;
            if (replayService.IsPausedByPlayer) CoopPauseNotice.ShowResumeFromPanel();
            else CoopPauseNotice.ShowPauseFromPanel();
            return false;
        }
    }

    [ManualMethodOverwrite]
    /*
        2026-09-22 (Timberborn 1.1.2.4, TimeSpeedButtonGroup.GetCurrentButton)
        float currentSpeed = _currentSpeedGetter();
        return _buttons.SingleOrDefault((TimeSpeedButton button) => (float)button.TimeSpeed == currentSpeed);
     */
    // The game's keys for the next and previous speed look for the button of the current speed, and find none while
    // the game runs at a boosted (or catch-up) speed, so the keys did nothing. In a session the button is the picked
    // speed's. The buttons' highlight is left to the game: at a speed no button has, it writes the speed on the last
    // one ("x3.5"), which is how the game itself shows a custom speed.
    [HarmonyPatch(typeof(TimeSpeedButtonGroup), "GetCurrentButton")]
    static class TimeSpeedButtonGroupCurrentButtonPatcher
    {
        static bool Prefix(TimeSpeedButtonGroup __instance, ref TimeSpeedButton __result)
        {
            ReplayService replayService = ReplayEvent.GetReplayServiceIfReady();
            if (replayService == null) return true;
            float chosen = replayService.ChosenSpeed;
            __result = null;
            foreach (TimeSpeedButton button in __instance._buttons)
            {
                if (button.TimeSpeed == chosen) { __result = button; break; }
            }
            return false;
        }
    }
}
