using System.Collections.Generic;
using System.Linq;

namespace BeaverBuddies.Colonies
{
    /// <summary>Each colony's wellbeing high score (ColonyWellbeingRecords), kept free of the game for the checks.</summary>
    public static class WellbeingRecords
    {
        /// <summary>
        /// Raises each colony's record to its wellbeing now, where that is higher, and returns the colonies whose record
        /// rose, lowest slot first. A colony with no beavers (null) keeps its record. Records start at 0, as the game's.
        /// </summary>
        public static List<int> Raise(int[] records, int?[] wellbeing)
        {
            var raised = new List<int>();
            for (int slot = 0; slot < records.Length && slot < wellbeing.Length; slot++)
            {
                if (wellbeing[slot] is int now && now > records[slot])
                {
                    records[slot] = now;
                    raised.Add(slot);
                }
            }
            return raised;
        }

        /// <summary>
        /// Whether a record that rose from <paramref name="previous"/> is announced: not a colony's first (its first day
        /// with beavers, when any wellbeing beats 0), as the game doesn't announce its first either. Still recorded.
        /// </summary>
        public static bool Announces(int previous) => previous > 0;

        /// <summary>The records, for the save: "slot:record" separated by commas, only the colonies that have one.</summary>
        public static string Encode(int[] records) =>
            string.Join(",", records.Select((record, slot) => (record, slot)).Where(pair => pair.record > 0)
                .Select(pair => $"{pair.slot}:{pair.record}"));

        /// <summary>Records read back from a save into <paramref name="records"/>. Anything that is not a slot and a record is skipped.</summary>
        public static void Decode(string text, int[] records)
        {
            foreach (string entry in (text ?? "").Split(','))
            {
                string[] parts = entry.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int slot) && int.TryParse(parts[1], out int record)
                    && slot >= 0 && slot < records.Length && record > 0)
                    records[slot] = record;
            }
        }
    }
}
