using System;
using System.Globalization;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// The trading post's offer form, kept free of the game so it can be checked headless: reading its boxes, what one
    /// click of − or + does, and which of the form's messages applies. An offer the form calls an exchange, a gift or a
    /// request is exactly one <see cref="ExchangeTerms.AreValid"/> and <see cref="ExchangeTerms.AreValidRounds"/> accept.
    /// A post carries at most <see cref="ExchangeTerms.MaxAmount"/> of each side a round, but a player may type more:
    /// what they type is then the whole trade, split into the fewest rounds that carry it (<see cref="Split"/>), and the
    /// rounds box is not read.
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
            /// <summary>An amount box does not hold a whole number from 0 to <see cref="ExchangeTerms.MaxAmount"/>.</summary>
            BadAmount,
            /// <summary>The rounds box does not hold a whole number from 1 to <see cref="ExchangeTerms.MaxRounds"/>.</summary>
            BadRounds,
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
        public const int MaxTyped = ExchangeTerms.MaxAmount * ExchangeTerms.MaxRounds;

        /// <summary>
        /// An amount box: empty means 0; otherwise digits only, up to <see cref="MaxTyped"/>. More than
        /// <see cref="ExchangeTerms.MaxAmount"/> is split into rounds by <see cref="Judge"/>.
        /// </summary>
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

        /// <summary>The rounds box: digits only, from 1 to <see cref="ExchangeTerms.MaxRounds"/>.</summary>
        public static bool TryReadRounds(string text, out int rounds) =>
            int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out rounds) && ExchangeTerms.AreValidRounds(rounds);

        /// <summary>
        /// The form as a whole. A repeating offer does not read the rounds box (it counts as 1 and is ignored). In a
        /// mixed-factions game <paramref name="giveAllowed"/> and <paramref name="getAllowed"/> say what the factions let
        /// cross each way (FactionTrade); without them everything may. An offer comes back as each round's amounts
        /// and the rounds, already split when a box held more than a round carries.
        /// </summary>
        public static Verdict Judge(string giveItem, string giveText, string getItem, string getText, string roundsText, bool repeat,
            out int give, out int get, out int rounds, Func<string, bool> giveAllowed = null, Func<string, bool> getAllowed = null) =>
            Judge(giveItem, giveText, getItem, getText, roundsText, repeat, out give, out get, out rounds, out _, giveAllowed, getAllowed);

        /// <summary>
        /// <see cref="Judge(string, string, string, string, string, bool, out int, out int, out int, Func{string, bool}, Func{string, bool})"/>,
        /// and whether the boxes were split into rounds (<paramref name="split"/>).
        /// </summary>
        public static Verdict Judge(string giveItem, string giveText, string getItem, string getText, string roundsText, bool repeat,
            out int give, out int get, out int rounds, out bool split, Func<string, bool> giveAllowed = null, Func<string, bool> getAllowed = null)
        {
            split = false;
            bool amountsRead = TryReadAmount(giveText, out give) & TryReadAmount(getText, out get);
            rounds = 1;
            if (!amountsRead) return Verdict.BadAmount;
            if (give == 0 && get == 0) return Verdict.NothingEitherWay;
            if ((give > 0 && string.IsNullOrEmpty(giveItem)) || (get > 0 && string.IsNullOrEmpty(getItem))) return Verdict.NoItem;
            if (give > 0 && get > 0 && string.Equals(giveItem, getItem, StringComparison.Ordinal)) return Verdict.SameItem;
            if (give > 0 && giveAllowed != null && !giveAllowed(giveItem)) return Verdict.GiveNotAllowed;
            if (get > 0 && getAllowed != null && !getAllowed(getItem)) return Verdict.GetNotAllowed;
            // More than a round carries is the whole trade: it is split into rounds, and the rounds box is not read.
            bool whole = give > ExchangeTerms.MaxAmount || get > ExchangeTerms.MaxAmount;
            if (!repeat && !whole && !TryReadRounds(roundsText, out rounds))
            {
                rounds = 1;
                return Verdict.BadRounds;
            }
            if (whole)
            {
                if (!Split(give, get, 1, repeat, out int eachGive, out int eachGet, out int splitRounds)) return Verdict.BadAmount;
                give = eachGive;
                get = eachGet;
                rounds = splitRounds;
                split = true;
            }
            if (get == 0) return Verdict.Gift;
            if (give == 0) return Verdict.Request;
            return Verdict.Exchange;
        }

        /// <summary>
        /// Splits <paramref name="give"/> for <paramref name="get"/> a round, over <paramref name="rounds"/> rounds, into
        /// the fewest rounds that carry the whole: 300 for 300 once is 3 rounds of 100 for 100. When the two don't divide
        /// evenly, each round's amounts are the nearest whole numbers to the same ratio (never 0 for a side that gives
        /// something). A repeating offer keeps repeating, with its round cut down to fit. False when the whole needs more
        /// than <see cref="ExchangeTerms.MaxRounds"/> rounds.
        /// </summary>
        public static bool Split(int give, int get, int rounds, bool repeat, out int eachGive, out int eachGet, out int splitRounds)
        {
            if (repeat) rounds = 1;
            long wholeGive = (long)Math.Max(0, give) * Math.Max(1, rounds), wholeGet = (long)Math.Max(0, get) * Math.Max(1, rounds);
            long most = Math.Max(wholeGive, wholeGet);
            long needed = Math.Max(1, (most + ExchangeTerms.MaxAmount - 1) / ExchangeTerms.MaxAmount);
            eachGive = eachGet = 0;
            splitRounds = 1;
            if (!repeat && needed > ExchangeTerms.MaxRounds) return false;
            eachGive = Share(wholeGive, needed);
            eachGet = Share(wholeGet, needed);
            splitRounds = repeat ? 1 : (int)needed;
            return true;
        }

        // One round's part of a whole: the nearest whole number, at least 1 of anything given, never more than a round carries.
        private static int Share(long whole, long rounds)
        {
            if (whole <= 0) return 0;
            long each = (long)Math.Round((double)whole / rounds, MidpointRounding.AwayFromZero);
            return (int)Math.Max(1, Math.Min(ExchangeTerms.MaxAmount, each));
        }

        /// <summary>
        /// How far one click of − or + moves an amount: ten at a time (Shift: one), beavers one at a time (Shift: ten).
        /// </summary>
        public static int Step(string item, bool shift) => item == ExchangeTerms.Beavers ? (shift ? 10 : 1) : (shift ? 1 : 10);

        /// <summary>How far one click moves the rounds: one (Shift: ten).</summary>
        public static int RoundsStep(bool shift) => shift ? 10 : 1;

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

        /// <summary>
        /// An amount after one click, within 0 and <see cref="ExchangeTerms.MaxAmount"/>; an amount already typed above
        /// that steps on within <see cref="MaxTyped"/>, so − and + never jump it back to one round's worth.
        /// </summary>
        public static int Stepped(int amount, int step, bool up) =>
            Stepped(amount, step, up, 0, amount > ExchangeTerms.MaxAmount ? MaxTyped : ExchangeTerms.MaxAmount);
    }
}
