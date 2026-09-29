#nullable enable
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

// 1.4.0-rc31, checks against the compiled mod and the game's own UI files (StreamingAssets/Modding/UI.zip): the
// Trading Posts and colonies window (Y) is drawn with the game's classes. The window hangs in the game UI's
// "Absolute-items", so only the style sheets GameUI.uxml loads reach it (not the entity panel's): every class it names is
// listed in TradeOverviewPanel.ClassesUsed, and every listed class is defined in one of those sheets.
internal static class Rc31RuntimeChecks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Run(Assembly mod, string managedPath, Action<string, Action> test)
    {
        test("rc31: the trading posts window uses only classes of the style sheets the game UI loads", () =>
        {
            using ZipArchive ui = UiZip(managedPath);
            var sheets = Regex.Matches(Read(ui, "Views/Common/GameUI.uxml"), "Style src=\"/Assets/Resources/UI/(Views/[^\"]+\\.uss)\"")
                .Select(m => m.Groups[1].Value).ToList();
            if (sheets.Count == 0) throw new Exception("GameUI.uxml loads no style sheets");
            var defined = new HashSet<string>();
            foreach (string sheet in sheets)
                foreach (Match m in Regex.Matches(Read(ui, sheet), "\\.([A-Za-z_][A-Za-z0-9_-]*)")) defined.Add(m.Groups[1].Value);
            Type window = mod.GetType("BeaverBuddies.Colonies.TradeOverviewPanel", true)!;
            var used = (string[])(window.GetField("ClassesUsed", All)?.GetValue(null) ?? throw new Exception("TradeOverviewPanel.ClassesUsed is gone"));
            var missing = used.Where(c => !defined.Contains(c)).ToList();
            if (missing.Count > 0) throw new Exception("not in the game UI's style sheets (" + string.Join(", ", sheets) + "): " + string.Join(", ", missing));

            // Every class-like string the window's code loads (its methods, lambdas and nested types) is one it lists.
            // Unity's own classes (unity-…) are the Toggle's parts, found by name, not styled here.
            var named = Types(window).SelectMany(t => t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)))
                .Where(m => m.GetMethodBody() != null)
                .SelectMany(m => IlScan.Instructions(m)).Select(i => i.Text)
                .Where(text => text != null && Regex.IsMatch(text, "^[a-z][a-z0-9]*(?:(?:-|--|_|__)[a-z0-9]+)+$") && !text.StartsWith("unity-"))
                .Select(text => text!).Distinct().ToList();
            var unlisted = named.Where(c => !used.Contains(c)).ToList();
            if (unlisted.Count > 0) throw new Exception("the window names classes it does not list in ClassesUsed: " + string.Join(", ", unlisted));
            Console.WriteLine("      The window's classes: " + string.Join(", ", named.OrderBy(c => c, StringComparer.Ordinal)));

            // The entity panel's classes are not among them: in a window they would draw nothing.
            foreach (string panelOnly in new[] { "entity-panel__text", "entity-fragment__button--red" })
                if (defined.Contains(panelOnly)) Console.WriteLine($"      Note: {panelOnly} is now in the game UI's sheets");
                else if (used.Contains(panelOnly) || named.Contains(panelOnly)) throw new Exception("the window relies on " + panelOnly + ", which a window does not have");
        });

        test("rc31: the trading posts window is still built as the game's named box", () =>
        {
            using ZipArchive ui = UiZip(managedPath);
            string box = Read(ui, "Views/Common/NamedBoxTemplate.uxml");
            // The frame, the title badge on its edge, its text and the close button, as the window builds them.
            foreach (string classes in new[] { "sliced-border box__content-container", "capsule-header capsule-header--lower content-centered",
                "capsule-header__text", "close-button" })
                if (!box.Contains("class=\"" + classes + "\"")) throw new Exception("NamedBoxTemplate no longer has class=\"" + classes + "\"");
            // The population's well-being box, the window's model, is still one of them, with the same scroll view.
            string wellbeing = Read(ui, "Views/Game/Population/PopulationWellbeingBox.uxml");
            if (!wellbeing.Contains("NamedBoxTemplate.uxml") || !wellbeing.Contains("game-scroll-view"))
                throw new Exception("the well-being box is no longer a named box with the game's scroll view");
            // The game UI's sheets define the text colours the window relies on: its light grey and its yellow.
            string common = Read(ui, "Views/Common/CommonStyle.uss");
            foreach (string rule in new[] { "\\.game-text-small\\s*\\{[^}]*color:\\s*rgb\\(204, 204, 204\\)", "\\.game-text-big\\s*\\{[^}]*color:\\s*rgb\\(204, 204, 204\\)",
                "\\.text--yellow\\s*\\{[^}]*color:\\s*rgb\\(188, 162, 108\\)" })
                if (!Regex.IsMatch(common, rule)) throw new Exception("CommonStyle no longer matches " + rule);
            // A caption's yellow wins over its size class: text--yellow comes after the size classes in the same sheet.
            if (common.IndexOf(".text--yellow", StringComparison.Ordinal) < common.IndexOf(".game-text-title", StringComparison.Ordinal))
                throw new Exception("text--yellow now comes before the text size classes: a caption would lose its yellow");
        });
    }

    static IEnumerable<Type> Types(Type type) => new[] { type }.Concat(type.GetNestedTypes(All).SelectMany(Types));

    static ZipArchive UiZip(string managedPath)
    {
        string path = Path.GetFullPath(Path.Combine(managedPath, "..", "StreamingAssets", "Modding", "UI.zip"));
        if (!File.Exists(path)) throw new Exception("the game's UI.zip is not at " + path);
        return ZipFile.OpenRead(path);
    }

    static string Read(ZipArchive zip, string entry)
    {
        ZipArchiveEntry file = zip.GetEntry(entry) ?? throw new Exception("UI.zip has no " + entry);
        using var reader = new StreamReader(file.Open());
        return reader.ReadToEnd();
    }
}
