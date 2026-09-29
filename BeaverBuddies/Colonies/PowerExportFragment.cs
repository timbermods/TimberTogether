using BeaverBuddies.Editor;
using BeaverBuddies.Events;
using BeaverBuddies.Panel;
using BeaverBuddies.Util;
using System;
using Timberborn.BaseComponentSystem;
using Timberborn.CoreUI;
using Timberborn.EntityPanelSystem;
using Timberborn.SelectionSystem;
using Timberborn.TooltipSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// The Power Export Facility's panel, built from the game's own panel pieces as the Trading Post's is, below the
    /// game's own (the worker, and the half's power network). On the player's own half: whom it links to, the power
    /// crossing now or why none does, the half's three settings (send power, charge my batteries first, use my batteries),
    /// and what the other colony's half is set to. On the other colony's half it says whose half it is and leads to the
    /// player's own. Display and check boxes only; each change is an ordinary action that every computer plays.
    /// </summary>
    public class PowerExportFragment : IEntityPanelFragment
    {
        private const float RefreshInterval = 0.5f;

        private readonly VisualElementInitializer _visualElementInitializer;
        private readonly ITooltipRegistrar _tooltipRegistrar;
        private readonly EntitySelectionService _entitySelectionService;

        private NineSliceVisualElement root;
        private Label headerText, flowText, statusText, partnerText;
        private Button windowButton, myHalfButton;
        private VisualElement mySettings;
        private Toggle sendToggle, chargeToggle, batteryToggle;
        private PowerExportHalf half;
        private float nextRefresh;
        // Why sending can't be turned on now (the tooltip of the greyed box), or null.
        private string sendBlocked;
        private int partnerSlot = -1;

        public PowerExportFragment(VisualElementInitializer visualElementInitializer, ITooltipRegistrar tooltipRegistrar,
            EntitySelectionService entitySelectionService)
        {
            _visualElementInitializer = visualElementInitializer;
            _tooltipRegistrar = tooltipRegistrar;
            _entitySelectionService = entitySelectionService;
        }

        public VisualElement InitializeFragment()
        {
            root = NativeElements.Section();
            root.style.flexShrink = 0;

            VisualElement header = NativeElements.Row();
            header.style.minHeight = 30;
            header.style.marginBottom = 4;
            headerText = RichText(13);
            headerText.style.flexGrow = 1;
            headerText.style.flexShrink = 1;
            header.Add(headerText);
            windowButton = SmallButton(T("BeaverBuddies.Colony.Power.WindowShort"), () => PowerOverviewPanel.Instance?.Toggle());
            windowButton.style.marginLeft = 6;
            _tooltipRegistrar.RegisterWithKeyBinding(windowButton, T("BeaverBuddies.Colony.Power.WindowTooltip"), PowerOverviewPanel.KeyBindingId);
            header.Add(windowButton);
            root.Add(header);

            flowText = RichText(13);
            root.Add(flowText);
            statusText = NativeElements.MutedText("", 12);
            statusText.style.marginTop = 2;
            root.Add(statusText);

            mySettings = new VisualElement();
            mySettings.style.marginTop = 6;
            sendToggle = NativeElements.CheckBox("");
            sendToggle.RegisterValueChangedCallback(change => Change(PowerExportSetting.Sending, change.newValue, sendToggle));
            _tooltipRegistrar.Register(sendToggle, () => sendBlocked ?? string.Format(T("BeaverBuddies.Colony.Power.SendTooltip"), Plain(partnerSlot)));
            mySettings.Add(sendToggle);
            chargeToggle = NativeElements.CheckBox(T("BeaverBuddies.Colony.Power.ChargeFirst"));
            chargeToggle.RegisterValueChangedCallback(change => Change(PowerExportSetting.ChargeFirst, change.newValue, chargeToggle));
            _tooltipRegistrar.Register(chargeToggle, () => half && half.UseBatteries
                ? string.Format(T("BeaverBuddies.Colony.Power.ChargeFirstOffTooltip"), Plain(partnerSlot))
                : T("BeaverBuddies.Colony.Power.ChargeFirstTooltip"));
            mySettings.Add(chargeToggle);
            batteryToggle = NativeElements.CheckBox("");
            batteryToggle.RegisterValueChangedCallback(change => Change(PowerExportSetting.UseBatteries, change.newValue, batteryToggle));
            _tooltipRegistrar.Register(batteryToggle, () => string.Format(T("BeaverBuddies.Colony.Power.UseBatteriesTooltip"), Plain(partnerSlot)));
            mySettings.Add(batteryToggle);
            root.Add(mySettings);

            partnerText = NativeElements.MutedText("", 12);
            partnerText.style.marginTop = 6;
            root.Add(partnerText);

            myHalfButton = NativeElements.WoodenButton(T("BeaverBuddies.Colony.Trade.SelectMyHalf"), SelectMyHalf);
            myHalfButton.style.marginTop = 6;
            root.Add(myHalfButton);

            _visualElementInitializer.InitializeVisualElement(root);
            root.style.display = DisplayStyle.None;
            return root;
        }

        public void ShowFragment(BaseComponent entity)
        {
            half = entity.GetComponent<PowerExportHalf>();
            nextRefresh = 0;
            Refresh();
        }

        public void ClearFragment()
        {
            half = null;
            if (root != null) root.style.display = DisplayStyle.None;
        }

        public void UpdateFragment()
        {
            if (!half) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            try
            {
                RefreshUnsafe();
            }
            catch (Exception error)
            {
                // A panel must never break the game; hide it and say why once in the log.
                Plugin.LogWarning("[Colony] Power export panel: " + error);
                root.style.display = DisplayStyle.None;
                half = null;
            }
        }

        private void RefreshUnsafe()
        {
            if (!half || !ColonyModeService.IsSeparateColonies)
            {
                root.style.display = DisplayStyle.None;
                return;
            }
            NativeElements.Show(root, true);
            PowerExportHalf partner = half.Partner;
            int me = PowerExports.ColonyOf(half), them = PowerExports.ColonyOf(partner), local = ColonySession.LocalSlot;
            partnerSlot = them;
            bool mine = local >= 0 && me == local;
            bool linked = me >= 0 && them >= 0 && me != them;

            NativeElements.Show(windowButton, PowerOverviewPanel.Instance != null);
            NativeElements.Show(myHalfButton, false);
            if (!mine || !linked)
            {
                NativeElements.Show(mySettings, false);
                NativeElements.Show(partnerText, false);
                NativeElements.SetText(statusText, "");
                if (linked && local >= 0 && them == local)
                {
                    // The other colony's half of a facility the player shares.
                    NativeElements.SetText(headerText, string.Format(T("BeaverBuddies.Colony.Power.TheirHalfTitle"), PowerExportText.ColoredName(me)));
                    NativeElements.SetText(flowText, FlowLine(half, partner, me, them));
                    NativeElements.SetText(statusText, string.Format(T("BeaverBuddies.Colony.Power.TheirHalf"), Plain(me)));
                    NativeElements.Show(myHalfButton, true);
                }
                else if (linked)
                {
                    NativeElements.SetText(headerText, string.Format(T("BeaverBuddies.Colony.Power.Between"),
                        PowerExportText.ColoredName(me), PowerExportText.ColoredName(them)));
                    NativeElements.SetText(flowText, FlowLine(half, partner, me, them));
                }
                else
                {
                    NativeElements.SetText(headerText, T("BeaverBuddies.Colony.Power.NotLinkedTitle"));
                    NativeElements.SetText(flowText, T(me >= 0 && me == them
                        ? "BeaverBuddies.Colony.Power.Status.SameColony" : "BeaverBuddies.Colony.Power.NotLinked"));
                }
                return;
            }

            NativeElements.SetText(headerText, string.Format(T("BeaverBuddies.Colony.Power.LinkedWith"), PowerExportText.ColoredName(them)));
            NativeElements.SetText(flowText, FlowLine(half, partner, me, them));
            PowerExportHalf sender = half.Sending ? half : partner != null && partner.Sending ? partner : null;
            string status = sender == null || sender.Status == PowerExportStatus.Sending ? ""
                : PowerExportText.Status(sender, PowerExports.ColonyOf(sender), PowerExports.ColonyOf(sender.Partner));
            NativeElements.SetText(statusText, status);
            NativeElements.Show(statusText, status.Length > 0);

            NativeElements.Show(mySettings, true);
            sendToggle.text = string.Format(T("BeaverBuddies.Colony.Power.Send"), Plain(them));
            batteryToggle.text = string.Format(T("BeaverBuddies.Colony.Power.UseBatteries"), Plain(them));
            sendToggle.SetValueWithoutNotify(half.Sending);
            chargeToggle.SetValueWithoutNotify(half.ChargeFirst);
            batteryToggle.SetValueWithoutNotify(half.UseBatteries);
            sendBlocked = PowerExportActions.SendBlocked(half);
            sendToggle.SetEnabled(sendBlocked == null);
            // Only the sending side's batteries count; while its batteries feed the other colony they don't charge first.
            chargeToggle.SetEnabled(half.Sending && !half.UseBatteries);
            batteryToggle.SetEnabled(half.Sending);

            NativeElements.Show(partnerText, partner != null);
            if (partner != null)
                NativeElements.SetText(partnerText, string.Format(T(partner.Sending
                    ? "BeaverBuddies.Colony.Power.PartnerSends" : "BeaverBuddies.Colony.Power.PartnerDoesNotSend"), Plain(them)));
        }

        /// <summary>"Player 1 → Player 2: 140 hp" while power crosses, else "No power crosses".</summary>
        private static string FlowLine(PowerExportHalf half, PowerExportHalf partner, int me, int them)
        {
            PowerExportHalf sender = half.Sending ? half : partner != null && partner.Sending ? partner : null;
            if (sender == null) return T("BeaverBuddies.Colony.Power.Status.NotSending");
            int from = sender == half ? me : them, to = sender == half ? them : me;
            return string.Format(T("BeaverBuddies.Colony.Power.FlowLine"), PowerExportText.ColoredName(from), PowerExportText.ColoredName(to), sender.Flow);
        }

        private void Change(PowerExportSetting setting, bool value, Toggle toggle)
        {
            if (!half) return;
            // Shown as it is until the action comes back and is played.
            toggle.SetValueWithoutNotify(PowerExportActions.Current(half, setting));
            PowerExportActions.Change(half, setting, value);
            nextRefresh = 0;
        }

        private void SelectMyHalf()
        {
            PowerExportHalf partner = half ? half.Partner : null;
            if (!partner) return;
            try { _entitySelectionService.SelectAndFocusOn(partner); }
            catch (Exception error) { Plugin.LogWarning("[Colony] Could not select the other half: " + error.Message); }
        }

        private static Label RichText(int size)
        {
            Label label = NativeElements.Text("", size);
            label.enableRichText = true;
            return label;
        }

        private static Button SmallButton(string text, Action onClick)
        {
            Button button = NativeElements.WoodenButton(text, onClick);
            button.style.minHeight = 24;
            button.style.height = 24;
            button.style.paddingTop = 0;
            button.style.paddingBottom = 0;
            button.style.fontSize = 12;
            button.style.flexShrink = 0;
            return button;
        }

        private static string Plain(int slot) => NativeNames.Plain(slot);

        private static string T(string key) => RegisteredLocalizationService.T(key);
    }
}
