using BeaverBuddies.Colonies;
using System.Text.Json;

/// <summary>
/// 1.4.0-rc32, the Power Export Facility (design/POWER-EXPORT-PLAN.md): the link rules, the flow arithmetic and the
/// power rule, headless; the building, its keys and its wiring read from the source.
/// </summary>
static class PowerExportChecks
{
    static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }

    static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "BeaverBuddies.sln"))) root = Path.GetDirectoryName(root)!;
        Check(root != null, "could not find the repository root");
        return root!;
    }

    static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    static JsonElement Json(params string[] parts) => JsonDocument.Parse(Source(parts).TrimStart('\uFEFF')).RootElement;

    static PowerExportMath.Network Net(int supply, int demand, int room = 0, int battery = 0, bool chargeFirst = true, bool useBatteries = false) =>
        new PowerExportMath.Network { Supply = supply, Demand = demand, BatteryRoom = room, BatteryPower = battery, ChargeFirst = chargeFirst, UseBatteries = useBatteries };

    sealed class FakePower : IColonyPowerMap
    {
        public readonly Dictionary<(ColonyCell, ColonyCell), int?> Owners = new Dictionary<(ColonyCell, ColonyCell), int?>();
        public int? PowerOwnerFacing(ColonyCell cell, ColonyCell target) => Owners.TryGetValue((cell, target), out int? owner) ? owner : null;
    }

    public static IEnumerable<(string Name, Action Run)> Tests()
    {
        yield return ("rc32: power goes one way between two colonies, one to one, and never round in a circle", () =>
        {
            var none = new List<(int, int)>();
            Check(PowerExportMath.Check(0, 1, none) == PowerLinkRefusal.None, "a first link is refused");
            Check(PowerExportMath.Check(0, 0, none) == PowerLinkRefusal.NotTwoColonies, "a colony sends to itself");
            Check(PowerExportMath.Check(-1, 1, none) == PowerLinkRefusal.NotTwoColonies, "a half with no colony sends");
            Check(PowerExportMath.Check(0, 1, new[] { (1, 0) }) == PowerLinkRefusal.PartnerSending, "both ways at once");
            Check(PowerExportMath.Check(0, 1, new[] { (0, 1) }) == PowerLinkRefusal.None, "a second facility on the same link is refused");
            Check(PowerExportMath.Check(0, 2, new[] { (0, 1) }) == PowerLinkRefusal.SendingElsewhere, "a colony sends to two colonies");
            Check(PowerExportMath.Check(2, 1, new[] { (0, 1) }) == PowerLinkRefusal.PartnerReceivingElsewhere, "a colony receives from two colonies");
            // A → B → C: B passes on to C; C → A would close the circle.
            Check(PowerExportMath.Check(1, 2, new[] { (0, 1) }) == PowerLinkRefusal.None, "a chain is refused");
            Check(PowerExportMath.Check(2, 0, new[] { (0, 1), (1, 2) }) == PowerLinkRefusal.MakesCircle, "a circle of three is let through");
            Check(PowerExportMath.Check(3, 0, new[] { (0, 1), (1, 2), (2, 3) }) == PowerLinkRefusal.MakesCircle, "a circle of four is let through");
            Check(PowerExportMath.Check(3, 0, new[] { (0, 1), (1, 2) }) == PowerLinkRefusal.None, "a colony joining the start of a chain is refused");
        });

        yield return ("rc32: the links let through each tick are the first that obey the rules, the same in any run", () =>
        {
            var admitted = PowerExportMath.Admit(new[] { (0, 1), (1, 0), (0, 2), (1, 2), (2, 0), (0, 1) });
            Check(admitted.SetEquals(new[] { (0, 1), (1, 2) }), "wrong links let through: " + string.Join(" ", admitted));
            var depth = PowerExportMath.Depths(new[] { (1, 2), (0, 1), (3, 4) });
            Check(depth[0] == 0 && depth[1] == 1 && depth[2] == 2 && depth[3] == 0 && depth[4] == 1, "wrong places along the chains");
        });

        yield return ("rc32: a colony sends only what the other can use, after its own buildings", () =>
        {
            // A makes 300 and uses 100; B makes nothing and needs 150.
            int[] flow = PowerExportMath.Flow(new[] { Net(300, 100), Net(0, 150) }, new[] { (0, 1) });
            Check(flow[0] == 150, "sent " + flow[0]);
            // B needs more than A can spare: A's own buildings keep theirs.
            flow = PowerExportMath.Flow(new[] { Net(300, 250), Net(0, 150) }, new[] { (0, 1) });
            Check(flow[0] == 50, "sent " + flow[0]);
            // Nothing to spare, or nothing needed.
            Check(PowerExportMath.Flow(new[] { Net(100, 150), Net(0, 150) }, new[] { (0, 1) })[0] == 0, "sent while short itself");
            Check(PowerExportMath.Flow(new[] { Net(300, 100), Net(200, 150) }, new[] { (0, 1) })[0] == 0, "sent what was not needed");
            // B's own batteries' room counts as what it can use.
            Check(PowerExportMath.Flow(new[] { Net(300, 100), Net(0, 50, room: 1000) }, new[] { (0, 1) })[0] == 200, "B's batteries got nothing");
        });

        yield return ("rc32: charge my batteries first, and use my batteries", () =>
        {
            // Charging first: A's batteries take its spare power before anything is sent.
            Check(PowerExportMath.Flow(new[] { Net(300, 100, room: 150), Net(0, 150) }, new[] { (0, 1) })[0] == 50, "batteries did not charge first");
            Check(PowerExportMath.Flow(new[] { Net(300, 100, room: 1000), Net(0, 150) }, new[] { (0, 1) })[0] == 0, "sent before the batteries were full");
            // Not first: the other colony first, A's batteries take what is left.
            Check(PowerExportMath.Flow(new[] { Net(300, 100, room: 1000, chargeFirst: false), Net(0, 150) }, new[] { (0, 1) })[0] == 150, "the other colony waited for the batteries");
            // Stored power is sent only with Use my batteries, and only what the batteries give beyond A's own shortfall.
            Check(PowerExportMath.Flow(new[] { Net(100, 100, battery: 80), Net(0, 150) }, new[] { (0, 1) })[0] == 0, "stored power was sent without leave");
            Check(PowerExportMath.Flow(new[] { Net(100, 100, battery: 80, useBatteries: true), Net(0, 150) }, new[] { (0, 1) })[0] == 80, "stored power was not sent");
            Check(PowerExportMath.Flow(new[] { Net(50, 100, battery: 80, useBatteries: true), Net(0, 150) }, new[] { (0, 1) })[0] == 30, "A's own buildings lost power to the batteries' sending");
            Check(PowerExportMath.Flow(new[] { Net(300, 100, battery: 500, useBatteries: true), Net(0, 150) }, new[] { (0, 1) })[0] == 150, "more than was needed was sent");
            // Stored power feeds the other colony's buildings, never its batteries; spare power still fills them.
            Check(PowerExportMath.Flow(new[] { Net(100, 100, battery: 500, useBatteries: true), Net(0, 0, room: 1000) }, new[] { (0, 1) })[0] == 0, "one colony's batteries were emptied into another's");
            Check(PowerExportMath.Flow(new[] { Net(160, 100, battery: 500, useBatteries: true), Net(0, 40, room: 1000) }, new[] { (0, 1) })[0] == 60, "spare power did not fill their batteries");
            Check(PowerExportMath.Flow(new[] { Net(100, 100, battery: 500, useBatteries: true), Net(0, 30), Net(0, 50, room: 1000) }, new[] { (0, 1), (1, 2) })[0] == 80,
                "stored power did not reach the buildings down the chain");
            // With both on, the other colony comes before these batteries (the panel says so).
            Check(PowerExportMath.Flow(new[] { Net(300, 100, room: 50, useBatteries: true), Net(0, 150) }, new[] { (0, 1) })[0] == 150, "both boxes on: the batteries charged first");
        });

        yield return ("rc32: B passes A's leftover power on to C, and never makes power", () =>
        {
            // A spares 200; B needs 50 of it; C needs 100.
            int[] flow = PowerExportMath.Flow(new[] { Net(300, 100), Net(0, 50), Net(0, 100) }, new[] { (0, 1), (1, 2) });
            Check(flow[0] == 150 && flow[1] == 100, $"sent {flow[0]} then {flow[1]}");
            // A spares 80: B keeps 50 for itself and passes 30.
            flow = PowerExportMath.Flow(new[] { Net(180, 100), Net(0, 50), Net(0, 100) }, new[] { (0, 1), (1, 2) });
            Check(flow[0] == 80 && flow[1] == 30, $"sent {flow[0]} then {flow[1]}");
            // Two of A's networks feeding one of B's share what it can use.
            flow = PowerExportMath.Flow(new[] { Net(300, 0), Net(300, 0), Net(0, 200) }, new[] { (0, 2), (1, 2) });
            Check(flow[0] + flow[1] == 200, "B was sent more than it could use");
            // An edge backwards along the order gets nothing (the caller orders networks along their chains).
            Check(PowerExportMath.Flow(new[] { Net(0, 100), Net(300, 0) }, new[] { (1, 0) })[0] == 0, "a backwards edge carried power");
            // Over many sizes: nothing crosses beyond what the sender has, nor beyond what the receivers can use.
            var random = new Random(32);
            for (int run = 0; run < 2000; run++)
            {
                var nets = Enumerable.Range(0, 3).Select(_ => Net(random.Next(0, 500), random.Next(0, 500), random.Next(0, 3) == 0 ? random.Next(0, 300) : 0,
                    random.Next(0, 300), random.Next(2) == 0, random.Next(2) == 0)).ToArray();
                flow = PowerExportMath.Flow(nets, new[] { (0, 1), (1, 2) });
                long have0 = nets[0].UseBatteries ? Math.Max(0, nets[0].Supply - nets[0].Demand) + nets[0].BatteryPower : Math.Max(0, nets[0].Supply - nets[0].Demand);
                Check(flow[0] >= 0 && flow[1] >= 0 && flow[0] <= have0, "the first network sent more than it had");
                long have1 = nets[1].Supply + flow[0] - nets[1].Demand + (nets[1].UseBatteries ? nets[1].BatteryPower : 0);
                Check(flow[1] <= Math.Max(0, have1), "the middle network passed on more than it had");
                Check(flow[1] <= Math.Max(0, nets[2].Demand - nets[2].Supply) + nets[2].BatteryRoom, "the last network got more than it could use");
            }
        });

        yield return ("rc32: the power rule refuses another colony's power connection, as the road rule refuses its roads", () =>
        {
            var map = new FakePower();
            var here = new ColonyCell(5, 5, 1);
            var there = here.Step(1, 0);
            var transputs = new[] { new ColonyTransput(here, there) };
            Check(ColonyPowerRule.Conflict(0, transputs, map, out _) == ColonyRefusal.None, "nothing there was refused");
            map.Owners[(here, there)] = 0;
            Check(ColonyPowerRule.Conflict(0, transputs, map, out _) == ColonyRefusal.None, "the colony's own power was refused");
            map.Owners[(here, there)] = 1;
            Check(ColonyPowerRule.Conflict(0, transputs, map, out string detail) == ColonyRefusal.TouchesOtherPower && detail != null, "another colony's power was let through");
            Check(ColonyPowerRule.MayJoin(null, 1) && ColonyPowerRule.MayJoin(1, null) && ColonyPowerRule.MayJoin(2, 2) && !ColonyPowerRule.MayJoin(0, 1),
                "nobody's joins anything; two colonies never");
        });

        yield return ("rc32: the Power Export Facility's blueprints: two linked halves, one worker, one connection, its cost", () =>
        {
            foreach (string faction in new[] { "Folktails", "IronTeeth" })
            {
                JsonElement b = Json("BeaverBuddies", "Buildings", "Power", "MultiColonyPowerExport", $"MultiColonyPowerExport.{faction}.blueprint.json");
                JsonElement building = b.GetProperty("BuildingSpec");
                var cost = building.GetProperty("BuildingCost").EnumerateArray().ToDictionary(c => c.GetProperty("Id").GetString()!, c => c.GetProperty("Amount").GetInt32());
                Check(cost.Count == 3 && cost["Gear"] == 10 && cost["Plank"] == 10 && cost["Log"] == 10, faction + ": wrong cost (10 of each a half, 20 for the facility)");
                Check(building.GetProperty("ScienceCost").GetInt32() == 200, faction + ": wrong science cost");
                Check(b.GetProperty("TemplateSpec").GetProperty("TemplateName").GetString() == $"MultiColonyPowerExport.{faction}", faction + ": wrong template name");
                Check(b.GetProperty("WorkplaceSpec").GetProperty("MaxWorkers").GetInt32() == 1 && b.GetProperty("WorkplaceSpec").GetProperty("DefaultWorkers").GetInt32() == 1, faction + ": not one worker");
                foreach (string spec in new[] { "LinkedBuildingSpec", "MultiColonyPowerExportSpec", "WorkshopSpec", "EnterableSpec" })
                    Check(b.TryGetProperty(spec, out _), $"{faction}: no {spec}");
                foreach (string spec in new[] { "DistrictCrossingSpec", "MultiColonyTradingPostSpec", "BlockObjectNavMeshSettingsSpec", "MechanicalBuildingSpec" })
                    Check(!b.TryGetProperty(spec, out _), $"{faction}: {spec} (the halves must not join roads, nor switch power with workers)");
                JsonElement node = b.GetProperty("MechanicalNodeSpec");
                Check(node.GetProperty("PowerInput").GetInt32() == 1 && node.GetProperty("PowerOutput").GetInt32() == 1, faction + ": the node is not 1 hp each way");
                var transputs = b.GetProperty("TransputProviderSpec").GetProperty("Transputs").EnumerateArray().ToList();
                Check(transputs.Count == 1 && !transputs[0].GetProperty("Directions").GetString()!.Contains(','), faction + ": a half has more than one power connection");
                Check(b.GetProperty("PlaceableBlockObjectSpec").GetProperty("Layout").GetString() == "Half"
                    && b.GetProperty("PlaceableBlockObjectSpec").GetProperty("ToolGroupId").GetString() == "Power", faction + ": not placed as halves in the Power tab");
                Check(Source("BeaverBuddies", "TemplateCollections", $"TemplateCollection.Buildings.{faction}.blueprint.json")
                    .Contains($"Buildings/Power/MultiColonyPowerExport/MultiColonyPowerExport.{faction}.blueprint"), faction + ": not on the toolbar");
            }
            Check(File.Exists(Path.Combine(Root(), "BeaverBuddies", "Buildings", "Power", "MultiColonyPowerExport", "PowerExportIcon.png")), "no icon");
        });

        yield return ("rc32: Ctrl+P shows colonies' power and H opens the Power window, keys the game leaves free", () =>
        {
            JsonElement power = Json("BeaverBuddies", "KeyBindings", "BeaverBuddies.KeyBind.ToggleColonyPower.blueprint.json").GetProperty("PrimaryInputBindingSpec");
            Check(power.GetProperty("Path").GetString() == "/Keyboard/p" && power.GetProperty("InputModifiers").GetString() == "Ctrl", "the power view is not Ctrl+P");
            JsonElement window = Json("BeaverBuddies", "KeyBindings", "BeaverBuddies.KeyBind.PowerOverview.blueprint.json").GetProperty("PrimaryInputBindingSpec");
            Check(window.GetProperty("Path").GetString() == "/Keyboard/h" && window.GetProperty("InputModifiers").GetString() == "None", "the Power window is not H");
            // No other binding of the mod's on either key.
            foreach (string file in Directory.GetFiles(Path.Combine(Root(), "BeaverBuddies", "KeyBindings"), "*.json"))
            {
                if (file.Contains("ToggleColonyPower") || file.Contains("PowerOverview")) continue;
                // A binding with no key of its own (the player sets one) takes none.
                if (!JsonDocument.Parse(File.ReadAllText(file).TrimStart('\uFEFF')).RootElement.TryGetProperty("PrimaryInputBindingSpec", out JsonElement other)) continue;
                string path = other.GetProperty("Path").GetString()!, modifiers = other.GetProperty("InputModifiers").GetString()!;
                Check(!(path == "/Keyboard/p" && modifiers == "Ctrl") && !(path == "/Keyboard/h" && modifiers == "None"), Path.GetFileName(file) + " takes a power key");
            }
            Check(Source("BeaverBuddies", "Colonies", "ColonyPowerOverlay.cs").Contains("\"BeaverBuddies.KeyBind.ToggleColonyPower\""), "the overlay listens for another key");
            Check(Source("BeaverBuddies", "Colonies", "PowerOverviewPanel.cs").Contains("\"BeaverBuddies.KeyBind.PowerOverview\""), "the window listens for another key");
            // The road view makes way while a power piece is in hand.
            Check(Source("BeaverBuddies", "Colonies", "ColonyRoadOverlay.cs").Contains("!ColonyPowerOverlay.IsPowerPiece(_toolService.ActiveTool)"), "roads show over a power piece");
        });

        yield return ("rc32: two colonies' power networks never join, and the facility is wired in like the Trading Post", () =>
        {
            string export = Source("BeaverBuddies", "Colonies", "PowerExport.cs");
            Check(export.Contains("[HarmonyPatch(typeof(MechanicalGraphManager), nameof(MechanicalGraphManager.AddNode))]"), "the network join is not guarded");
            Check(export.Contains("if (!ColonyPowerRule.MayJoin(mine, theirs)) continue;"), "two colonies' nodes connect");
            Check(export.Contains("if (mine == null && theirs != null) mine = theirs;"), "a node nobody owns can bridge two colonies' power");
            Check(Source("BeaverBuddies", "Colonies", "PowerExportService.cs").Contains("if (!half.Node.Active || !partner.Node.Active || !Staffed(half) || !Staffed(partner))"),
                "a paused half still moves power");
            Check(export.Contains("if (!ColonyModeService.IsSeparateColonies) return true;"), "a shared game's power is changed");
            Check(export.Contains("DistrictOwner.OwnerOf(node, useConstructionDistrict: false)"), "the simulation asks the construction district");
            string configurator = Source("BeaverBuddies", "Colonies", "ColonyConfigurator.cs");
            foreach (string bind in new[] { "Bind<PowerExportService>().AsSingleton();", "Bind<PowerExportFragment>().AsSingleton();", "Bind<PowerOverviewPanel>().AsSingleton();",
                "Bind<ColonyPowerOverlay>().AsSingleton();", "AddDecorator<MultiColonyPowerExportSpec, PowerExportHalf>();", "AddDecorator<MultiColonyPowerExportSpec, WorkWorkplaceBehavior>();",
                "builder.AddBottomFragment(_powerExportFragment);" })
                Check(configurator.Contains(bind), "not bound: " + bind);
            string world = Source("BeaverBuddies", "Colonies", "ColonyGameWorld.cs");
            Check(world.Contains("PowerExports.IsFacilityBuilding(entity)) continue;"), "a facility's doors take one colony's road");
            Check(world.Contains("ColonyPowerRule.Conflict(slot, TransputsOf("), "the host does not judge power connections");
            Check(Source("BeaverBuddies", "Colonies", "ColonyPlacementValidator.cs").Contains("ColonyPowerRule.Conflict(slot, ColonyGameWorld.TransputsOf("), "previews do not show the power rule");
            Check(Source("BeaverBuddies", "Colonies", "ColonyRulesService.cs").Contains("service.world.IsMeetingTemplate(placed.prefabName)) halves.Add(placed);"), "a facility's halves are judged apart");
            Check(Source("BeaverBuddies", "Colonies", "TradingPostToolDisabler.cs").Contains("HasSpec<MultiColonyPowerExportSpec>()"), "a shared game offers the facility");
            Check(Source("BeaverBuddies", "Colonies", "ColonyStamps.cs").Contains("PowerExports.IsFacilityBuilding(entity)) return null;"), "a facility half is stamped with its placer");
            string events = Source("BeaverBuddies", "Colonies", "PowerExportEvents.cs");
            Check(events.Contains("public override ColonyScope GetColonyScope() => ColonyScope.Entities(entityID);"), "a setting is not judged by its half's owner");
            Check(events.Contains("PowerExportMath.Check(colony, partnerColony,"), "sending is not checked as it is played");
        });

        yield return ("rc32: every text the Power Export Facility shows is in the English strings", () =>
        {
            string csv = Source("BeaverBuddies", "Localizations", "enUS_BeaverBuddie.csv");
            var have = new HashSet<string>(csv.Split('\n').Select(line => line.Split(',')[0]));
            var used = new HashSet<string>();
            foreach (string file in new[] { "PowerExport.cs", "PowerExportEvents.cs", "PowerExportFragment.cs", "PowerOverviewPanel.cs", "PowerExportService.cs", "ColonyPowerOverlay.cs", "ColonyRulesService.cs" })
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Source("BeaverBuddies", "Colonies", file), "\"(BeaverBuddies\\.(?:Colony\\.Power|Colony\\.Refused\\.TouchesOtherPower)[A-Za-z.]*)\""))
                    used.Add(m.Groups[1].Value);
            foreach (string key in new[] { "BeaverBuddies.Building.PowerExport.DisplayName", "BeaverBuddies.Building.PowerExport.Description",
                "BeaverBuddies.Building.PowerExport.FlavorDescription", "BeaverBuddies.KeyBindings.ToggleColonyPower", "BeaverBuddies.KeyBindings.PowerOverview" })
                used.Add(key);
            Check(used.Count > 40, "too few texts found: " + used.Count);
            var missing = used.Where(key => !have.Contains(key)).ToList();
            Check(missing.Count == 0, "missing: " + string.Join(", ", missing));
            Check(csv.Contains("BeaverBuddies.Building.PowerExport.DisplayName,\"Power Export Facility\""), "the building's name is not Power Export Facility");
        });
    }
}
