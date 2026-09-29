using System;
using System.Collections.Generic;
using Timberborn.BlockObjectTools;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.InputSystem;
using Timberborn.MapStateSystem;
using Timberborn.MechanicalSystem;
using Timberborn.Rendering;
using Timberborn.SingletonSystem;
using Timberborn.TerrainSystem;
using Timberborn.ToolSystem;
using UnityEngine;

namespace BeaverBuddies.Colonies
{
    /// <summary>
    /// Draws every colony's power networks on the map in its colour, as the road view (Ctrl+L) draws roads: every shaft,
    /// gearbox, generator, battery and powered building, built or being built, as the same filled square, in the same
    /// strong colony colours. A Power Export Facility's half takes the colour of the network it joins (else its road's).
    /// Two colonies' power networks never join except through a Power Export Facility, so this shows whose power runs
    /// where. Shown at any time with its key (Ctrl+P), while the Power window is open, and while a power piece (a shaft,
    /// gearbox, generator, battery or the facility) is in hand, when the road view makes way for it. Display only: it
    /// reads the placed buildings and their owners and never touches the simulation. The squares are built into a mesh
    /// when something changes, not drawn again every frame.
    /// </summary>
    public class ColonyPowerOverlay : ILoadableSingleton, IPostLoadableSingleton, IUpdatableSingleton, IInputProcessor
    {
        public const string ToggleKeyBindingId = "BeaverBuddies.KeyBind.ToggleColonyPower";

        private const float RedrawInterval = 0.5f;
        private const float RefreshInterval = 3f;
        private const float AbovePaths = 0.12f;
        private static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");

        private readonly AreaTileDrawerFactory _areaTileDrawerFactory;
        private readonly MarkerDrawerFactory _markerDrawerFactory;
        private readonly MapSize _mapSize;
        private readonly InputService _inputService;
        private readonly ToolService _toolService;
        private readonly EventBus _eventBus;
        private readonly EntityRegistry _entityRegistry;

        private GameObject root;
        private readonly List<AreaTileDrawer> drawers = new List<AreaTileDrawer>();
        private bool failed, toggledOn, shown, dirty = true;
        private float nextRedraw, nextRefresh;

        public ColonyPowerOverlay(AreaTileDrawerFactory areaTileDrawerFactory, MarkerDrawerFactory markerDrawerFactory, MapSize mapSize,
            InputService inputService, ToolService toolService, EventBus eventBus, EntityRegistry entityRegistry)
        {
            _areaTileDrawerFactory = areaTileDrawerFactory;
            _markerDrawerFactory = markerDrawerFactory;
            _mapSize = mapSize;
            _inputService = inputService;
            _toolService = toolService;
            _eventBus = eventBus;
            _entityRegistry = entityRegistry;
        }

        public void Load() => _eventBus.Register(this);

        public void PostLoad() => _inputService.AddInputProcessor(this);

        [OnEvent]
        public void OnEntityInitialized(EntityInitializedEvent entityInitializedEvent)
        {
            if (entityInitializedEvent.Entity.HasComponent<MechanicalNodeSpec>()) dirty = true;
        }

        [OnEvent]
        public void OnEntityDeleted(EntityDeletedEvent entityDeletedEvent)
        {
            if (entityDeletedEvent.Entity.HasComponent<MechanicalNodeSpec>()) dirty = true;
        }

        public bool ProcessInput()
        {
            if (_inputService.IsKeyDown(ToggleKeyBindingId)) toggledOn = !toggledOn;
            return false;
        }

        /// <summary>
        /// A power piece is in hand (a shaft, gearbox, generator, battery, or the Power Export Facility: a mechanical
        /// building that is not a consumer): where power runs matters more than where roads run for it.
        /// </summary>
        public static bool IsPowerPiece(ITool tool)
        {
            if (!(tool is BlockObjectTool blockObjectTool) || blockObjectTool.Template == null) return false;
            if (blockObjectTool.Template.HasSpec<MultiColonyPowerExportSpec>()) return true;
            MechanicalNodeSpec node = blockObjectTool.Template.GetSpec<MechanicalNodeSpec>();
            return node != null && node.PowerInput <= 0;
        }

        private static readonly ColonyProfiler.Spot PowerDrawing = ColonyProfiler.Declare("Power overlay drawing");

