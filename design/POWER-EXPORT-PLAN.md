# Power Export Facility — Design Plan

A handoff document, written from a design discussion with Kyler (2026-09-29), not from an implementation. Read it
fully before writing code. Anything marked **VERIFY** is a belief about Timberborn internals that was not checked
against the decompiled game: confirm it before building on it. Written against the rc31 source (the Trading Post form
without rounds).

## 1. Goal

Let one colony send spare power to another. Power only: the Power Export Facility trades nothing. Players who want to pay for
power barter for it at a Trading Post as usual.

Decisions (Kyler):

1. **One building with two halves**, like the Trading Post. Each half joins a different colony's power network.
2. **Power networks never join except through a Power Export Facility.** Always applied in separate colonies, a feature like
   Trading Posts (no setting).
3. **The sender's batteries fill first**, with a setting to send before charging them.
4. **Batteries may be drained for the partner**, when a toggle is on.
5. **Cost:** 20 Gears, 20 Planks, 20 Logs, 200 science.
6. **1-to-1:** a colony sends to at most one colony and receives from at most one. Chains are allowed: if A sends to
   B, B may pass its spare power (A's included) on to C.
7. **One direction per pair.** Between two colonies it is A → B or B → A, never both.
8. **Native UI**, as close to the game's as possible. The Trading Post panel is the model.
9. **Name:** Power Export Facility (exactly). Internal ids stay short: `MultiColonyPowerExport`, `PowerExportMath`.
10. **One worker on each side.** Each half is staffed by its own colony's beaver (or bot).
11. **Ctrl+P shows every colony's power networks**, the way Ctrl+L shows roads (Ctrl+L stays exactly as it is).
12. **A Power window**, like the Trading Posts and colonies window (Y).

## 2. The rule

**Two colonies' power networks never join, except through a Power Export Facility.** It replaces the line in
`TWO-COLONIES.md` ("two colonies' shafts that touch make one network").

- **A network's owner** is its buildings' owner: a building's district's owner, else its `ColonyStamp` (the placer).
  Shafts and gearboxes have no district, so their stamp decides. The rule keeps every network to one colony.
- **Placement** (`ColonyPowerRule`, beside `ColonyRoadRule`, pure and tested with fakes the same way): refuse a
  mechanical building if any of its transputs would meet a transput of another colony's building. A Power Export Facility's
  outer transputs are nobody's: each half takes its own colony's network. Message in the road rule's voice, e.g.
  *That would join another colony's power. Connect them through a Power Export Facility.* Judged by the host inside the
  replayed placement, like the road rule.
- **Safety net:** where the game connects transputs into a graph, never connect two colonies' nodes. Anything the
  placement check misses stays two networks. Runs on every computer from simulation state only.
- **Accepted leak** (as for buildings): a consumer A places on B's road joins B's district and is B's. A shaft A
  places is A's.

## 3. The building

- Two halves, each with one transput on its outer end. Suggested footprint 2×1×1 (one tile per half) — confirm against
  the shaft transput layout.
- **Neutral:** either colony may place it; either partner may demolish it. No owner slot.
- **Live** when each half's network belongs to a different colony. Derived, never saved (like
  `TradingPosts.JoinsTwoColonies`). Same colony on both sides: inert, status *Both sides are your colony*. It never
  acts as a shaft, so it cannot merge your own networks either.
- **Cost:** 20 Gears, 20 Planks, 20 Logs; 200 science from the placer's pool (the shared pool when science isn't
  separate). The partner needs no unlock to use its half.
- **Toolbar:** Power tab, after the batteries. Blueprints `MultiColonyPowerExport.Folktails` / `.IronTeeth` beside
  the Trading Post's, and a spec type with a mod-unique name (`MultiColonyPowerExportSpec`, for the reason given in
  `TradingPostSpec.cs`). In a mixed game both halves show the placer's faction, as the Trading Post does.
- **One worker per half**, from that half's own colony. So each half needs two connections from its colony: a road
  to its entrance (for the worker) and power at its transput. The two halves' entrances are on opposite sides and are
  nobody's, as the Trading Post's are; the facility never joins the two colonies' roads.
- **A half's road and power must be the same colony.** Otherwise the facility is inert, with the status *This half's
  road and power belong to different colonies*.
