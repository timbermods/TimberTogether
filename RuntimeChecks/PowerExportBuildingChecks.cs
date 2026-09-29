using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

// The Power Export Facility is a building the mod adds with blueprints (BeaverBuddies/Buildings/Power): the game's
// District Crossing model and block layout as two linked halves, without the crossing's workings, with one worker, a power
// node and one power connection each. These read the built mod's blueprints next to its DLL and the game's own, and the
// game's assemblies for every member the facility's code relies on (PowerExport.cs, PowerExportService.cs).
internal static class PowerExportBuildingChecks
{
    const string Folder = "Buildings/Power/MultiColonyPowerExport";
    const string SpecName = "MultiColonyPowerExportSpec";
    static readonly string[] Factions = { "Folktails", "IronTeeth" };
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static void Run(Assembly mod, string modDirectory, string managedDirectory, Action<string, Action> test)
    {
        string zipPath = Path.GetFullPath(Path.Combine(managedDirectory, "..", "StreamingAssets", "Modding", "Blueprints.zip"));
        JsonNode GameBlueprint(string path)
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry entry = zip.GetEntry(path) ?? throw new Exception("the game has no " + path);
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return JsonNode.Parse(reader.ReadToEnd())!;
        }
        JsonNode ModBlueprint(string path)
        {
            string file = Path.Combine(modDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) throw new Exception("the built mod has no " + path);
            return JsonNode.Parse(File.ReadAllText(file))!;
        }

        foreach (string faction in Factions)
        {
            test($"Power Export Facility: the {faction} one has the game's District Crossing's model and blocks, and none of its workings", () =>
            {
                JsonNode crossing = GameBlueprint($"Buildings/DistrictManagement/DistrictCrossing/DistrictCrossing.{faction}.blueprint.json");
                JsonNode facility = ModBlueprint($"{Folder}/MultiColonyPowerExport.{faction}.blueprint.json");
                Expect(JsonNode.DeepEquals(crossing["BlockObjectSpec"], facility["BlockObjectSpec"]), "its blocks and door are the crossing's");
                Expect(JsonNode.DeepEquals(crossing["Children"], facility["Children"]), "its models and colliders are the crossing's");
                Expect(JsonNode.DeepEquals(crossing["BuildingModelSpec"], facility["BuildingModelSpec"]), "its model names are the crossing's");
                Expect(JsonNode.DeepEquals(crossing["TransformSlotInitializerSpec"], facility["TransformSlotInitializerSpec"]), "its worker's place is the crossing's");
                Expect(facility["PlaceableBlockObjectSpec"]!["Layout"]!.GetValue<string>() == crossing["PlaceableBlockObjectSpec"]!["Layout"]!.GetValue<string>(),
                    "it is laid down as two halves, as the crossing is");
                Expect(facility["PlaceableBlockObjectSpec"]!["ToolGroupId"]!.GetValue<string>() == "Power", "it sits in the Power toolbar group");
                Expect(facility["DistrictCrossingSpec"] == null && facility["DistrictObstacleSpec"] == null && facility["BlockObjectNavMeshSettingsSpec"] == null,
                    "nothing of the crossing's that would join two colonies' roads");
                Expect(facility["LinkedBuildingSpec"] != null && facility[SpecName] is JsonObject mark && mark.Count == 0, "two linked halves with the mod's mark");
                Expect(facility["LabeledEntitySpec"]!["Icon"]!.GetValue<string>() == $"{Folder}/PowerExportIcon", "its own icon");
                // The power connection's direction is one the game reads.
                Type directions = Assembly.Load("Timberborn.Coordinates").GetType("Timberborn.Coordinates.Directions3D", true)!;
                foreach (JsonNode? transput in facility["TransputProviderSpec"]!["Transputs"]!.AsArray())
                    Enum.Parse(directions, transput!["Directions"]!.GetValue<string>());
            });
        }

        test("Power Export Facility: its icons are sprites in the built mod", () =>
        {
            foreach (string icon in new[] { Path.Combine(modDirectory, Folder.Replace('/', Path.DirectorySeparatorChar), "PowerExportIcon.png"),
                Path.Combine(modDirectory, "UI", "Images", "BeaverBuddies", "square-toggle-power.png") })
            {
                Expect(File.Exists(icon) && File.ReadAllBytes(icon).Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "a PNG: " + icon);
                JsonNode meta = JsonNode.Parse(File.ReadAllText(icon + ".meta.json"))!;
                Expect(meta["isSprite"]!.GetValue<bool>(), "marked as a sprite: " + icon);
            }
        });

        test("Power Export Facility: its mark is a spec the game finds by its name, which no game assembly uses", () =>
        {
            Type spec = mod.GetType("BeaverBuddies.Colonies." + SpecName, true)!;
            Type componentSpec = Assembly.Load("Timberborn.BlueprintSystem").GetType("Timberborn.BlueprintSystem.ComponentSpec", true)!;
            Expect(componentSpec.IsAssignableFrom(spec) && !spec.IsAbstract && spec.IsPublic, "a public component spec");
            byte[] name = Encoding.UTF8.GetBytes(SpecName);
            var clashes = Directory.GetFiles(managedDirectory, "*.dll").Where(dll => Contains(File.ReadAllBytes(dll), name)).Select(Path.GetFileName).ToList();
            Expect(clashes.Count == 0, "the name also appears in " + string.Join(", ", clashes));
        });