        public void UpdateSingleton()
        {
            try
            {
                bool wanted = ColonyModeService.IsSeparateColonies
                    && (toggledOn || PowerOverviewPanel.Instance?.IsOpen == true || IsPowerPiece(_toolService.ActiveTool));
                if (!wanted)
                {
                    if (shown) root?.SetActive(false);
                    shown = false;
                    return;
                }
                if (!EnsureDrawers()) return;
                if (!shown)
                {
                    root.SetActive(true);
                    shown = true;
                    dirty = true;
                    nextRedraw = 0;
                }
                float now = Time.unscaledTime;
                if (!(dirty && now >= nextRedraw) && now < nextRefresh) return;
                nextRedraw = now + RedrawInterval;
                nextRefresh = now + RefreshInterval;
                dirty = false;
                long started = ColonyProfiler.Start();
                Redraw();
                ColonyProfiler.Stop(PowerDrawing, started);
            }
            catch (Exception error)
            {
                // Seeing the power networks is a help, not a requirement: previews and refusals still explain themselves.
                if (!failed) Plugin.LogError("[Colony] Could not draw colony power: " + error);
                failed = true;
                root?.SetActive(false);
            }
        }

        private bool EnsureDrawers()
        {
            if (root != null) return true;
            if (failed) return false;
            root = new GameObject("BeaverBuddies_ColonyPower");
            for (int slot = 0; slot < ColonySlotTable.MaxSlots; slot++)
            {
                var holder = new GameObject("Colony" + (slot + 1));
                holder.transform.parent = root.transform;
                drawers.Add(FilledDrawer(ColorOf(slot), holder) ?? _areaTileDrawerFactory.Create(ColorOf(slot), holder));
            }
            root.SetActive(false);
            return true;
        }

        /// <summary>The road view's filled squares (the game's tile marker, a mesh built once per change).</summary>
        private AreaTileDrawer FilledDrawer(Color color, GameObject holder)
        {
            try
            {
                var spec = _markerDrawerFactory._markerDrawerFactorySpec;
                Mesh mesh = spec?.TileMesh.Asset;
                Material source = spec?.TileMaterial.Asset;
                if (mesh == null || source == null) return null;
                var material = new Material(source);
                material.SetColor(ColorProperty, color);
                Vector2Int tileCount = WorldTiling.TileCount2D(_mapSize.TerrainSize.x, _mapSize.TerrainSize.y);
                var parent = new GameObject(holder.name + "Tiles");
                parent.transform.parent = holder.transform;
                parent.transform.localPosition = new Vector3(0f, AbovePaths, 0f);
                var drawer = new AreaTileDrawer(mesh, material, tileCount, parent);
                foreach (Transform part in parent.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = Layers.UILayer;
                return drawer;
            }
            catch (Exception error)
            {
                Plugin.LogWarning("[Colony] Colony power is drawn as outlines: " + error.Message);
                return null;
            }
        }

        private void Redraw()
        {
            var tiles = new List<Vector3Int>[drawers.Count];
            for (int slot = 0; slot < tiles.Length; slot++) tiles[slot] = new List<Vector3Int>();
            foreach (EntityComponent entity in _entityRegistry.Entities)
            {
                if (!entity.HasComponent<MechanicalNodeSpec>()) continue;
                BlockObject blockObject = entity.GetComponent<BlockObject>();
                if (!blockObject || blockObject.IsPreview || !blockObject.Positioned) continue;
                int? owner = OwnerOf(entity);
                if (owner == null || owner.Value < 0 || owner.Value >= tiles.Length) continue;
                // Its bottom level only: a building would otherwise get squares floating inside it.
                int bottom = int.MaxValue;
                foreach (Vector3Int tile in blockObject.PositionedBlocks.GetAllCoordinates()) bottom = Math.Min(bottom, tile.z);
                foreach (Vector3Int tile in blockObject.PositionedBlocks.GetAllCoordinates())
                {
                    if (tile.z == bottom) tiles[owner.Value].Add(tile);
                }
            }
            for (int slot = 0; slot < drawers.Count; slot++) drawers[slot].UpdateArea(tiles[slot]);
        }

        /// <summary>Whose power a piece is: a facility half's network's colony (else its road's), anything else its owner.</summary>
        private static int? OwnerOf(EntityComponent entity)
        {
            PowerExportHalf half = entity.GetComponent<PowerExportHalf>();
            if (half != null)
            {
                int network = PowerExports.NetworkColonyOf(half);
                if (network >= 0) return network;
                int road = PowerExports.ColonyOf(half);
                return road >= 0 ? road : (int?)null;
            }
            return DistrictOwner.OwnerOf(entity);
        }

        private static Color ColorOf(int slot)
        {
            Color color = slot >= 0 && slot < ColonyRoadOverlay.Palette.Roads.Length ? ColonyRoadOverlay.Palette.Roads[slot] : Color.white;
            color.a = 0.7f;
            return color;
        }
    }
}