- **Power moves only while both halves are staffed**: a worker at work on each side, as vanilla workplaces run (a
  power wheel only turns with its beaver on it). Each colony keeps its own working hours, so power flows in the hours
  both colonies' workers are on shift. The status line says which side is unstaffed.
- Hidden in a one-shared-colony game (a disabler like `TradingPostToolDisabler`).

## 4. Direction

- Each half has **Send power to <partner>**, off by default. Only that half's colony (or its steward, by the steward
  rules) may change it. Synced event carrying the actor's slot; every check is made in `Replay()`.
- **One direction per pair of colonies, over every Power Export Facility between them.** While B sends to A anywhere, A's
  toggle is greyed: *Player 2 is sending power to you. They must stop first.* Per pair, not per building: two exports
  pointing opposite ways would loop power.
- **1-to-1:** a colony sends to one colony and receives from one. Turning on a second outgoing or incoming link is
  refused with a message naming the existing one. Several Power Export Facilities between the same two colonies all carry the
  one link (they add capacity only if a cap is ever added; today there is none).
- **No cycles:** a toggle that would close a circle (A → B → C → A) is refused. Links therefore form chains with a
  start and an end, solved in order.

## 5. How much is sent

Timberborn's model: a network's supply (generators + battery discharge) against its demand (consumers); short supply
shares out evenly, surplus charges batteries.

The export half is a **consumer** in the sender's network; the import half is a **generator** in the receiver's,
with the same amount (**VERIFY** that a `MechanicalNode`'s input/output can change at runtime — the water wheel's
varying output suggests it can).

**The sender sends only what the receiver can use:** the receiver's unmet consumer demand + room in its batteries +
what it is passing on down the chain.

Two settings on the sender's half:

