using BeaverBuddies.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.CoreUI;
using Timberborn.DistributionSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// A trade message that asks the player for an answer (an offer made to their colony, or a request to end one of its
    /// exchanges) stays on screen until the player closes it or answers. A click on the message goes to their side of
    /// that Trading Post and selects it, where they answer, and the message stays until they do; its close button closes
    /// it; answering at that post closes it too. News of a colony handed over (ColonyLifecycle) stays the same way, with
    /// no post to go to: a click on it closes it. A message may carry a button that does what it asks (a steward's Run
    /// this colony), which closes it. It is drawn as the game's own quick notification is (the green board with the
    /// game's text, Common/QuickNotificationPanel), just below where that one appears, moved aside so it never covers
    /// the connection panel and its chat, and it chimes as it appears (NoticeSounds). Display only: posted by an action
    /// every computer plays, it is built on the next frame, on the computer of the player it is for, so nothing of it
    /// runs inside a tick.
    /// </summary>
    public class TradeNotices : RegisteredSingleton, IPostLoadableSingleton, IUpdatableSingleton
    {
        // The game's quick notification sits at 100 px and takes up to two lines; these go below it, as wide as it may be.
        private const float Top = 152, MaxWidth = 500;
        // Past this many, the oldest goes (news before a question), so the messages never cover the map.
        private const int MaxShown = 5;
        // Space kept between the messages and the connection panel.
        private const float PanelGap = 8;

        private readonly UILayout _uiLayout;
        private readonly VisualElementInitializer _visualElementInitializer;
        private readonly EntitySelectionService _entitySelectionService;
        private readonly NoticeSounds _noticeSounds;

        private sealed class Notice
        {
            public DistrictCrossing Half;
            public bool Asks;
            public VisualElement Root;
        }

        /// <summary>A message as posted: its text, its post (or none), and the button that does what it asks (or none).</summary>
        private readonly struct Posted
        {
            public readonly string Text, ActionText;
            public readonly DistrictCrossing Half;
            public readonly bool Warning;
            public readonly Action Action;

            public Posted(string text, DistrictCrossing half, bool warning, string actionText, Action action)
            {
                Text = text;
                Half = half;
                Warning = warning;
                ActionText = actionText;
                Action = action;
            }
        }

        private VisualElement stack;
        // Posted by this frame's actions, shown on the next frame.
        private readonly List<Posted> posted = new List<Posted>();
        private readonly List<Notice> shown = new List<Notice>();
        // A chime asked for by news shown the game's own way (an offer accepted), played on the next frame.
        private bool chimePending;
        private float shownLeft, shownRight;

        public static TradeNotices Instance => SingletonManager.GetSingleton<TradeNotices>();

        public TradeNotices(UILayout uiLayout, VisualElementInitializer visualElementInitializer,
            EntitySelectionService entitySelectionService, NoticeSounds noticeSounds)
        {
            _uiLayout = uiLayout;
            _visualElementInitializer = visualElementInitializer;
            _entitySelectionService = entitySelectionService;
            _noticeSounds = noticeSounds;
        }

        public void PostLoad()
        {
            try
            {
                // A strip across the screen that only places the messages: clicks beside them go to the game.
                stack = new VisualElement { pickingMode = PickingMode.Ignore };
                var s = stack.style;
                s.position = Position.Absolute;
                s.left = 0;
                s.right = 0;
                s.top = Top;
                s.alignItems = Align.Center;
                _uiLayout.AddAbsoluteItem(stack);
            }
            catch (Exception error)
            {
                Plugin.LogError("[Colony] Could not add the trade messages: " + error);
                stack = null;
            }
        }

        /// <summary>
        /// Shows <paramref name="text"/> until the player closes it or answers at <paramref name="half"/>; a click on it
        /// goes to <paramref name="half"/>. A newer message for the same post takes the older one's place. A
        /// <paramref name="warning"/> is on the game's red board, as its warning notices are. False if it cannot be shown
        /// this way (the caller then shows it as an ordinary notice).
        /// </summary>
        public bool Post(string text, DistrictCrossing half, bool warning)
        {
            if (stack == null) return false;
            posted.Add(new Posted(text, half, warning, null, null));
            return true;
        }

        /// <summary>
        /// Shows <paramref name="text"/> until the player clicks it away, with no Trading Post to go to (a click on it
        /// only closes it). False if it cannot be shown this way, as <see cref="Post"/>.
        /// </summary>
        public bool PostNews(string text, bool warning) => Post(text, null, warning);

        /// <summary>
        /// News with a button that does what it asks (<paramref name="actionText"/>, then <paramref name="action"/>, which
        /// runs outside the tick as any button's click does); the button or the close button closes it. False if it
        /// cannot be shown this way, as <see cref="Post"/>.
        /// </summary>
        public bool PostWithAction(string text, bool warning, string actionText, Action action)
        {
            if (stack == null) return false;
            posted.Add(new Posted(text, null, warning, actionText, action));
            return true;
        }

        /// <summary>Chimes on the next frame, as a message that stays does: for news shown the game's own way.</summary>
        public void ChimeSoon() => chimePending = true;

        /// <summary>The player answered at <paramref name="half"/>: its message has done its job.</summary>
        public void Answered(DistrictCrossing half)
        {
            if (half == null) return;
            posted.RemoveAll(p => p.Half == half);
            foreach (Notice notice in shown.Where(n => n.Half == half).ToList()) Close(notice);
        }

        public void UpdateSingleton()
        {
            if (shown.Count > 0) KeepClearOfPanel();
            if (posted.Count == 0 && !chimePending) return;
            try
            {
                foreach (Posted message in posted) Show(message);
                _noticeSounds.Play(NoticeSounds.TradeSound);
                if (posted.Count > 0) KeepClearOfPanel();
            }
            catch (Exception error)
            {
                Plugin.LogWarning("[Colony] Could not show a trade message: " + error.Message);
            }
            finally
            {
                posted.Clear();
                chimePending = false;
            }
        }

        /// <summary>
        /// The game's notification board, green or red (square-large--green or --red, game-text-normal: the quick
        /// notification's classes), with the button that does what it asks (if any) and the game's round close button
        /// (close-button) at its end.
        /// </summary>
        private void Show(Posted message)
        {
            DistrictCrossing half = message.Half;
            // A newer message for the same post replaces the older; news with no post (null) never replaces another.
            if (half != null) foreach (Notice old in shown.Where(n => n.Half == half).ToList()) Close(old);
            // Full: news goes first, a question for a post only when there is nothing else to drop.
            while (shown.Count >= MaxShown) Close(shown.FirstOrDefault(n => !n.Asks) ?? shown[0]);

            var notice = new Notice { Half = half, Asks = half != null || message.Action != null };
            var board = new NineSliceVisualElement();
            board.AddToClassList(message.Warning ? "square-large--red" : "square-large--green");
            var s = board.style;
            s.flexDirection = FlexDirection.Row;
            s.alignItems = Align.Center;
            s.maxWidth = MaxWidth;
            s.marginBottom = 4;
            s.paddingLeft = 8; s.paddingRight = 4; s.paddingTop = 5; s.paddingBottom = 5;

            var label = new Label(NativeElements.SentencePerLine(NativeElements.Plain(message.Text)));
            label.AddToClassList("game-text-normal");
            // A long message wraps inside the board's width instead of pushing the close button out of it.
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 1;
            label.style.minWidth = 0;
            label.style.marginRight = 6;
            board.Add(label);

            Button act = null;
            if (message.Action != null)
            {
                Action action = message.Action;
                // The game's wooden button, at the size of the trading window's small ones.
                act = NativeElements.WoodenButton(message.ActionText, () =>
                {
                    Close(notice);
                    try { action(); }
                    catch (Exception error) { Plugin.LogWarning("[Colony] A message's button could not act: " + error.Message); }
                });
                var a = act.style;
                a.fontSize = 12;
                a.minHeight = 24; a.height = 24;
                a.paddingTop = 0; a.paddingBottom = 0; a.paddingLeft = 9; a.paddingRight = 9;
                a.flexShrink = 0;
                a.marginRight = 6;
                board.Add(act);
            }

            var close = new Button(() => Close(notice));
            close.AddToClassList("close-button");
            var c = close.style;
            // The game's close button sits on a box's corner; here it ends the line, at the text's size.
            c.position = Position.Relative;
            c.right = 0;
            c.top = 0;
            c.width = 24;
            c.height = 24;
            c.flexShrink = 0;
            c.marginLeft = 0; c.marginRight = 0; c.marginTop = 0; c.marginBottom = 0;
            board.Add(close);

            // A click anywhere else on the message goes to the post.
            board.RegisterCallback<ClickEvent>(click =>
            {
                if (click.target is VisualElement target && (target == close || close.Contains(target) || (act != null && (target == act || act.Contains(target)))))
                    return;
                GoTo(notice);
            });

            notice.Root = board;
            _visualElementInitializer.InitializeVisualElement(board);
            stack.Add(board);
            shown.Add(notice);
            // In front of the game's panels and the mod's windows, which may be opened after it.
            stack.BringToFront();
        }

        /// <summary>
        /// A message about a post selects the player's side of it and stays until they answer there or close it; news
        /// with no post closes.
        /// </summary>
        private void GoTo(Notice notice)
        {
            if (!notice.Half)
            {
                if (!notice.Asks) Close(notice);
                return;
            }
            try { _entitySelectionService.SelectAndFocusOn(notice.Half); }
            catch (Exception error) { Plugin.LogWarning("[Colony] Could not go to the trading post: " + error.Message); }
        }

        private void Close(Notice notice)
        {
            shown.Remove(notice);
            notice.Root?.RemoveFromHierarchy();
        }

        /// <summary>
        /// Moves the column of messages aside when it would cover the connection panel (and its chat): the column keeps
        /// its place across the screen, between the panel and the far edge. Display only, measured each frame while a
        /// message is shown.
        /// </summary>
        private void KeepClearOfPanel()
        {
            if (stack == null || stack.panel == null) return;
            float left = 0, right = 0;
            try
            {
                VisualElement panel = BeaverBuddies.Panel.ConnectionPanelService.PanelRoot;
                Rect strip = stack.worldBound, box = panel != null && panel.panel == stack.panel && panel.resolvedStyle.display != DisplayStyle.None
                    ? panel.worldBound : Rect.zero;
                bool usable = box.width > 0 && box.height > 0 && !float.IsNaN(box.width) && !float.IsNaN(strip.width) && strip.width > 0;
                // Only where the messages reach down to the panel's height.
                float messagesBottom = strip.yMin + stack.layout.height;
                if (usable && box.yMin < messagesBottom && box.yMax > strip.yMin)
                {
                    if (box.center.x < strip.center.x) left = Mathf.Max(0, box.xMax - strip.xMin + PanelGap);
                    else right = Mathf.Max(0, strip.xMax - box.xMin + PanelGap);
                }
            }
            catch (Exception)
            {
                left = right = 0;
            }
            // Changed only when it moves, so the layout isn't redone every frame.
            if (Mathf.Abs(left - shownLeft) < 1 && Mathf.Abs(right - shownRight) < 1) return;
            shownLeft = left;
            shownRight = right;
            stack.style.paddingLeft = left;
            stack.style.paddingRight = right;
        }
    }
}
