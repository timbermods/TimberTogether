using BeaverBuddies.Editor;
using BeaverBuddies.Events;
using BeaverBuddies.Panel;
using BeaverBuddies.Util;
using System;
using UnityEngine;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// A colony's player changes a setting of their half of a Power Export Facility: sending power to the other colony,
    /// charging their batteries first, or using their batteries for the other colony. Judged by the host as a change to
    /// the half (its colony only, or whoever looks after it), and checked again as it is played, on every computer:
    /// sending must obey the link rules (<see cref="PowerExportMath.Check"/>).
    /// </summary>
    [Serializable]
    public class PowerExportSettingEvent : ReplayEvent
    {
        /// <summary>The half (named entityID so the colony rules judge it by its owner).</summary>
        public string entityID;
        public int setting;
        public bool value;

        public override ColonyScope GetColonyScope() => ColonyScope.Entities(entityID);

        public override void Replay(IReplayContext context)
        {
            PowerExportHalf half = GetComponent<PowerExportHalf>(context, entityID);
            if (half == null) return;
            PowerExportSetting which = (PowerExportSetting)setting;
            int colony = PowerExports.ColonyOf(half);
            if (slot >= 0 && colony != slot)
            {
                Plugin.Log($"[Colony] Power export: colony {slot + 1} may not change a half of colony {colony + 1}");
                return;
            }
            PowerExportHalf partner = half.Partner;
            int partnerColony = PowerExports.ColonyOf(partner);
            if (which == PowerExportSetting.Sending && value && !half.Sending)
            {
                PowerLinkRefusal refusal = PowerExportMath.Check(colony, partnerColony,
                    PowerExportService.Instance?.LinksExcept(half) ?? new System.Collections.Generic.List<(int, int)>());
                if (refusal != PowerLinkRefusal.None)
                {
                    Plugin.Log($"[Colony] Power export: colony {colony + 1} may not send to colony {partnerColony + 1}: {refusal}");
                    if (player == ColonySession.LocalPlayer)
                        SingletonManager.GetSingleton<ColonyRulesService>()?.ShowNotice(PowerExportText.Refusal(refusal, partnerColony));
                    return;
                }
            }
            bool was = which switch
            {
                PowerExportSetting.Sending => half.Sending,
                PowerExportSetting.ChargeFirst => half.ChargeFirst,
                _ => half.UseBatteries,
            };
            if (was == value) return;
            half.Set(which, value);
            ColonyDigest.Note("power", colony, setting, value ? 1 : 0);
            Plugin.Log($"[Colony] Power export: colony {colony + 1} set {which} to {value}");
            // The other colony hears that power starts or stops coming (display only, on its computers).
            if (which == PowerExportSetting.Sending && partnerColony >= 0 && partnerColony == ColonySession.LocalSlot)
                SingletonManager.GetSingleton<ColonyRulesService>()?.ShowNotice(string.Format(RegisteredLocalizationService.T(value
                    ? "BeaverBuddies.Colony.Power.Notice.Started" : "BeaverBuddies.Colony.Power.Notice.Stopped"),
                    NativeNames.Plain(colony)), warning: false);
        }

        public override string ToActionString() => $"Power export {(PowerExportSetting)setting} {(value ? "on" : "off")}";
    }

    /// <summary>What the facility's panel and the Power window do with a half's check boxes.</summary>
    internal static class PowerExportActions
    {
        public static bool Current(PowerExportHalf half, PowerExportSetting setting) => setting switch
        {
            PowerExportSetting.Sending => half.Sending,
            PowerExportSetting.ChargeFirst => half.ChargeFirst,
            _ => half.UseBatteries,
        };

        /// <summary>Why the half's colony can't start sending now (a sentence for the greyed box), or null.</summary>
        public static string SendBlocked(PowerExportHalf half)
        {
            if (!half || half.Sending) return null;
            int me = PowerExports.ColonyOf(half), them = PowerExports.ColonyOf(half.Partner);
            PowerLinkRefusal refusal = PowerExportMath.Check(me, them,
                PowerExportService.Instance?.LinksExcept(half) ?? new System.Collections.Generic.List<(int, int)>());
            return refusal == PowerLinkRefusal.None ? null : PowerExportText.Refusal(refusal, them);
        }

        /// <summary>Sends the change through the host (power exports work in a hosted game, as trading does).</summary>
        public static void Change(PowerExportHalf half, PowerExportSetting setting, bool value)
        {
            string id = half ? GetEntityID(half) : null;
            if (id == null) return;
            if (ReplayEvent.DoPrefix(() => new PowerExportSettingEvent { entityID = id, setting = (int)setting, value = value }))
                SingletonManager.GetSingleton<ColonyRulesService>()?.ShowNotice(PowerExportText.T("BeaverBuddies.Colony.Power.HostFirst"));
        }

        private static string GetEntityID(PowerExportHalf half) => ReplayEvent.GetEntityID(half);
    }

    /// <summary>A colony's name for plain text (no markup).</summary>
    internal static class NativeNames
    {
        public static string Plain(int slot) => Util.NativeElements.Plain(ColonyExchangeService.ColonyName(slot));
    }

    /// <summary>The Power Export Facility's texts shared by its panel, the Power window and the refusals.</summary>
    internal static class PowerExportText
    {
        public static string T(string key) => RegisteredLocalizationService.T(key);

        /// <summary>Why sending to <paramref name="partner"/> can't start, in a sentence.</summary>
        public static string Refusal(PowerLinkRefusal refusal, int partner) => refusal switch
        {
            PowerLinkRefusal.NotTwoColonies => T("BeaverBuddies.Colony.Power.Refused.NotTwoColonies"),
            PowerLinkRefusal.PartnerSending => string.Format(T("BeaverBuddies.Colony.Power.Refused.PartnerSending"), NativeNames.Plain(partner)),
            PowerLinkRefusal.SendingElsewhere => T("BeaverBuddies.Colony.Power.Refused.SendingElsewhere"),
            PowerLinkRefusal.PartnerReceivingElsewhere => string.Format(T("BeaverBuddies.Colony.Power.Refused.PartnerReceivingElsewhere"), NativeNames.Plain(partner)),
            PowerLinkRefusal.MakesCircle => T("BeaverBuddies.Colony.Power.Refused.MakesCircle"),
            _ => "",
        };

        /// <summary>A colony's name in bold in its player's colour, lightened where it would be hard to read.</summary>
        public static string ColoredName(int slot)
        {
            string name = NativeNames.Plain(slot);
            if (slot < 0 || slot >= StartingLocationPlayer.PLAYER_COLORS.Length) return "<b>" + name + "</b>";
            string hex = ChatFormat.ReadableHex(ColorUtility.ToHtmlStringRGB(StartingLocationPlayer.PLAYER_COLORS[slot]));
            return $"<color=#{hex}><b>{name}</b></color>";
        }

        /// <summary>A half's status line: power from <paramref name="sender"/>'s colony to <paramref name="receiver"/>'s.</summary>
        public static string Status(PowerExportHalf half, int sender, int receiver)
        {
            string from = NativeNames.Plain(sender), to = NativeNames.Plain(receiver);
            return half.Status switch
            {
                PowerExportStatus.Sending => string.Format(T("BeaverBuddies.Colony.Power.Status.Flowing"), from, to, half.Flow),
                PowerExportStatus.Receiving => string.Format(T("BeaverBuddies.Colony.Power.Status.Flowing"), from, to, half.Flow),
                PowerExportStatus.NotSending => T("BeaverBuddies.Colony.Power.Status.NotSending"),
                PowerExportStatus.Unfinished => T("BeaverBuddies.Colony.Power.Status.Unfinished"),
                PowerExportStatus.NoRoad => T("BeaverBuddies.Colony.Power.Status.NoRoad"),
                PowerExportStatus.SameColony => T("BeaverBuddies.Colony.Power.Status.SameColony"),
                PowerExportStatus.NoPower => T("BeaverBuddies.Colony.Power.Status.NoPower"),
                PowerExportStatus.Mismatched => T("BeaverBuddies.Colony.Power.Status.Mismatched"),
                PowerExportStatus.NoWorker => T("BeaverBuddies.Colony.Power.Status.NoWorker"),
                PowerExportStatus.LinkRefused => T("BeaverBuddies.Colony.Power.Status.LinkRefused"),
                PowerExportStatus.PartnerNeedsNone => string.Format(T("BeaverBuddies.Colony.Power.Status.PartnerNeedsNone"), to),
                PowerExportStatus.NothingToSpare => string.Format(T("BeaverBuddies.Colony.Power.Status.NothingToSpare"), from),
                _ => "",
            };
        }
    }
}