| Setting | Default | Effect |
|---|---|---|
| **Charge my batteries first** | on | On: own consumers → own batteries → partner. Off: own consumers → partner → own batteries. |
| **Use my batteries for Player 2** | off | On: when live surplus can't cover what the partner can use, the sender's batteries discharge to cover the rest (after the sender's own consumers). Off: only live surplus is sent. |

- With both on, the sender's batteries act as a buffer for the partner: they fill from surplus and discharge to the
  partner when surplus falls short.
- **Relaying:** a colony's spare power includes what it imports, so B passes A's leftover on to C. If B's battery
  toggle is on, B's batteries may feed C too.
- **Timing:** each link uses the previous tick's figures (one-tick lag per link). Invisible in play, identical on
  every computer, and no two networks are ever solved together.
- **Math in one pure function** (e.g. `PowerExportMath.Transfer`), headless-tested in `StabilityTests`: battery
  order both ways, battery drain on/off, relaying, never more than the receiver can use, direction lock, 1-to-1, cycle
  refusal.

## 6. Seeing the power networks (Ctrl+P)

A `ColonyPowerOverlay`, a copy of `ColonyRoadOverlay` for power. Ctrl+L and the road overlay stay exactly as they are.

- **Ctrl+P toggles it at any time.** New key binding `BeaverBuddies.KeyBind.ToggleColonyPower`, path `/Keyboard/p`,
  modifiers `Ctrl`, loc key `BeaverBuddies.KeyBindings.ToggleColonyPower` = *Show colonies' power*, next to the roads
  binding in the Timber Together key group (rebindable, like every binding). Checked against the game: plain P is
  `ToggleBuildingPause` with `AllowOtherModifiers: false`, and no game or mod binding uses Ctrl+P.
- **Draws every colony's power network**, yours and everyone else's: every tile of a shaft, gearbox, generator,
  battery or mechanical building, built or being built, as the same filled square the road overlay uses, in a
  strong version of its colony's colour. A Power Export Facility shows each half in its side's colour.
- **Also shown while a power piece is in hand** (shafts, gearboxes, generators, batteries, the Power Export Facility),
  just as roads show while a building tool is in hand. Every other building tool keeps showing roads.
- Display only: reads placed buildings and their owners, never the simulation. Rebuilt into a mesh when something
  changes, not every frame, with the same redraw and refresh intervals as the road overlay.
- `TWO-COLONIES.md` gets a *Seeing the power networks* line beside *Seeing the roads*.

## 7. The Power window (H)

A `PowerOverviewPanel`, built as `TradeOverviewPanel` is: the game's framed box with a title badge and close button,
dragged by its title or frame, closed with its close button or Esc, never pausing the game. Display and buttons only:
every button sends the same action as the facility's own panel.

- **Opens and closes with H** (binding `BeaverBuddies.KeyBind.PowerOverview`, path `/Keyboard/h`, modifiers `None`,
  *Power window*, rebindable; checked against the game: H's only binding is Ctrl+H `ToggleGUI`, with
  `AllowOtherModifiers: false`, and no Timber Together binding uses H), and a square **Power** button at the top right beside
  the Trade button, drawn like the game's own top-right buttons.
- **While it is open, the Ctrl+P power view is shown** and goes back to what it was when the window closes.
- **The chain** at the top, one line: *Player 1 → Player 2 → Player 3*, each name in its colony's colour, with the hp
  on each arrow. *No power is being sent* when there is none.
- **Your power**, one row per network of your colony: generated, used and spare (hp), batteries stored/capacity,
  and a **Go there** button that selects its largest generator. What comes in or goes out through a facility shows on
  the network it joins.
- **Power Export Facilities**, one row per facility with a half on your colony's network: the partner's faction icon
  and name, direction and hp now, a status line (the facility panel's), the three checkboxes for your half, and **Go
  there**. Greyed with the reason when your toggle can't be turned on (direction lock, 1-to-1, cycle).
- **Colonies**, one row per other colony: generated, used, spare and batteries, from the same figures (every computer
  holds every colony's state; only the display is filtered). Useful to see who could spare power before asking, and
  to settle payment at a Trading Post.
- Figures refresh on the overview panel's interval, and rows are rebuilt only when what they show changes (the rc30
  lesson: no lost clicks, no flashing tooltips).

## 8. UI (the facility panel)

Native, built like `TradingPostFragment` from `NativeElements` and the game's tooltip registrar.

- **Header row:** the partner's faction icon, *Power Export Facility with Player 2*.
- **Body** (`scroll--green-decorated`):
  - **Flow:** *Player 1 → Player 2* and the amount in the game's power style (*140 hp*). Status lines: *Not
    sending*, *Player 2 needs no power*, *Nothing to spare*, *Connect both halves to power*, *Both sides are your
    colony*, *No worker on Player 2's side*.
  - **Workers:** the game's own workplace panel on each half (worker slot, hours), as the Trading Post keeps it.
  - **Your half:** `CheckBox` **Send power to Player 2**; under it, greyed while not sending, **Charge my batteries
    first** and **Use my batteries for Player 2**.
  - **Partner's half:** the same lines, read-only and muted.
  - **Select my half** wooden button, as on the Trading Post.
- **The game's own power panel** stays on each half, showing that half's network. In the network's statistics the
  sender sees the Power Export Facility as a consumer, the receiver as a generator.
- **Notices** in the Trading Post notice style: *Player 1 is sending you power.* / *Player 1 stopped sending power.*
  No questions to answer: nothing is traded.
- No ledger or counter.

## 9. VERIFY before building

1. `MechanicalNode` input/output changeable at runtime, and how the graph re-reads it.
2. Where `MechanicalGraph` connects transputs (the safety-net hook), and whether `ClusterMechanicalConnectorActivator`
   connects nodes another way.
3. Battery charge/discharge order within a tick (for both battery settings).
4. Shaft transput layout, for the footprint and which faces connect.
5. The workers. Option A: build on the District Crossing's workings, as the Trading Post does, which already staffs
   each half from its own district (`DistrictCrossingWorkplaceBehavior`); check that the crossing's road link between
   the halves is acceptable here, or can be cut. Option B: a workplace per half. Pick after reading both.
6. That a staffed-but-off-shift half reads as unstaffed (the worker is at home), matching vanilla power buildings.

## 10. Docs to update when built

- `TWO-COLONIES.md`: the Power line under shared things becomes the rule; a Power Export Facility section after Trading Posts.
- README / site: a colony rule and a `#play` card, per CLAUDE.md's writing rules. Player text says **Power Export Facility**.

## 11. Out of scope

Payment, prices or a ledger at the Power Export Facility; fans (one colony sending to two); cycles; a throughput cap.
