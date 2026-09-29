using System.Collections.Generic;

namespace BeaverBuddies.Colonies
{
    /// <summary>A power connection of a building being placed: the cell it is on and the cell it faces.</summary>
    public readonly struct ColonyTransput
    {
        public readonly ColonyCell Cell;
        public readonly ColonyCell Target;

        public ColonyTransput(ColonyCell cell, ColonyCell target)
        {
            Cell = cell;
            Target = target;
        }
    }

    /// <summary>What the power rule reads of the map. Implemented against the game in ColonyGameWorld, and by fakes in tests.</summary>
    public interface IColonyPowerMap
    {
        /// <summary>
        /// The colony whose power connection on <paramref name="target"/> faces back to <paramref name="cell"/> (built or
        /// being built), where the two would join. Null for none, or for one that joins any network (a Power Export
        /// Facility's half, or a building nobody owns).
        /// </summary>
        int? PowerOwnerFacing(ColonyCell cell, ColonyCell target);
    }

    /// <summary>
    /// The power rule of separate colonies, like the road rule: two colonies' power networks never join, except through a
    /// Power Export Facility. A mechanical building (a shaft, a gearbox, a generator, a battery, a workshop) may not be
    /// placed where one of its power connections would meet another colony's. A Power Export Facility's halves join
    /// whichever network reaches them: each has one connection, so a half can never join two networks.
    ///
    /// Reads only, and only what is the same on every computer, so a verdict is too.
    /// </summary>
    public static class ColonyPowerRule
    {
        /// <summary>Whether two nodes of these owners may be joined (nobody's joins anything).</summary>
        public static bool MayJoin(int? a, int? b) => a == null || b == null || a.Value == b.Value;

        /// <summary>Why <paramref name="slot"/>'s colony may not place a building with these power connections, or None.</summary>
        public static ColonyRefusal Conflict(int slot, IReadOnlyList<ColonyTransput> transputs, IColonyPowerMap map, out string detail)
        {
            detail = null;
            for (int i = 0; i < transputs.Count; i++)
            {
                int? owner = map.PowerOwnerFacing(transputs[i].Cell, transputs[i].Target);
                if (MayJoin(slot, owner)) continue;
                detail = $"power at {transputs[i].Cell} would join slot {owner}'s at {transputs[i].Target}";
                return ColonyRefusal.TouchesOtherPower;
            }
            return ColonyRefusal.None;
        }
    }
}
