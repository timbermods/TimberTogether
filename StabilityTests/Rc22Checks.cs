/// <summary>
/// 1.4.0-rc22, from a playtest: an accepted offer chimes, a long name pausing leaves the connection panel's size alone,
/// the colonies window opens with Y, each other player's action sounds can be muted from the panel, a steward's message
/// has its Run this colony button, the ledger has Clear, an offer's messages stay until answered or closed and say the
/// whole exchange, more than a round carries is split into rounds (ColonyChecks), the colonies window shows another
/// colony's goods, and the trade messages keep clear of the connection panel, which comes to the front when pressed.
/// Checks that need no game: they read the source.
/// </summary>
static class Rc22Checks
{
    static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }

    static string Root()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "BeaverBuddies.sln"))) root = Path.GetDirectoryName(root)!;
        Check(root != null, "could not find the repository root");
        return root!;
    }

    static string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    static string Body(string text, string signature)
    {
        int start = text.IndexOf(signature, StringComparison.Ordinal);
        Check(start >= 0, "not found: " + signature);
        int open = text.IndexOf('{', start), depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(start, i - start + 1);
        }
        throw new Exception("unbalanced braces after " + signature);
    }

    static string Csv(string key)
    {
        string csv = Source("BeaverBuddies", "Localizations", "enUS_BeaverBuddie.csv");
        int at = csv.IndexOf("\n" + key + ",\"", StringComparison.Ordinal);
        Check(at >= 0, key + " is gone");
        int start = at + key.Length + 3;
        return csv.Substring(start, csv.IndexOf("\",\"", start, StringComparison.Ordinal) - start);
    }

    public static IEnumerable<(string Name, Action Run)> Tests()
    {
        yield return ("rc22: the colony that offered hears a chime when its offer is accepted, and the offer's messages say the whole exchange", () =>
        {
            string exchange = Source("BeaverBuddies", "Colonies", "TradingPostExchange.cs");
            string accept = Body(exchange, "public void Accept(DistrictCrossing half,");
            Check(accept.Contains("\"BeaverBuddies.Colony.Trade.Notice.AcceptedRounds\"") && accept.Contains("chime: true"), "an accepted offer no longer chimes, or no longer says every round");
            Check(Body(exchange, "private void Tell(").Contains("if (chime) TradeNotices.Instance?.ChimeSoon();"), "Tell no longer chimes when asked");
            Check(Body(exchange, "public void Propose(DistrictCrossing half,").Contains("\"BeaverBuddies.Colony.Trade.Notice.ProposedRounds\""), "an offer's message says one round only");
            Check(Body(exchange, "private string Whole(").Contains("Amount(giveAmount * rounds, giveGood)"), "the whole exchange is not every round's goods");
            Check(Csv("BeaverBuddies.Colony.Trade.Notice.ProposedRounds").Contains("in all, over {3} rounds")
                && Csv("BeaverBuddies.Colony.Trade.Notice.AcceptedRounds").Contains("in all, over {3} rounds"), "the rounds' messages don't say the whole");
            // The chime is played on the next frame, never inside the action.
            string notices = Source("BeaverBuddies", "Colonies", "TradeNotices.cs");
            Check(Body(notices, "public void UpdateSingleton()").Contains("if (posted.Count == 0 && !chimePending) return;"), "a chime asked for is never played");
        });

        yield return ("rc22: a long name pausing the game never widens or wraps the connection panel's header", () =>
        {
            string view = Source("BeaverBuddies", "Panel", "ConnectionPanelView.cs");
            foreach (string rule in new[] { "pausedTag.style.flexBasis = 0;", "pausedTag.style.minWidth = 0;", "pausedTag.style.whiteSpace = WhiteSpace.NoWrap;",
                "pausedTag.style.textOverflow = TextOverflow.Ellipsis;", "pausedTag.style.overflow = Overflow.Hidden;" })
                Check(view.Contains(rule), "the paused tag lost " + rule);
            Check(view.Contains("pausedTag.tooltip = model.PausedText ?? \"\";"), "the full paused line is no longer the tooltip");
        });

        yield return ("rc22: the colonies window opens with Y by default, a key the player can change, and the texts say Y", () =>
        {
            string binding = Source("BeaverBuddies", "KeyBindings", "BeaverBuddies.KeyBind.TradeOverview.blueprint.json");
            Check(binding.Contains("\"Path\": \"/Keyboard/y\"") && binding.Contains("\"InputModifiers\": \"None\"") && binding.Contains("\"Unchangeable\": false"),
                "the colonies window's key is not a changeable Y");
            string csv = Source("BeaverBuddies", "Localizations", "enUS_BeaverBuddie.csv");
            Check(!csv.Contains("Ctrl+T"), "a text still says Ctrl+T");
            // The README and the site follow at the release (they describe the released build).
            Check(!Source("TWO-COLONIES.md").Contains("Ctrl+T"), "TWO-COLONIES.md still says Ctrl+T");
        });

        yield return ("rc22: another player's action sounds can be muted from their row in the connection panel", () =>
        {
            string replay = Source("BeaverBuddies", "ReplayService.cs");
            Check(replay.Contains("int soundsOf = BeaverBuddies.Activity.RemoteSounds.Enter(replayEvent.player);")
                && replay.Contains("try { replayEvent.Replay(this); }\n                finally { BeaverBuddies.Activity.RemoteSounds.Exit(soundsOf); }"),
                "an action's sounds are no longer marked with its player");
            string sounds = Source("BeaverBuddies", "Activity", "RemoteSounds.cs");
            Check(sounds.Contains("playing != ColonySession.LocalPlayer"), "a player could mute their own actions");
            Check(Body(sounds, "internal static void Install(Harmony harmony)").Contains("catch (Exception error)")
                && !Body(sounds, "internal static void Install(Harmony harmony)").Contains("FailedPatches"), "muting is a reason to refuse co-op");
            string plugin = Source("BeaverBuddies", "Plugin.cs");
            Check(plugin.Contains("BeaverBuddies.Activity.RemoteSounds.Install(harmony);") && !plugin.Contains("Install(nameof(RemoteSounds)"), "the mute is not installed, or refuses co-op when it fails");
            string view = Source("BeaverBuddies", "Panel", "ConnectionPanelView.cs");
            Check(view.Contains("line.Add(row.IsYou ? SoundSpacer() : SoundButton(row));"), "the rows lost their sound button, or no longer line up");
            string service = Source("BeaverBuddies", "Panel", "ConnectionPanelService.cs");
            Check(Body(service, "void OnSoundClicked(PanelRow row)").Contains("RemoteSounds.SetMuted(row.Id, !RemoteSounds.IsMuted(row.Id));"), "the button no longer toggles");
            Check(Body(service, "void StartChatSession(TimberNetBase net)").Contains("RemoteSounds.ClearMuted();"), "a new session keeps the last one's mutes");
            foreach (string icon in new[] { "sound-on.png", "sound-off.png" })
                Check(File.Exists(Path.Combine(Root(), "BeaverBuddies", "UI", "Images", "BeaverBuddies", icon))
                    && File.Exists(Path.Combine(Root(), "BeaverBuddies", "UI", "Images", "BeaverBuddies", icon + ".meta.json")), icon + " is missing");
        });

        yield return ("rc22: a player asked to look after a colony gets a message with Run this colony on it", () =>
        {
            string stewards = Source("BeaverBuddies", "Colonies", "ColonyStewards.cs");
            Check(Body(stewards, "public void Grant(int slot,").Contains("AskToRun(playerId, slot);"), "the steward is told without the button");
            string ask = Body(stewards, "private void AskToRun(string playerId, int slot)");
            Check(ask.Contains("TradeNotices.Instance?.PostWithAction(") && ask.Contains("new ActAsColonyEvent { colonySlot = slot }")
                && ask.Contains("\"BeaverBuddies.Colony.Overview.RunColony\""), "the message's button no longer runs the colony");
            string notices = Source("BeaverBuddies", "Colonies", "TradeNotices.cs");
            Check(Body(notices, "private void Show(Posted message)").Contains("Close(notice);\n                    try { action(); }"), "the button leaves its message open");
        });

        yield return ("rc22: the ledger has Clear at its top right, which empties this half's ledger on every computer", () =>
        {
            string panel = Source("BeaverBuddies", "Colonies", "TradingPostFragment.cs");
            Check(Body(panel, "private VisualElement BuildLedger()").Contains("ledgerClearButton = SmallButton(T(\"BeaverBuddies.Colony.Trade.LedgerClear\"), ClearLedger);"),
                "Clear is not the All posts button's kind");
            Check(Body(panel, "private void ClearLedger()").Contains("new LedgerClearedEvent { crossingID = halfId }"), "Clear sends no action");
            string exchange = Source("BeaverBuddies", "Colonies", "TradingPostExchange.cs");
            Check(Body(exchange, "public void ClearLedger(DistrictCrossing half, int actorSlot)").Contains("OwnerOf(half) != actorSlot"), "another colony could clear a half's ledger");
            Check(Body(exchange, "internal void ClearLedger()").Contains("Changed(\"exchange-ledger-clear\");"), "clearing the ledger is not in the colony digest");
            Check(Source("BeaverBuddies", "Colonies", "ColonyRulesService.cs").Contains("replayEvent is LedgerClearedEvent"), "a player not seated yet may clear a ledger");
            Check(Csv("BeaverBuddies.Colony.Trade.LedgerClear") == "Clear", "the button's label changed");
        });

        yield return ("rc22: the colonies window shows every good another colony has, as icons and amounts", () =>
        {
            string window = Source("BeaverBuddies", "Colonies", "TradeOverviewPanel.cs");
            Check(window.Contains("RefreshGoods(card, slot, slot != me && slot != seat"), "the goods show for this player's own colony, or not at all");
            Check(Body(window, "private void RefreshGoods(ColonyCard card, int slot, bool shown)").Contains("_items.GoodsOfColony(slot)"), "the goods are not the colony's");
            Check(Body(Source("BeaverBuddies", "Colonies", "TradeItems.cs"), "public List<KeyValuePair<string, int>> GoodsOfColony(int slot)").Contains("DistrictOwner.OwnerOfDistrict(districtCenter) == slot"),
                "the goods add up other colonies' districts");
        });

        yield return ("rc22: the trade messages keep clear of the connection panel, and the panel comes to the front when pressed", () =>
        {
            string notices = Source("BeaverBuddies", "Colonies", "TradeNotices.cs");
            Check(Body(notices, "public void UpdateSingleton()").Contains("if (shown.Count > 0) KeepClearOfPanel();"), "the messages no longer move aside");
            Check(Body(notices, "private void KeepClearOfPanel()").Contains("ConnectionPanelService.PanelRoot"), "the messages don't know where the panel is");
            Check(!notices.Contains("Time."), "a trade message is timed");
            string service = Source("BeaverBuddies", "Panel", "ConnectionPanelService.cs");
            Check(service.Contains("view.SetLifted(focused || pressedLast);") && service.Contains("view.Pressed += inside => pressedLast = inside;"),
                "pressing the panel no longer brings it to the front");
            Check(service.Contains("if (pressedLast && input.MainMouseButtonDown && !input.MouseOverUI) pressedLast = false;"), "a click on the game leaves the panel in front");
        });
    }
}
