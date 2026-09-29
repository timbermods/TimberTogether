using System;
using System.Collections.Generic;
using System.Linq;

namespace BeaverBuddies.Colonies
{
    /// <summary>Why a colony may not start sending power to another (see <see cref="PowerExportMath.Check"/>).</summary>
    public enum PowerLinkRefusal
    {
        None,
        /// <summary>Both halves are in the same colony, or a half is in none.</summary>
        NotTwoColonies,
        /// <summary>The other colony already sends power to this one: power goes one way between two colonies.</summary>
        PartnerSending,
        /// <summary>This colony already sends power to a third colony (each colony sends to one).</summary>
        SendingElsewhere,
        /// <summary>The other colony already gets power from a third colony (each colony receives from one).</summary>
        PartnerReceivingElsewhere,
        /// <summary>Power would come back round to this colony through others.</summary>
        MakesCircle,
    }

    /// <summary>
    /// The rules and the arithmetic of the Power Export Facility, with nothing of the game in them (checked headless in
    /// StabilityTests).
    ///
    /// Links: a colony sends power to at most one colony and receives from at most one; between two colonies it goes one
    /// way; and it never comes back round (A → B → C → A). Links therefore make chains, each with a start and an end.
    ///
    /// Flow, on those chains, every tick: a network sends only what the next can use (its consumers' shortfall, room in
    /// its batteries, and what it passes on in turn), and only what it has to spare after its own consumers. With
    /// "charge my batteries first" its batteries fill before anything is sent; with "use my batteries" its stored power
    /// may be sent too, as far as its batteries can give after its own consumers. Power a network gets is spare power it
    /// may pass on, so B can pass A's leftover on to C.
    /// </summary>
    public static class PowerExportMath
    {
        // ---- links between colonies ----

        /// <summary>
        /// Whether <paramref name="from"/> may start sending to <paramref name="to"/>, given the links already on (from
        /// every other sending half, in any order; the same link twice is fine).
        /// </summary>
        public static PowerLinkRefusal Check(int from, int to, IEnumerable<(int from, int to)> existing)
        {
            if (from < 0 || to < 0 || from == to) return PowerLinkRefusal.NotTwoColonies;
            var links = existing.Where(l => l.from >= 0 && l.to >= 0 && l.from != l.to).Distinct().ToList();
            if (links.Any(l => l.from == to && l.to == from)) return PowerLinkRefusal.PartnerSending;
            if (links.Any(l => l.from == from && l.to != to)) return PowerLinkRefusal.SendingElsewhere;
            if (links.Any(l => l.to == to && l.from != from)) return PowerLinkRefusal.PartnerReceivingElsewhere;
            if (links.Contains((from, to))) return PowerLinkRefusal.None;
            // Follow the power on from `to`: each colony sends to one at most, so this is a walk, bounded by the colonies.
            int at = to;
            for (int step = 0; step <= links.Count; step++)
            {
                int next = -1;
                foreach (var link in links)
                {
                    if (link.from == at) { next = link.to; break; }
                }
                if (next < 0) return PowerLinkRefusal.None;
                if (next == from) return PowerLinkRefusal.MakesCircle;
                at = next;
            }
            return PowerLinkRefusal.MakesCircle;
        }

        /// <summary>
        /// The links that obey the rules, taken in the order given (the first wins): what is let through every tick, so a
        /// link that stopped obeying them (a road now reaches a half from another colony) is left out the same way on
        /// every computer.
        /// </summary>
        public static HashSet<(int from, int to)> Admit(IEnumerable<(int from, int to)> requested)
        {
            var admitted = new List<(int from, int to)>();
            foreach (var link in requested)
            {
                if (Check(link.from, link.to, admitted) == PowerLinkRefusal.None && !admitted.Contains(link)) admitted.Add(link);
            }
            return new HashSet<(int from, int to)>(admitted);
        }

