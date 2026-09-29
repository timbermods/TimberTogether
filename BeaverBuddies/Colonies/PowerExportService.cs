using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.EntitySystem;
using Timberborn.MechanicalSystem;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// Moves power through the Power Export Facilities, every tick on every computer, from saved state and the game's own
    /// networks only (so every computer moves the same). A link from one colony to another is on while a half of that
    /// colony sends and obeys the rules (<see cref="PowerExportMath.Admit"/>, in the order of the halves' ids). A facility
    /// carries its link while both halves are finished, each half's road and network belong to its own colony, and both
    /// workers are at work. How much crosses is <see cref="PowerExportMath.Flow"/> on the networks involved, figured from
    /// the networks as they are, without the facilities' own share.
    /// </summary>
    public class PowerExportService : RegisteredSingleton, ILoadableSingleton, ITickableSingleton
    {
        // A worker between two shifts of work stops for a tick or two; the half counts as staffed through that.
        private const int WorkerGraceTicks = 5;

        private readonly EntityComponentRegistry _entityComponentRegistry;
        private readonly IDayNightCycle _dayNightCycle;
        private long ticks;
        private bool anyFlow;
        private readonly List<PowerExportHalf> halves = new List<PowerExportHalf>();

        /// <summary>Display: the links let through at the last tick (sender colony → receiver colony) and what each carried.</summary>
        public IReadOnlyDictionary<(int from, int to), int> Links => links;
        private readonly Dictionary<(int from, int to), int> links = new Dictionary<(int from, int to), int>();

        public static PowerExportService Instance => SingletonManager.GetSingleton<PowerExportService>();

        public PowerExportService(EntityComponentRegistry entityComponentRegistry, IDayNightCycle dayNightCycle)
        {
            _entityComponentRegistry = entityComponentRegistry;
            _dayNightCycle = dayNightCycle;
        }

        // Loadable only so the game builds it at load: it is found through SingletonManager, not injected.
        public void Load() { }

        /// <summary>Every facility half, finished or not, in the order of their ids (the same on every computer).</summary>
        public List<PowerExportHalf> AllHalves()
        {
            return _entityComponentRegistry.GetEnabled<PowerExportHalf>()
                .Select(half => (id: Events.ReplayEvent.GetEntityID(half), half))
                .Where(p => p.id != null)
                .OrderBy(p => p.id, StringComparer.Ordinal)
                .Select(p => p.half).ToList();
        }

        /// <summary>
        /// The links asked for now, by every sending half but <paramref name="except"/> (its partner included: it would
        /// send the other way): what a colony's new link is checked against when its player turns sending on.
        /// </summary>
        public List<(int from, int to)> LinksExcept(PowerExportHalf except)
        {
            var result = new List<(int from, int to)>();
            foreach (PowerExportHalf half in AllHalves())
            {
                if (half == except || !half.Sending) continue;
                int from = PowerExports.ColonyOf(half), to = PowerExports.ColonyOf(half.Partner);
                if (from >= 0 && to >= 0 && from != to) result.Add((from, to));
            }
            return result;
        }

        private static readonly ColonyProfiler.Spot Moving = ColonyProfiler.Declare("Power exports");

        public void Tick()
        {
            ticks++;
            if (!ColonyModeService.IsSeparateColonies)
            {
                if (anyFlow) StopAll();
                return;
            }
            long started = ColonyProfiler.Start();
            try
            {
                Move();
            }
            finally
            {
                ColonyProfiler.Stop(Moving, started);
            }
        }

        private void StopAll()
        {
            foreach (PowerExportHalf half in AllHalves())
            {
                half.SetPower(0, 0);
                half.Flow = 0;
            }
            links.Clear();
            anyFlow = false;
        }

        private void Move()
        {
            halves.Clear();
            halves.AddRange(AllHalves());
            links.Clear();
            if (halves.Count == 0)
            {
                anyFlow = false;
                return;
            }

            // Who is who, and which workers are at work.
            var colony = new Dictionary<PowerExportHalf, int>();
            var network = new Dictionary<PowerExportHalf, int>();
            foreach (PowerExportHalf half in halves)
            {
                if (half.WorkingNow) half.LastWorkingTick = ticks;
                colony[half] = PowerExports.ColonyOf(half);
                network[half] = half.IsFinished ? PowerExports.NetworkColonyOf(half) : -1;
            }

            // The links asked for, in the halves' order, and those that obey the rules.
            var requested = new List<(int from, int to)>();
            foreach (PowerExportHalf half in halves)
            {
                PowerExportHalf partner = half.Partner;
                if (!half.Sending || partner == null || !colony.ContainsKey(partner)) continue;
                int from = colony[half], to = colony[partner];
                if (from >= 0 && to >= 0 && from != to) requested.Add((from, to));
            }
            HashSet<(int from, int to)> admitted = PowerExportMath.Admit(requested);
            Dictionary<int, int> depth = PowerExportMath.Depths(admitted);

            // Each half's status, and the facilities that carry a link now (sending half first).
            var carrying = new List<(PowerExportHalf sender, PowerExportHalf receiver)>();
            foreach (PowerExportHalf half in halves)
            {
                PowerExportHalf partner = half.Partner;
                half.Status = StatusOf(half, partner, colony, network, admitted);
                if (half.Status == PowerExportStatus.Sending) carrying.Add((half, partner));
            }

            // The networks involved, by their colony's place along its chain, then by the first half's id in them.
            var graphs = new List<MechanicalGraph>();
            var graphIndex = new Dictionary<MechanicalGraph, int>();
            var graphDepth = new Dictionary<MechanicalGraph, int>();
            foreach (var (sender, receiver) in carrying)
            {
                foreach (PowerExportHalf half in new[] { sender, receiver })
                {
                    MechanicalGraph graph = half.Node.Graph;
                    if (graphIndex.ContainsKey(graph)) continue;
                    graphIndex[graph] = graphs.Count;
                    graphs.Add(graph);
                    graphDepth[graph] = depth.TryGetValue(colony[half], out int d) ? d : 0;
                }
            }
            // A stable sort: the order found (the halves' ids) breaks ties.
            List<MechanicalGraph> ordered = graphs.Select((g, i) => (g, i)).OrderBy(p => graphDepth[p.g]).ThenBy(p => p.i).Select(p => p.g).ToList();
            var position = new Dictionary<MechanicalGraph, int>();
            for (int i = 0; i < ordered.Count; i++) position[ordered[i]] = i;

            // Every facility half's own share, taken out of its network's figures.
            var drawn = new Dictionary<MechanicalGraph, int>();
            var given = new Dictionary<MechanicalGraph, int>();
            foreach (PowerExportHalf half in halves)
            {
                MechanicalGraph graph = half.Node != null ? half.Node.Graph : null;
                if (graph == null || !position.ContainsKey(graph)) continue;
                drawn[graph] = (drawn.TryGetValue(graph, out int d) ? d : 0) + half.PowerDrawn;
                given[graph] = (given.TryGetValue(graph, out int g) ? g : 0) + half.PowerGiven;
            }
            float hours = Mathf.Max(_dayNightCycle.FixedDeltaTimeInHours, 1e-6f);
            var nets = new List<PowerExportMath.Network>();
            foreach (MechanicalGraph graph in ordered)
            {
                int room = graph.BatteryCapacity > graph.BatteryCharge
                    ? (int)Mathf.Min(Mathf.Ceil((graph.BatteryCapacity - graph.BatteryCharge) / hours), int.MaxValue / 4) : 0;
                int battery = graph.BatteryCharge > 0 ? (int)Mathf.Min(Mathf.Ceil(graph.BatteryCharge / hours), int.MaxValue / 4) : 0;
                nets.Add(new PowerExportMath.Network
                {
                    Supply = graph.PowerSupply - (given.TryGetValue(graph, out int g) ? g : 0),
                    Demand = graph.PowerDemand - (drawn.TryGetValue(graph, out int d) ? d : 0),
                    BatteryRoom = room,
                    BatteryPower = battery,
                });
            }
            var edges = new List<(int from, int to)>();
            foreach (var (sender, receiver) in carrying)
            {
                int from = position[sender.Node.Graph], to = position[receiver.Node.Graph];
                edges.Add((from, to));
                // The sending half's settings are its network's (one colony's settings, the first half's if several).
                PowerExportMath.Network net = nets[from];
                if (edges.Count(e => e.from == from) == 1)
                {
                    net.ChargeFirst = sender.ChargeFirst;
                    net.UseBatteries = sender.UseBatteries;
                }
            }
            int[] flows = PowerExportMath.Flow(nets, edges);

            // Apply: the sending half draws what crosses from its network, the receiving half gives it to its own.
            var input = new Dictionary<PowerExportHalf, int>();
            var output = new Dictionary<PowerExportHalf, int>();
            for (int e = 0; e < carrying.Count; e++)
            {
                var (sender, receiver) = carrying[e];
                int flow = flows[e];
                input[sender] = flow;
                output[receiver] = flow;
                var key = (colony[sender], colony[receiver]);
                links[key] = (links.TryGetValue(key, out int sum) ? sum : 0) + flow;
                if (flow <= 0)
                {
                    PowerExportMath.Network from = nets[edges[e].from];
                    bool spare = from.Supply + (from.UseBatteries ? (long)from.BatteryPower : 0) > from.Demand;
                    sender.Status = receiver.Status = spare ? PowerExportStatus.PartnerNeedsNone : PowerExportStatus.NothingToSpare;
                }
                else receiver.Status = PowerExportStatus.Receiving;
            }
            foreach (var link in admitted)
            {
                if (!links.ContainsKey(link)) links[link] = 0;
            }
            anyFlow = false;
            foreach (PowerExportHalf half in halves)
            {
                int take = input.TryGetValue(half, out int i) ? i : 0;
                int give = output.TryGetValue(half, out int o) ? o : 0;
                half.SetPower(take, give);
                half.Flow = Math.Max(take, give);
                if (take > 0 || give > 0) anyFlow = true;
            }
        }

        private PowerExportStatus StatusOf(PowerExportHalf half, PowerExportHalf partner, Dictionary<PowerExportHalf, int> colony,
            Dictionary<PowerExportHalf, int> network, HashSet<(int from, int to)> admitted)
        {
            if (partner == null || !colony.ContainsKey(partner)) return PowerExportStatus.Unfinished;
            bool mineSends = half.Sending, theirsSends = partner.Sending;
            if (!mineSends && !theirsSends) return PowerExportStatus.NotSending;
            if (!half.IsFinished || !partner.IsFinished) return PowerExportStatus.Unfinished;
            int a = colony[half], b = colony[partner];
            if (a < 0 || b < 0) return PowerExportStatus.NoRoad;
            if (a == b) return PowerExportStatus.SameColony;
            int na = network[half], nb = network[partner];
            if (na < 0 || nb < 0) return PowerExportStatus.NoPower;
            if (na != a || nb != b) return PowerExportStatus.Mismatched;
            if (half.Node.Graph == null || partner.Node.Graph == null) return PowerExportStatus.NoPower;
            // A paused (or otherwise blocked) half moves nothing at once: its node counts nothing in its network, so the
            // other half must not give or take either (the workers' grace would otherwise make power from nothing).
            if (!half.Node.Active || !partner.Node.Active || !Staffed(half) || !Staffed(partner)) return PowerExportStatus.NoWorker;
            // Only the half whose colony sends carries the link; its partner's status follows once the flow is known.
            PowerExportHalf sender = mineSends ? half : partner;
            if (!admitted.Contains((colony[sender], colony[sender.Partner]))) return PowerExportStatus.LinkRefused;
            return mineSends ? PowerExportStatus.Sending : PowerExportStatus.Receiving;
        }

        private bool Staffed(PowerExportHalf half) => half.LastWorkingTick != long.MinValue && ticks - half.LastWorkingTick <= WorkerGraceTicks;

        /// <summary>Diagnostics: every half's settings and flow, in id order.</summary>
        public string Fingerprint() => string.Join(" ", AllHalves().Select(h =>
            $"{(h.Sending ? "S" : "-")}{(h.ChargeFirst ? "C" : "-")}{(h.UseBatteries ? "B" : "-")}{h.Flow}"));
    }
}
