using BeaverBuddies.Events;
using BeaverBuddies.IO;
using BeaverBuddies.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Timberborn.AssetSystem;
using Timberborn.BaseComponentSystem;
using Timberborn.CoreUI;
using Timberborn.EntitySystem;
using Timberborn.InputSystem;
using Timberborn.MechanicalSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TooltipSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// The Power window, drawn as the Trading Posts and colonies window is (the game's framed box with a title badge and
    /// a close button, dragged by its title or frame): where power goes between colonies (each chain on one line, with
    /// what crosses each link), this player's colony's power networks (made, used, spare, batteries, and what comes in or
    /// goes out, each with a button to go there), its Power Export Facilities (with their check boxes), and the other
    /// colonies' power. It opens and closes with H (a key the player can change), the square Power button at the top
    /// right, or "Power" on a facility, and closes with its close button or Esc. While it is open, every colony's power
    /// networks show on the map (as Ctrl+P shows them). It does not pause the game. Display and check boxes only: each
    /// change sends an ordinary action.
    /// </summary>
    public class PowerOverviewPanel : IPostLoadableSingleton, IUpdatableSingleton, IInputProcessor
    {
        public const string KeyBindingId = "BeaverBuddies.KeyBind.PowerOverview";
        private const string ToggleIconPath = "UI/Images/BeaverBuddies/square-toggle-power";
        private const float Top = 110, BottomMargin = 40, Width = 470;

        private readonly UILayout _uiLayout;
        private readonly InputService _inputService;
        private readonly VisualElementInitializer _visualElementInitializer;
        private readonly VisualElementLoader _visualElementLoader;
        private readonly IAssetLoader _assetLoader;
        private readonly ITooltipRegistrar _tooltipRegistrar;
        private readonly EntitySelectionService _entitySelectionService;
        private readonly MechanicalGraphRegistry _mechanicalGraphRegistry;

        private sealed class FacilityRow
        {
            public PowerExportHalf Half;
            public Label Title, Detail;
            public Toggle Send, ChargeFirst, UseBatteries;
        }

        private sealed class NetworkRow
        {
            public BaseComponent Target;
            public Label Title, Detail;
        }

        /// <summary>A network's figures, in hp (batteries in hph), and where to go to see it.</summary>
        private sealed class NetworkFigures
        {
            public int Colony = -1, Made, Used, In, Out, Charge, Capacity;
            public BaseComponent Target;
            public string Key;
        }

        private VisualElement window, chainList, networkList, facilityList, colonyList;
        private NineSliceVisualElement box;
        private Label chainEmpty, networkEmpty, facilityEmpty, colonyEmpty;
        private VisualElement topButton;
        private Toggle topToggle;
        private bool open;
        private float nextRefresh;
        private string networksShape, facilitiesShape, coloniesShape, chainsShape;
        private readonly List<NetworkRow> networkRows = new List<NetworkRow>();
        private readonly List<FacilityRow> facilityRows = new List<FacilityRow>();
        private readonly Dictionary<int, (Label title, Label detail)> colonyRows = new Dictionary<int, (Label, Label)>();
        private readonly List<Label> chainRows = new List<Label>();

        public static PowerOverviewPanel Instance { get; private set; }

        /// <summary>The window is open (the power networks show on the map while it is).</summary>
        public bool IsOpen => open;

        public PowerOverviewPanel(UILayout uiLayout, InputService inputService, VisualElementInitializer visualElementInitializer,
            VisualElementLoader visualElementLoader, IAssetLoader assetLoader, ITooltipRegistrar tooltipRegistrar,
            EntitySelectionService entitySelectionService, MechanicalGraphRegistry mechanicalGraphRegistry)
        {
            _uiLayout = uiLayout;
            _inputService = inputService;
            _visualElementInitializer = visualElementInitializer;
            _visualElementLoader = visualElementLoader;
            _assetLoader = assetLoader;
            _tooltipRegistrar = tooltipRegistrar;
            _entitySelectionService = entitySelectionService;
            _mechanicalGraphRegistry = mechanicalGraphRegistry;
        }

        public void PostLoad()
        {
            Instance = this;
            _inputService.AddInputProcessor(this);
            try
            {
                Build();
            }
            catch (Exception error)
            {
                Plugin.LogError("[Colony] Could not build the Power window: " + error);
                window = null;
            }
            try
            {
                BuildTopButton();
            }
            catch (Exception error)
            {
                Plugin.LogError("[Colony] Could not add the Power button: " + error);
                topButton = null;
            }
        }

        private static bool Available => ColonyModeService.IsSeparateColonies && !EventIO.IsNull && ColonySession.LocalSlot >= 0;

        public bool ProcessInput()
        {
            if (_inputService.IsKeyDown(KeyBindingId) && Available)
            {
                Toggle();
                return true;
            }
            if (open && _inputService.UICancel)
            {
                Close();
                return true;
            }
            return false;
        }

        public bool Toggle()
        {
            if (open)
            {
                Close();
                return true;
            }
            return Open();
        }

        public bool Open()
        {
            if (window == null || !Available) return false;
            open = true;
            window.style.display = DisplayStyle.Flex;
            topToggle?.SetValueWithoutNotify(true);
            nextRefresh = 0;
            Refresh();
            return true;
        }

        public void Close()
        {
            open = false;
            if (window != null) window.style.display = DisplayStyle.None;
            topToggle?.SetValueWithoutNotify(false);
        }

        public void UpdateSingleton()
        {
            if (topButton != null) NativeElements.Show(topButton, Available);
            if (!open || window == null) return;
            if (!Available)
            {
                Close();
                return;
            }
            if (Time.unscaledTime < nextRefresh) return;
            Refresh();
        }

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + 1f;
            try
            {
                FitToScreen();
                RefreshLists();
            }
            catch (Exception error)
            {
                Plugin.LogWarning("[Colony] Power window: " + error);
                Close();
            }
        }

        // ---- building ----

        private void Build()
        {
            window = new VisualElement { pickingMode = PickingMode.Ignore };
            var s = window.style;
            s.position = Position.Absolute;
            s.left = 0;
            s.right = 0;
            s.top = Top;
            s.alignItems = Align.Center;

            box = new NineSliceVisualElement();
            box.AddToClassList("sliced-border");
            box.AddToClassList("sliced-border--nontransparent");
            box.AddToClassList("box__content-container");
            box.style.width = Width;
            box.style.alignSelf = Align.Center;
            box.style.flexGrow = 0;
            window.Add(box);

            var header = new NineSliceVisualElement();
            header.AddToClassList("capsule-header");
            header.AddToClassList("capsule-header--lower");
            header.AddToClassList("content-centered");
            var title = new Label(T("BeaverBuddies.Colony.Power.Window.Title"));
            title.AddToClassList("capsule-header__text");
            header.Add(title);
            box.Add(header);
            MakeDraggable(header, title);

            var close = new Button(Close);
            close.AddToClassList("close-button");
            _tooltipRegistrar.RegisterWithKeyBinding(close, T("BeaverBuddies.Colony.Power.Window.CloseTooltip"), KeyBindingId);
            box.Add(close);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("game-scroll-view");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;

            scroll.Add(TradeOverviewPanel.Heading(T("BeaverBuddies.Colony.Power.Window.Chains")));
            chainEmpty = TradeOverviewPanel.MutedLine(T("BeaverBuddies.Colony.Power.Window.NoChains"));
            chainEmpty.style.marginTop = 4;
            scroll.Add(chainEmpty);
            chainList = new VisualElement();
            scroll.Add(chainList);

            Label networksTitle = TradeOverviewPanel.Heading(T("BeaverBuddies.Colony.Power.Window.Networks"));
            networksTitle.style.marginTop = 16;
            scroll.Add(networksTitle);
            networkEmpty = TradeOverviewPanel.MutedLine(T("BeaverBuddies.Colony.Power.Window.NoNetworks"));
            networkEmpty.style.marginTop = 4;
            scroll.Add(networkEmpty);
            networkList = new VisualElement();
            scroll.Add(networkList);

            Label facilitiesTitle = TradeOverviewPanel.Heading(T("BeaverBuddies.Colony.Power.Window.Facilities"));
            facilitiesTitle.style.marginTop = 16;
            scroll.Add(facilitiesTitle);
            facilityEmpty = TradeOverviewPanel.MutedLine(T("BeaverBuddies.Colony.Power.Window.NoFacilities"));
            facilityEmpty.style.marginTop = 4;
            scroll.Add(facilityEmpty);
            facilityList = new VisualElement();
            scroll.Add(facilityList);

            Label coloniesTitle = TradeOverviewPanel.Heading(T("BeaverBuddies.Colony.Power.Window.Colonies"));
            coloniesTitle.style.marginTop = 16;
            scroll.Add(coloniesTitle);
            colonyEmpty = TradeOverviewPanel.MutedLine(T("BeaverBuddies.Colony.Power.Window.NoColonies"));
            colonyEmpty.style.marginTop = 4;
            scroll.Add(colonyEmpty);
            colonyList = new VisualElement();
            scroll.Add(colonyList);
            box.Add(scroll);

            Label hint = NativeElements.GameText(T("BeaverBuddies.Colony.Power.Window.Hint"), NativeElements.TextSmall);
            hint.style.marginTop = 12;
            box.Add(hint);

            _visualElementInitializer.InitializeVisualElement(window);
            window.style.display = DisplayStyle.None;
            _uiLayout.AddAbsoluteItem(window);
        }

        /// <summary>The Power button among the game's own at the top right, beside the Trade button.</summary>
        private void BuildTopButton()
        {
            topButton = _visualElementLoader.LoadVisualElement("Common/SquareToggle");
            topToggle = topButton.Q<Toggle>("Toggle");
            Sprite icon = _assetLoader.LoadSafe<Sprite>(ToggleIconPath);
            VisualElement checkmark = topToggle?.Q(className: "unity-toggle__checkmark");
            if (icon != null && checkmark != null) checkmark.style.backgroundImage = new StyleBackground(icon);
            else if (topToggle != null) topToggle.text = T("BeaverBuddies.Colony.Power.Window.Button");
            topToggle?.RegisterValueChangedCallback(change =>
            {
                if (change.newValue == open) return;
                if (change.newValue && !Open()) topToggle.SetValueWithoutNotify(false);
                else if (!change.newValue) Close();
            });
            _tooltipRegistrar.RegisterWithKeyBinding(topButton, T("BeaverBuddies.Colony.Power.Window.ButtonTooltip"), KeyBindingId);
            topButton.style.display = DisplayStyle.None;
            _uiLayout.AddTopRightButton(topButton, 51);
        }

        private void FitToScreen()
        {
            float screen = window.panel?.visualTree.worldBound.height ?? 0;
            if (screen > 0 && !float.IsNaN(screen)) box.style.maxHeight = Mathf.Max(220, screen - Top - dragOffset.y - BottomMargin);
        }

        // ---- moving the box (as the Trading Posts window's) ----

        private Vector2 dragOffset, dragOffsetAtStart;
        private Vector3 dragStart;
        private bool dragging;

        private void MakeDraggable(params VisualElement[] grips)
        {
            var handles = new HashSet<VisualElement>(grips) { box };
            box.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !(e.target is VisualElement target) || !handles.Contains(target)) return;
                dragging = true;
                dragStart = e.position;
                dragOffsetAtStart = dragOffset;
                box.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            box.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || !box.HasPointerCapture(e.pointerId)) return;
                Vector3 moved = e.position - dragStart;
                dragOffset = dragOffsetAtStart + new Vector2(moved.x, moved.y);
                PlaceBox();
            });
            box.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                if (box.HasPointerCapture(e.pointerId)) box.ReleasePointer(e.pointerId);
            });
            box.RegisterCallback<PointerCaptureOutEvent>(e => dragging = false);
        }

        private void PlaceBox()
        {
            Rect screen = window.panel?.visualTree.worldBound ?? Rect.zero;
            if (screen.width > 0 && screen.height > 0 && !float.IsNaN(screen.width) && !float.IsNaN(screen.height))
            {
                float side = Mathf.Max(0, screen.width / 2 - 80);
                dragOffset.x = Mathf.Clamp(dragOffset.x, -side, side);
                dragOffset.y = Mathf.Clamp(dragOffset.y, 10 - Top, Mathf.Max(10 - Top, screen.height - Top - 80));
            }
            box.style.translate = new Translate(dragOffset.x, dragOffset.y, 0);
            FitToScreen();
        }

        // ---- the lists ----

        private void RefreshLists()
        {
            int me = ColonySession.LocalSlot;
            List<NetworkFigures> networks = Networks();
            RefreshChains();
            RefreshNetworks(networks.Where(n => n.Colony == me).ToList());
            RefreshFacilities(me);
            RefreshColonies(me, networks);
        }

        /// <summary>
        /// Every network with something in it worth listing (a generator, a battery or a facility half), with its colony
        /// and figures, biggest first. Display only: read from the game's networks as they are.
        /// </summary>
        private List<NetworkFigures> Networks()
        {
            var result = new List<NetworkFigures>();
            foreach (MechanicalGraph graph in _mechanicalGraphRegistry.MechanicalGraphs)
            {
                var figures = new NetworkFigures { Charge = graph.BatteryCharge, Capacity = graph.BatteryCapacity };
                bool worth = graph.BatteryCapacity > 0;
                int bestOutput = -1;
                string bestId = null;
                foreach (MechanicalNode node in graph.Nodes)
                {
                    PowerExportHalf half = node.GetComponent<PowerExportHalf>();
                    if (half != null)
                    {
                        figures.In += half.PowerGiven;
                        figures.Out += half.PowerDrawn;
                        worth = true;
                        continue;
                    }
                    if (figures.Colony < 0) figures.Colony = PowerExports.PowerOwnerOf(node) ?? -1;
                    if (node.IsGenerator) worth = true;
                    // Where "Go there" leads: the strongest generator, else a battery, else any part (ties by id).
                    int output = node.IsGenerator ? node.Actuals.PowerOutput + 1 : node.IsBattery ? 0 : -1;
                    string id = ReplayEvent.GetEntityID(node);
                    if (output > bestOutput || (output == bestOutput && string.CompareOrdinal(id, bestId) < 0))
                    {
                        bestOutput = output;
                        bestId = id;
                        figures.Target = node;
                    }
                }
                if (!worth || figures.Colony < 0) continue;
                figures.Made = graph.PowerSupply - figures.In;
                figures.Used = graph.PowerDemand - figures.Out;
                figures.Key = bestId ?? "";
                result.Add(figures);
            }
            return result.OrderByDescending(n => n.Made).ThenBy(n => n.Key, StringComparer.Ordinal).ToList();
        }

        private void RefreshChains()
        {
            IReadOnlyDictionary<(int from, int to), int> links = PowerExportService.Instance?.Links
                ?? new Dictionary<(int from, int to), int>();
            var lines = new List<string>();
            var heads = links.Keys.Select(l => l.from).Where(from => !links.Keys.Any(l => l.to == from)).Distinct().OrderBy(s => s);
            foreach (int head in heads)
            {
                string line = PowerExportText.ColoredName(head);
                int at = head;
                for (int step = 0; step <= links.Count; step++)
                {
                    var next = links.Keys.Where(l => l.from == at).OrderBy(l => l.to).FirstOrDefault();
                    if (!links.ContainsKey(next) || next.from != at) break;
                    line += string.Format(T("BeaverBuddies.Colony.Power.Window.ChainLink"), links[next], PowerExportText.ColoredName(next.to));
                    at = next.to;
                }
                lines.Add(line);
            }
            NativeElements.Show(chainEmpty, lines.Count == 0);
            string shape = lines.Count.ToString();
            if (shape != chainsShape)
            {
                chainsShape = shape;
                chainList.Clear();
                chainRows.Clear();
                foreach (string _ in lines)
                {
                    Label label = NativeElements.GameText("", NativeElements.TextNormal);
                    label.enableRichText = true;
                    label.style.marginTop = 4;
                    chainList.Add(label);
                    chainRows.Add(label);
                }
            }
            for (int i = 0; i < lines.Count && i < chainRows.Count; i++) NativeElements.SetText(chainRows[i], lines[i]);
        }

        private void RefreshNetworks(List<NetworkFigures> mine)
        {
            NativeElements.Show(networkEmpty, mine.Count == 0);
            string shape = string.Join(",", mine.Select(n => n.Key));
            if (shape != networksShape)
            {
                networksShape = shape;
                networkList.Clear();
                networkRows.Clear();
                foreach (NetworkFigures _ in mine)
                {
                    var row = new NetworkRow();
                    NineSliceVisualElement board = TradeOverviewPanel.Board();
                    VisualElement line = NativeElements.Row();
                    line.Add(TradeOverviewPanel.TitleAndDetail(out row.Title, out row.Detail));
                    NetworkRow target = row;
                    Button go = TradeOverviewPanel.SmallButton(T("BeaverBuddies.Colony.Overview.GoTo"), () => GoTo(target.Target));
                    go.style.marginLeft = 10;
                    line.Add(go);
                    board.Add(line);
                    networkList.Add(board);
                    networkRows.Add(row);
                }
                _visualElementInitializer.InitializeVisualElement(networkList);
            }
            for (int i = 0; i < mine.Count && i < networkRows.Count; i++)
            {
                NetworkFigures n = mine[i];
                networkRows[i].Target = n.Target;
                NativeElements.SetText(networkRows[i].Title, string.Format(T("BeaverBuddies.Colony.Power.Window.NetworkTitle"), i + 1, Hp(n.Made - n.Used)));
                NativeElements.SetText(networkRows[i].Detail, NetworkDetail(n));
            }
        }

        /// <summary>"Makes 300 hp · uses 250 hp · batteries 120 / 400 hph", then what comes in or goes out.</summary>
        private static string NetworkDetail(NetworkFigures n)
        {
            string text = string.Format(T("BeaverBuddies.Colony.Power.Window.NetworkDetail"), Hp(n.Made), Hp(n.Used));
            if (n.Capacity > 0) text += " · " + string.Format(T("BeaverBuddies.Colony.Power.Window.Batteries"), Count(n.Charge), Count(n.Capacity));
            if (n.In > 0) text += "\n" + string.Format(T("BeaverBuddies.Colony.Power.Window.In"), Hp(n.In));
            if (n.Out > 0) text += "\n" + string.Format(T("BeaverBuddies.Colony.Power.Window.Out"), Hp(n.Out));
            return text;
        }

        private void RefreshFacilities(int me)
        {
            List<PowerExportHalf> mine = (PowerExportService.Instance?.AllHalves() ?? new List<PowerExportHalf>())
                .Where(h => PowerExports.ColonyOf(h) == me).ToList();
            NativeElements.Show(facilityEmpty, mine.Count == 0);
            string shape = string.Join(",", mine.Select(ReplayEvent.GetEntityID));
            if (shape != facilitiesShape)
            {
                facilitiesShape = shape;
                facilityList.Clear();
                facilityRows.Clear();
                foreach (PowerExportHalf half in mine)
                {
                    var row = new FacilityRow { Half = half };
                    NineSliceVisualElement board = TradeOverviewPanel.Board();
                    VisualElement line = NativeElements.Row();
                    line.Add(TradeOverviewPanel.TitleAndDetail(out row.Title, out row.Detail));
                    Button go = TradeOverviewPanel.SmallButton(T("BeaverBuddies.Colony.Overview.GoTo"), () => GoTo(row.Half));
                    go.style.marginLeft = 10;
                    line.Add(go);
                    board.Add(line);
                    row.Send = Box(row, PowerExportSetting.Sending);
                    row.ChargeFirst = Box(row, PowerExportSetting.ChargeFirst);
                    row.UseBatteries = Box(row, PowerExportSetting.UseBatteries);
                    board.Add(row.Send);
                    board.Add(row.ChargeFirst);
                    board.Add(row.UseBatteries);
                    facilityList.Add(board);
                    facilityRows.Add(row);
                }
                _visualElementInitializer.InitializeVisualElement(facilityList);
            }
            foreach (FacilityRow row in facilityRows)
            {
                PowerExportHalf half = row.Half;
                if (!half) continue;
                PowerExportHalf partner = half.Partner;
                int them = PowerExports.ColonyOf(partner);
                bool linked = them >= 0 && them != me;
                NativeElements.SetText(row.Title, linked
                    ? string.Format(T("BeaverBuddies.Colony.Power.LinkedWith"), PowerExportText.ColoredName(them))
                    : T("BeaverBuddies.Colony.Power.NotLinkedTitle"));
                PowerExportHalf sender = half.Sending ? half : partner != null && partner.Sending ? partner : null;
                string detail;
                if (!linked) detail = T(them == me ? "BeaverBuddies.Colony.Power.Status.SameColony" : "BeaverBuddies.Colony.Power.NotLinked");
                else if (sender == null) detail = T("BeaverBuddies.Colony.Power.Status.NotSending");
                else
                {
                    int from = PowerExports.ColonyOf(sender), to = PowerExports.ColonyOf(sender.Partner);
                    detail = sender.Status == PowerExportStatus.Sending
                        ? string.Format(T("BeaverBuddies.Colony.Power.Status.Flowing"), NativeNames.Plain(from), NativeNames.Plain(to), sender.Flow)
                        : PowerExportText.Status(sender, from, to);
                }
                NativeElements.SetText(row.Detail, detail);
                foreach (Toggle toggle in new[] { row.Send, row.ChargeFirst, row.UseBatteries }) NativeElements.Show(toggle, linked);
                if (!linked) continue;
                row.Send.text = string.Format(T("BeaverBuddies.Colony.Power.Send"), NativeNames.Plain(them));
                row.UseBatteries.text = string.Format(T("BeaverBuddies.Colony.Power.UseBatteries"), NativeNames.Plain(them));
                row.Send.SetValueWithoutNotify(half.Sending);
                row.ChargeFirst.SetValueWithoutNotify(half.ChargeFirst);
                row.UseBatteries.SetValueWithoutNotify(half.UseBatteries);
                row.Send.SetEnabled(PowerExportActions.SendBlocked(half) == null);
                row.ChargeFirst.SetEnabled(half.Sending && !half.UseBatteries);
                row.UseBatteries.SetEnabled(half.Sending);
            }
        }

        /// <summary>One of a facility row's check boxes (the game's small check box), sending its change as the panel's do.</summary>
        private Toggle Box(FacilityRow row, PowerExportSetting setting)
        {
            Toggle toggle = NativeElements.CheckBox(setting == PowerExportSetting.ChargeFirst ? T("BeaverBuddies.Colony.Power.ChargeFirst") : "", small: true);
            toggle.style.marginTop = 4;
            toggle.RegisterValueChangedCallback(change =>
            {
                if (!row.Half) return;
                toggle.SetValueWithoutNotify(PowerExportActions.Current(row.Half, setting));
                PowerExportActions.Change(row.Half, setting, change.newValue);
                nextRefresh = 0;
            });
            _tooltipRegistrar.Register(toggle, () =>
            {
                if (!row.Half) return "";
                string partner = NativeNames.Plain(PowerExports.ColonyOf(row.Half.Partner));
                return setting switch
                {
                    PowerExportSetting.Sending => PowerExportActions.SendBlocked(row.Half) ?? string.Format(T("BeaverBuddies.Colony.Power.SendTooltip"), partner),
                    PowerExportSetting.ChargeFirst => row.Half.UseBatteries
                        ? string.Format(T("BeaverBuddies.Colony.Power.ChargeFirstOffTooltip"), partner)
                        : T("BeaverBuddies.Colony.Power.ChargeFirstTooltip"),
                    _ => string.Format(T("BeaverBuddies.Colony.Power.UseBatteriesTooltip"), partner),
                };
            });
            return toggle;
        }

        private void RefreshColonies(int me, List<NetworkFigures> networks)
        {
            ColonyLifecycle lifecycle = ColonyLifecycle.Instance;
            List<int> slots = Enumerable.Range(0, ColonySlotTable.MaxSlots)
                .Where(slot => slot != me && (lifecycle?.OwnsDistrict(slot) ?? networks.Any(n => n.Colony == slot))).ToList();
            NativeElements.Show(colonyEmpty, slots.Count == 0);
            string shape = string.Join(",", slots);
            if (shape != coloniesShape)
            {
                coloniesShape = shape;
                colonyList.Clear();
                colonyRows.Clear();
                foreach (int slot in slots)
                {
                    NineSliceVisualElement board = TradeOverviewPanel.Board();
                    board.Add(TradeOverviewPanel.TitleAndDetail(out Label title, out Label detail));
                    colonyList.Add(board);
                    colonyRows[slot] = (title, detail);
                }
                _visualElementInitializer.InitializeVisualElement(colonyList);
            }
            foreach (int slot in slots)
            {
                if (!colonyRows.TryGetValue(slot, out var labels)) continue;
                var theirs = networks.Where(n => n.Colony == slot).ToList();
                int made = theirs.Sum(n => n.Made), used = theirs.Sum(n => n.Used);
                int charge = theirs.Sum(n => n.Charge), capacity = theirs.Sum(n => n.Capacity);
                NativeElements.SetText(labels.title, string.Format(T("BeaverBuddies.Colony.Power.Window.ColonyTitle"),
                    PowerExportText.ColoredName(slot), Hp(made - used)));
                string detail = theirs.Count == 0 ? T("BeaverBuddies.Colony.Power.Window.NoPower")
                    : string.Format(T("BeaverBuddies.Colony.Power.Window.NetworkDetail"), Hp(made), Hp(used));
                if (capacity > 0) detail += " · " + string.Format(T("BeaverBuddies.Colony.Power.Window.Batteries"), Count(charge), Count(capacity));
                NativeElements.SetText(labels.detail, detail);
            }
        }

        private void GoTo(BaseComponent target)
        {
            if (!target) return;
            try { _entitySelectionService.SelectAndFocusOn(target); }
            catch (Exception error) { Plugin.LogWarning("[Colony] Could not go to the power building: " + error.Message); }
        }

        /// <summary>"140 hp" (spare may be negative: "-20 hp", short of power).</summary>
        private static string Hp(int value) => string.Format(T("BeaverBuddies.Colony.Power.Hp"), Count(value));

        private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

        private static string T(string key) => RegisteredLocalizationService.T(key);
    }
}