        /// <summary>Each colony's place along its chain: 0 for a colony nothing sends to, then 1, 2... (links admitted).</summary>
        public static Dictionary<int, int> Depths(IEnumerable<(int from, int to)> admitted)
        {
            var links = admitted.ToList();
            var depth = new Dictionary<int, int>();
            foreach (var link in links)
            {
                if (!depth.ContainsKey(link.from)) depth[link.from] = 0;
                if (!depth.ContainsKey(link.to)) depth[link.to] = 0;
            }
            // At most one link into each colony and no circles, so this settles within as many passes as there are links.
            for (int pass = 0; pass <= links.Count; pass++)
            {
                bool changed = false;
                foreach (var link in links)
                {
                    int want = depth[link.from] + 1;
                    if (depth[link.to] < want && want <= links.Count) { depth[link.to] = want; changed = true; }
                }
                if (!changed) break;
            }
            return depth;
        }

        // ---- flow ----

        /// <summary>A power network on a chain, in hp, without what Power Export Facilities send or bring.</summary>
        public sealed class Network
        {
            /// <summary>Its own generators' output.</summary>
            public int Supply;
            /// <summary>Its own consumers' demand.</summary>
            public int Demand;
            /// <summary>The power its batteries could take in (0 when full or it has none).</summary>
            public int BatteryRoom;
            /// <summary>The power its batteries could give this tick (0 when empty or it has none).</summary>
            public int BatteryPower;
            /// <summary>The sending half's "Charge my batteries first" (on: its batteries fill before anything is sent).</summary>
            public bool ChargeFirst = true;
            /// <summary>The sending half's "Use my batteries" (on: its stored power may be sent too).</summary>
            public bool UseBatteries;
        }

        /// <summary>
        /// How much crosses each edge (a sending half's network to its partner's), in the order the edges are given.
        /// Every edge must go from a network earlier in <paramref name="order"/> than the one it feeds (the caller sorts
        /// networks by their colony's place along the chain); an edge that does not is given nothing.
        /// </summary>
        public static int[] Flow(IReadOnlyList<Network> networks, IReadOnlyList<(int from, int to)> edges)
        {
            int n = networks.Count;
            var flows = new int[edges.Count];
            var valid = new bool[edges.Count];
            for (int e = 0; e < edges.Count; e++)
                valid[e] = edges[e].from >= 0 && edges[e].to >= 0 && edges[e].from < n && edges[e].to < n && edges[e].from < edges[e].to;

            // What each network can use, from the end of the chains back: its shortfall, its batteries' room, and what
            // the networks it feeds can use.
            var want = new long[n];
            for (int i = n - 1; i >= 0; i--)
            {
                Network net = networks[i];
                long use = Math.Max(0, net.Demand - net.Supply) + Math.Max(0, net.BatteryRoom);
                for (int e = 0; e < edges.Count; e++)
                {
                    if (valid[e] && edges[e].from == i) use += want[edges[e].to];
                }
                want[i] = Math.Min(use, int.MaxValue);
            }

            // What each network has, from the start of the chains on, given out to the networks it feeds in order. Two
            // networks feeding one (a colony's two networks, each at its own facility) share what it can use.
            var incoming = new long[n];
            var wantLeft = (long[])want.Clone();
            for (int i = 0; i < n; i++)
            {
                Network net = networks[i];
                long live = net.Supply + incoming[i] - (long)net.Demand;
                long available;
                if (net.UseBatteries)
                    // Its batteries give what they can beyond its own shortfall; its batteries charge only from what is left.
                    available = Math.Max(0, live) + Math.Max(0, net.BatteryPower - Math.Max(0, -live));
                else if (net.ChargeFirst)
                    available = Math.Max(0, live - Math.Max(0, net.BatteryRoom));
                else
                    available = Math.Max(0, live);
                for (int e = 0; e < edges.Count; e++)
                {
                    if (!valid[e] || edges[e].from != i) continue;
                    long give = Math.Min(available, wantLeft[edges[e].to]);
                    if (give <= 0) continue;
                    flows[e] = (int)give;
                    available -= give;
                    wantLeft[edges[e].to] -= give;
                    incoming[edges[e].to] += give;
                }
            }
            return flows;
        }
    }
}