        test("Power Export Facility: every game member its code relies on is still there", () =>
        {
            Assembly mechanical = Assembly.Load("Timberborn.MechanicalSystem");
            Type manager = mechanical.GetType("Timberborn.MechanicalSystem.MechanicalGraphManager", true)!;
            Type node = mechanical.GetType("Timberborn.MechanicalSystem.MechanicalNode", true)!;
            Type graph = mechanical.GetType("Timberborn.MechanicalSystem.MechanicalGraph", true)!;
            Type factory = mechanical.GetType("Timberborn.MechanicalSystem.MechanicalGraphFactory", true)!;
            Type transputMap = mechanical.GetType("Timberborn.MechanicalSystem.TransputMap", true)!;
            Type transput = mechanical.GetType("Timberborn.MechanicalSystem.Transput", true)!;
            // The network join the power rule guards (PowerNetworkSeparationPatcher replaces it in separate colonies).
            Expect(manager.GetMethod("AddNode", All, null, new[] { node }, null) != null, "MechanicalGraphManager.AddNode(MechanicalNode)");
            Expect(manager.GetField("_mechanicalGraphFactory", All)?.FieldType == factory, "MechanicalGraphManager._mechanicalGraphFactory");
            Expect(manager.GetField("_transputMap", All)?.FieldType == transputMap, "MechanicalGraphManager._transputMap");
            Expect(factory.GetMethod("Create", All) != null && factory.GetMethod("Join", All) != null, "MechanicalGraphFactory.Create and Join");
            Expect(graph.GetMethod("AddNode", All) != null, "MechanicalGraph.AddNode");
            Expect(transputMap.GetMethod("GetFacingTransput", All) != null && transputMap.GetMethod("GetTransputsAtCoordinates", All) != null,
                "TransputMap.GetFacingTransput and GetTransputsAtCoordinates");
            foreach (string member in new[] { "Connect", "Target", "Coordinates", "ParentNode", "ConnectedNode", "Connected", "IsFinished" })
                Expect(transput.GetMember(member, All).Length > 0, "Transput." + member);
            foreach (string member in new[] { "SetInputMultiplier", "SetOutputMultiplier", "Graph", "Transputs", "Actuals", "IsGenerator", "IsBattery" })
                Expect(node.GetMember(member, All).Length > 0, "MechanicalNode." + member);
            foreach (string member in new[] { "PowerSupply", "PowerDemand", "BatteryCharge", "BatteryCapacity", "Nodes" })
                Expect(graph.GetMember(member, All).Length > 0, "MechanicalGraph." + member);
            // The half's partner, and its worker at work.
            Type linked = Assembly.Load("Timberborn.LinkedBuildingSystem").GetType("Timberborn.LinkedBuildingSystem.LinkedBuilding", true)!;
            Expect(linked.GetField("_linked", All)?.FieldType == linked, "LinkedBuilding._linked");
            Assembly workshops = Assembly.Load("Timberborn.Workshops");
            Expect(workshops.GetType("Timberborn.Workshops.Workshop", true)!.GetProperty("CurrentlyWorking", All) != null, "Workshop.CurrentlyWorking");
            Expect(workshops.GetType("Timberborn.Workshops.WorkWorkplaceBehavior", false) != null, "WorkWorkplaceBehavior");
            Type dayNight = Assembly.Load("Timberborn.TimeSystem").GetType("Timberborn.TimeSystem.IDayNightCycle", true)!;
            Expect(dayNight.GetProperty("FixedDeltaTimeInHours", All) != null, "IDayNightCycle.FixedDeltaTimeInHours");
        });

        test("Power Export Facility: the game's own network join still connects facing connections and joins their networks", () =>
        {
            // PowerNetworkSeparationPatcher is the game's AddNode with one check added: it must still be what the game does.
            MethodInfo addNode = Assembly.Load("Timberborn.MechanicalSystem").GetType("Timberborn.MechanicalSystem.MechanicalGraphManager", true)!
                .GetMethod("AddNode", All)!;
            byte[] il = addNode.GetMethodBody()!.GetILAsByteArray()!;
            var called = new HashSet<string>();
            Module module = addNode.Module;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;
                try { called.Add(module.ResolveMethod(BitConverter.ToInt32(il, i + 1))!.Name); } catch { }
            }
            foreach (string name in new[] { "Create", "AddNode", "GetFacingTransput", "get_IsFinished", "Connect", "Join" })
                Expect(called.Contains(name), "the game's AddNode no longer calls " + name + " (it calls " + string.Join(", ", called.OrderBy(n => n)) + ")");
        });
    }

    static void Expect(bool condition, string what)
    {
        if (!condition) throw new Exception("wrong: " + what);
    }

    static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }
}
