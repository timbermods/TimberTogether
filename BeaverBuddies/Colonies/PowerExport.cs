using HarmonyLib;
using System.Collections.Generic;
using System.Collections.Immutable;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.Buildings;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.LinkedBuildingSystem;
using Timberborn.MechanicalSystem;
using Timberborn.Persistence;
using Timberborn.Workshops;
using Timberborn.WorldPersistence;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// Marks the Power Export Facility (Buildings/Power/MultiColonyPowerExport): two linked halves, as a District
    /// Crossing's, each with one worker and one power connection, that send spare power from one colony's network to
    /// another's. The game finds spec types by their class name across every loaded assembly, so the name is this mod's own.
    /// </summary>
    public record MultiColonyPowerExportSpec : ComponentSpec
    {
    }

    /// <summary>Why a Power Export Facility half moves no power now (display only; worked out every tick).</summary>
    public enum PowerExportStatus
    {
        /// <summary>Power crosses now (see <see cref="PowerExportHalf.Flow"/>).</summary>
        Sending,
        Receiving,
        /// <summary>Neither half's colony sends.</summary>
        NotSending,
        /// <summary>A half is still being built.</summary>
        Unfinished,
        /// <summary>A half has no colony's road at its door.</summary>
        NoRoad,
        /// <summary>Both halves are in the same colony.</summary>
        SameColony,
        /// <summary>A half's power connection reaches no network.</summary>
        NoPower,
        /// <summary>A half's road and its power network belong to different colonies.</summary>
        Mismatched,
        /// <summary>A half has no worker at work (none assigned, off shift, or paused).</summary>
        NoWorker,
        /// <summary>The link breaks a rule (one way, one to one, no circles): another link was there first.</summary>
        LinkRefused,
        /// <summary>The receiving side can use nothing now.</summary>
        PartnerNeedsNone,
        /// <summary>The sending side has nothing to spare now.</summary>
        NothingToSpare,
    }

    /// <summary>
    /// A half of a Power Export Facility: its colony's settings (saved with it, set by an action the host judges) and
    /// what it moves (worked out every tick by <see cref="PowerExportService"/>, the same on every computer). Its power
    /// node is a consumer and a generator of 1 hp each, scaled to what crosses: the sending half draws it from its
    /// network, the receiving half gives it to its own.
    /// </summary>
    public class PowerExportHalf : BaseComponent, IAwakableComponent, IPersistentEntity, IFinishedStateListener, IRegisteredComponent
    {
        private static readonly ComponentKey HalfKey = new ComponentKey("BeaverBuddies.PowerExportHalf");
        private static readonly PropertyKey<bool> SendingKey = new PropertyKey<bool>("Sending");
        private static readonly PropertyKey<bool> ChargeFirstKey = new PropertyKey<bool>("ChargeFirst");
        private static readonly PropertyKey<bool> UseBatteriesKey = new PropertyKey<bool>("UseBatteries");

        private MechanicalNode node;
        private Workshop workshop;
        private LinkedBuilding linked;
        private BlockObject blockObject;
        private int appliedInput = -1, appliedOutput = -1;

        /// <summary>This half's colony sends power to the other half's.</summary>
        public bool Sending { get; private set; }
        /// <summary>Its colony's batteries fill before anything is sent.</summary>
        public bool ChargeFirst { get; private set; } = true;
        /// <summary>Its colony's stored power may be sent too.</summary>
        public bool UseBatteries { get; private set; }

        // Worked out every tick (not saved; the same on every computer).
        /// <summary>Power crossing through this half now, in hp: drawn from its network when sending, given when receiving.</summary>
        public int Flow { get; internal set; }
        public PowerExportStatus Status { get; internal set; } = PowerExportStatus.NotSending;
        /// <summary>The last tick its worker was at work (a worker stops for a tick between shifts of work).</summary>
        internal long LastWorkingTick = long.MinValue;

        public MechanicalNode Node => node;
        public bool IsFinished => blockObject && blockObject.IsFinished;
        public bool WorkingNow => workshop && workshop.CurrentlyWorking;

        /// <summary>The other half (linked when both are placed), or null.</summary>
        public PowerExportHalf Partner => linked && linked._linked ? linked._linked.GetComponent<PowerExportHalf>() : null;

        public void Awake()
        {
            node = GetComponent<MechanicalNode>();
            workshop = GetComponent<Workshop>();
            linked = GetComponent<LinkedBuilding>();
            blockObject = GetComponent<BlockObject>();
        }

        public void OnEnterFinishedState() => SetPower(0, 0);

        public void OnExitFinishedState()
        {
            SetPower(0, 0);
            Flow = 0;
        }

        /// <summary>What the half's node draws from and gives to its network, in hp (every tick, every computer).</summary>
        internal void SetPower(int input, int output)
        {
            if (node == null) return;
            if (appliedInput != input)
            {
                appliedInput = input;
                node.SetInputMultiplier(input);
            }
            if (appliedOutput != output)
            {
                appliedOutput = output;
                node.SetOutputMultiplier(output);
            }
        }

        /// <summary>What the half's node draws from its network now (what the game counts in it).</summary>
        public int PowerDrawn => node != null ? node.Actuals.PowerInput : 0;

        /// <summary>What the half's node gives to its network now.</summary>
        public int PowerGiven => node != null ? node.Actuals.PowerOutput : 0;

        internal void Set(PowerExportSetting setting, bool value)
        {
            switch (setting)
            {
                case PowerExportSetting.Sending: Sending = value; break;
                case PowerExportSetting.ChargeFirst: ChargeFirst = value; break;
                case PowerExportSetting.UseBatteries: UseBatteries = value; break;
            }
        }

        public void Save(IEntitySaver entitySaver)
        {
            // Separate colonies only: in a shared game the facility is not offered, and nothing is saved.
            if (!ColonyModeService.IsSeparateColonies) return;
            IObjectSaver saver = entitySaver.GetComponent(HalfKey);
            saver.Set(SendingKey, Sending);
            saver.Set(ChargeFirstKey, ChargeFirst);
            saver.Set(UseBatteriesKey, UseBatteries);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (!entityLoader.TryGetComponent(HalfKey, out IObjectLoader loader)) return;
            if (loader.Has(SendingKey)) Sending = loader.Get(SendingKey);
            if (loader.Has(ChargeFirstKey)) ChargeFirst = loader.Get(ChargeFirstKey);
            if (loader.Has(UseBatteriesKey)) UseBatteries = loader.Get(UseBatteriesKey);
        }
    }

    public enum PowerExportSetting
    {
        Sending,
        ChargeFirst,
        UseBatteries,
    }

    /// <summary>Who is who at a Power Export Facility, and whose power network is whose.</summary>
    public static class PowerExports
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BuildingSpec, object> isFacilityTemplate =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BuildingSpec, object>();
        private static readonly object IsFacility = new object(), IsNotFacility = new object();

        /// <summary>An entity, or a building's preview, is a Power Export Facility half.</summary>
        public static bool IsFacilityBuilding(BaseComponent entity) =>
            entity && entity.GetComponent<MultiColonyPowerExportSpec>() != null;

        /// <summary>A building template is the Power Export Facility (either faction's).</summary>
        public static bool IsFacilityTemplate(BuildingSpec spec)
        {
            if (spec == null) return false;
            return isFacilityTemplate.GetValue(spec, s => s.HasSpec<MultiColonyPowerExportSpec>() ? IsFacility : IsNotFacility) == IsFacility;
        }

        /// <summary>The colony whose district the half's door opens onto (its worker's), or -1.</summary>
        public static int ColonyOf(PowerExportHalf half)
        {
            if (!half) return -1;
            DistrictBuilding building = half.GetComponent<DistrictBuilding>();
            return DistrictOwner.OwnerOfDistrict(building ? building.District : null) ?? -1;
        }

        /// <summary>
        /// Whose power a node is, for the power rule: its district's colony, else the colony that placed it. A Power
        /// Export Facility half is nobody's (it joins whichever network reaches it), and so is a building nobody placed.
        /// Simulation code: never the construction district, which the game works out from its instant map.
        /// </summary>
        public static int? PowerOwnerOf(MechanicalNode node)
        {
            if (!node || node.GetComponent<PowerExportHalf>() != null) return null;
            return DistrictOwner.OwnerOf(node, useConstructionDistrict: false);
        }

        /// <summary>
        /// The colony a half's network belongs to: its neighbour's across its one connection, else (a neighbour nobody
        /// owns) the first owned node found in the network. -1 for a half reaching no network, or a network of nobody's.
        /// </summary>
        public static int NetworkColonyOf(PowerExportHalf half)
        {
            MechanicalNode node = half ? half.Node : null;
            if (node == null || node.Graph == null) return -1;
            ImmutableArray<Transput> transputs = node.Transputs;
            if (transputs.IsDefault) return -1;
            bool connected = false;
            foreach (Transput transput in transputs)
            {
                if (!transput.Connected) continue;
                connected = true;
                int? owner = PowerOwnerOf(transput.ConnectedNode);
                if (owner != null) return owner.Value;
            }
            if (!connected) return -1;
            // All of a network is one colony's (the power rule). Every node is looked at, so the answer never depends on
            // the order the game keeps them in (which may differ between computers); two colonies' would be nobody's.
            int found = -1;
            foreach (MechanicalNode other in node.Graph.Nodes)
            {
                int? owner = PowerOwnerOf(other);
                if (owner == null) continue;
                if (found >= 0 && found != owner.Value) return -1;
                found = owner.Value;
            }
            return found;
        }
    }

    /*
     * 9/29/2026 (Timberborn 1.1.2.4): a node joins every network whose connection faces its own, as it enters the finished
     * state (MechanicalGraphManager.AddNode):
        _mechanicalGraphFactory.Create().AddNode(mechanicalNode);
        HashSet<MechanicalGraph> hashSet = new HashSet<MechanicalGraph> { mechanicalNode.Graph };
        foreach (Transput current in mechanicalNode.Transputs)
        {
            Transput facingTransput = _transputMap.GetFacingTransput(current);
            if (facingTransput != null && facingTransput.IsFinished)
            {
                MechanicalGraph graph = facingTransput.ParentNode.Graph;
                if (graph != null) { current.Connect(facingTransput); facingTransput.Connect(current); hashSet.Add(graph); }
            }
        }
        if (hashSet.Count > 1) _mechanicalGraphFactory.Join(hashSet);
     * A network split later (MechanicalGraphReorganizer) follows only the connections made here.
     */
    // Separate colonies: two colonies' nodes are never connected, so their networks never join, whatever the placement
    // check let through (two placements at once, a road that changed a building's colony). A Power Export Facility's half
    // is nobody's and connects to anything. Shared games and each colony's own nodes are the game's.
    [HarmonyPatch(typeof(MechanicalGraphManager), nameof(MechanicalGraphManager.AddNode))]
    static class PowerNetworkSeparationPatcher
    {
        static bool Prefix(MechanicalGraphManager __instance, MechanicalNode mechanicalNode)
        {
            if (!ColonyModeService.IsSeparateColonies) return true;
            __instance._mechanicalGraphFactory.Create().AddNode(mechanicalNode);
            var graphs = new HashSet<MechanicalGraph> { mechanicalNode.Graph };
            int? mine = PowerExports.PowerOwnerOf(mechanicalNode);
            foreach (Transput current in mechanicalNode.Transputs)
            {
                Transput facing = __instance._transputMap.GetFacingTransput(current);
                if (facing == null || !facing.IsFinished) continue;
                MechanicalGraph graph = facing.ParentNode.Graph;
                if (graph == null) continue;
                if (!ColonyPowerRule.MayJoin(mine, PowerExports.PowerOwnerOf(facing.ParentNode))) continue;
                current.Connect(facing);
                facing.Connect(current);
                graphs.Add(graph);
            }
            if (graphs.Count > 1) __instance._mechanicalGraphFactory.Join(graphs);
            return false;
        }
    }
}
