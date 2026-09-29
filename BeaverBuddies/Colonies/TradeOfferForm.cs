using System;
using System.Globalization;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// The trading post's offer form, kept free of the game so it can be checked headless: reading its boxes, what one
    /// click of − or + does, and which of the form's messages applies. A player types the whole trade ("101 Logs for 2
    /// Gears"); an offer the form calls an exchange, a gift or a request is exactly one
    /// <see cref="ExchangeTerms.AreValidTerms"/> accepts, and it is never refused for its amounts: the game carries it in
    /// <see cref="ExchangeTerms.RoundsFor"/> rounds of up to <see cref="ExchangeTerms.MaxAmount"/> each, spread as evenly
    /// as it goes. A repeating offer's amounts are each round's, up to <see cref="ExchangeTerms.MaxAmount"/>.
    /// </summary>
    public static class TradeOfferForm
    {
        public enum Verdict
        {
            /// <summary>Something each way.</summary>
            Exchange,
            /// <summary>The offering colony gives and asks nothing back.</summary>
            Gift,
            /// <summary>The offering colony asks and gives nothing.</summary>
            Request,
            /// <summary>An amount box does not hold a whole number from 0 to <see cref="MaxTyped"/>.</summary>
            BadAmount,
            /// <summary>Both amounts are 0.</summary>
            NothingEitherWay,
            /// <summary>A side with an amount has no item chosen.</summary>
            NoItem,
            /// <summary>Both sides give the same item.</summary>
            SameItem,
            /// <summary>A mixed-factions game: what the offering colony gives may not go to the other colony's faction.</summary>
            GiveNotAllowed,
            /// <summary>A mixed-factions game: what the offering colony asks for may not come to its own faction.</summary>
            GetNotAllowed,
        }

        public static bool IsOffer(Verdict verdict) =>
            verdict == Verdict.Exchange || verdict == Verdict.Gift || verdict == Verdict.Request;

        /// <summary>The most an amount box takes: a whole exchange's worth, a full round every round.</summary>
        public const int MaxTyped = ExchangeTerms.MaxWhole;

        /// <summary>The most an amount box takes for a repeating offer (each round's) or a whole one.</summary>
        public static int MaxFor(bool repeat) => repeat ? ExchangeTerms.MaxAmount : MaxTyped;

        /// <summary>An amount box: empty means 0; otherwise digits only, up to <see cref="MaxTyped"/>.</summary>
        public static bool TryReadAmount(string text, out int amount)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                amount = 0;
                return true;
            }
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out amount) && amount <= MaxTyped;
        }

        /// <summary>The "keep at least" box: empty means 0; otherwise digits only, up to <see cref="ExchangeTerms.MaxKeep"/>.</summary>
        public static bool TryReadKeep(string text, out int keep)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                keep = 0;
                return true;
            }
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out keep) && keep <= ExchangeTerms.MaxKeep;
        }

        /// <summary>How far one click of − or + moves the reserve: fifty at a time (Shift: ten).</summary>
        public static int KeepStep(bool shift) => shift ? 10 : 50;

        /// <summary>
        /// The form as a whole: the whole trade as typed, and the rounds the game carries it in (a repeating offer: 1,
        /// each round the same). In a mixed-factions game <paramref name="giveAllowed"/> and <paramref name="getAllowed"/>
        /// say what the factions let cross each way (FactionTrade); without them everything may.
        /// </summary>
        public static Verdict Judge(string giveItem, string giveText, string getItem, string getText, bool repeat,
            out int give, out int get, out int rounds, Func<string, bool> giveAllowed = null, Func<string, bool> getAllowed = null)
        {
            bool amountsRead = TryReadAmount(giveText, out give) & TryReadAmount(getText, out get);
            rounds = 1;
            if (!amountsRead || give > MaxFor(repeat) || get > MaxFor(repeat)) return Verdict.BadAmount;
            if (give == 0 && get == 0) return Verdict.NothingEitherWay;
            if ((give > 0 && string.IsNullOrEmpty(giveItem)) || (get > 0 && string.IsNullOrEmpty(getItem))) return Verdict.NoItem;
            if (give > 0 && get > 0 && string.Equals(giveItem, getItem, StringComparison.Ordinal)) return Verdict.SameItem;
            if (give > 0 && giveAllowed != null && !giveAllowed(giveItem)) return Verdict.GiveNotAllowed;
            if (get > 0 && getAllowed != null && !getAllowed(getItem)) return Verdict.GetNotAllowed;
            rounds = repeat ? 1 : ExchangeTerms.RoundsFor(give, get);
            if (get == 0) return Verdict.Gift;
            if (give == 0) return Verdict.Request;
            return Verdict.Exchange;
        }

        /// <summary>
        /// How far one click of − or + moves an amount: ten at a time (Shift: one), beavers one at a time (Shift: ten).
        /// </summary>
        public static int Step(string item, bool shift) => item == ExchangeTerms.Beavers ? (shift ? 10 : 1) : (shift ? 1 : 10);

        /// <summary>
        /// A number after one click: on to the next whole step (from 95, + gives 100 and − gives 90), within
        /// <paramref name="min"/> and <paramref name="max"/>.
        /// </summary>
        public static int Stepped(int value, int step, bool up, int min, int max)
        {
            if (step <= 0) step = 1;
            value = Math.Max(min, Math.Min(max, value));
            long next = up ? ((long)value / step + 1) * step : ((long)value + step - 1) / step * step - step;
            return (int)Math.Max(min, Math.Min(max, next));
        }

        /// <summary>An amount after one click, within 0 and what the box takes (<see cref="MaxFor"/>).</summary>
        public static int Stepped(int amount, int step, bool up, bool repeat) => Stepped(amount, step, up, 0, MaxFor(repeat));
    }
}
