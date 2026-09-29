# Connection panel and chat

A small panel in a corner of your screen during a multiplayer game. It shows who is connected and their colony, each
connection's ping, whether you're in sync, how fast the game runs, and who paused it. Below that are a speed boost and a
chat box.

## What it shows

**Collapsed**, one line: the sync dot, the number of players and one ping.

```
o  3 players  42 ms                                 [+]
```

**Expanded:**

```
Multiplayer                                 Host   [-]
o  In sync
-------------------------------------------------------
Player 1 (colony 1)                                -
Player 2 (colony 2)                            42 ms [<)]
Player 3 (colony 3)                           190 ms [<x]
-------------------------------------------------------
Tick rate   1.7 ticks/s
Speed       1x
Ease off below  Off  >
Connection  Direct
```

Your own row is in bold, with a dash instead of a ping. In a separate-colonies game each name shows the colony that
player runs (a steward shows the colony they're running). In a shared game the rows are names only.

| Line | Meaning |
| --- | --- |
| **Status** | *In sync* is normal. *Catching up* (guests): a few ticks behind the host. *Waiting for host* (guests): waiting for the host's next tick. *Connection unstable*: someone hasn't responded for five seconds. *Out of sync*: a desync; see the [troubleshooting page](https://timbermods.github.io/TimberTogether/troubleshooting.html#desync). *Disconnected*: the session has ended. The dot is green in sync, yellow while catching up or waiting, red otherwise. |
| **Players** | Everyone in the game, host first. On the host, a guest still loading shows *(loading)*: unpause once nobody does. **Click a row** to go to that player (their cursor, or what they've selected); your own row takes you back to your colony. |
| **Sound** | The speaker at the end of another player's row. **Click it** to mute the sounds of that player's actions (deleting, placing) and their chat chime on your computer; click again to hear them. Only you are affected, for this session. |
| **Eye** | Next to the speaker, in a separate-colonies game. **Click it** to hide what that player is still building on your computer (their finished buildings and Trading Posts stay in view); click again to see it. Shown by default. Only you are affected, for this session. |
| **Ping** | Round trip to that player, in milliseconds. Normal text up to 80 ms, yellow up to 160 ms, red above or **No response**. A guest sees the other guests' pings to the host. |
| **Tick rate** | Simulation ticks per second: about 1.7 at normal speed, about 11.7 at the fastest button, 0 when paused. |
| **Speed** | The speed the game runs at, including any speed boost. |
| **Behind host** | Guests: how many ticks behind the host you are. Should be 0 or 1. |
| **Guest behind**, **Guest fps** | Host: the slowest guest's lag in ticks, and the lowest guest frame rate. |
| **Easing off** | Host: the share of the chosen speed the game runs at while a guest catches up, such as *75% of speed*. It returns to full speed by itself. |
| **Ease off below** | Host: click to choose a guest frame rate floor (Off, 20, 30, 45 or 60 fps). The game slows a little while a guest stays below it. |
| **Connection** | Direct (IP, Hamachi) or Steam. |

## Pausing

Pressing pause (the speed buttons' pause, or its key) pauses the game for everyone. While it is paused, the title line
shows *Paused by* and the player's name, open or collapsed, cut short if it's long (hover for all of it); a new game
waiting to start shows *Paused*. Anyone can pick
a speed to carry on. Menus, dialogs and panels don't pause a co-op game.

## Showing and hiding

- **Click the title** to collapse or expand it. Collapsed, it shows **N new** for unread chat messages.
- Trade messages move aside so they don't cover it. The panel is drawn in front of the game's alerts.
- In Timber Together's settings (**Mods** → the settings button beside it): **Connection panel** is Expanded,
  Collapsed or Hidden, and **Connection panel position** is top left (default), top right, bottom left or bottom
  right.
- **Options → Bindings → Timber Together → Toggle connection panel**: a key to hide and show it (none by default).

It appears only in multiplayer games, and scales with your UI scale.

## Chat

```
Speed boost [-] [+0.5] [+]   = 1.5x
Player 2: anyone want to build a second dam?
Player 1: yes, upstream of the farm
Player 2: on it
[ Type a message...                                   ]
```

- **Send:** click anywhere on the open panel, type and press **Enter**. The box keeps the cursor after each message,
  so you can type the next one straight away. Enter on an empty box, **Esc** or a click on the game gives the
  keyboard back. **Enter** opens the box too (**Chat: start typing** under Options → Bindings).
- **Typing doesn't play the game:** the game's hotkeys are off while you type.
- **Names** are each player's **Ping display name**, in their cursor color. A player who kept the default yellow gets
  a color by player number, so no two start the same. Pick the color you see your own name in under Options (Esc) →
  **Player cursors**.
- **Everyone sees the same conversation**, in the same order. A player who joins later gets the history.
- **A chime** plays when another player's message arrives, open, collapsed or hidden (at most once a
  second). Muting a player's speaker silences their chime.
  It follows the game's interface volume. Your own messages and the history you get on joining are silent.
- **New messages you haven't seen** (you scrolled up to read older ones) show as a yellow *2 new* at the top right
  of the messages. Click it to go down to them.
- **Chat isn't saved**: it starts empty after a reload or a rehost. Messages are one line, up to 200 characters.

**Speed boost.** The row at the top of the chat adds to the speed everyone picked. **−** and **+** step by 0.5, or
type a number and press **Enter**. With +0.5, the fastest button runs at 7.5x. It goes from −6.5 to +23, and the
game stays between 0.5x and 30x. Any player can change it, for everyone. It resets to 0 in a new session. The slowest
computer still sets the real pace: the **Tick rate** line says what's achieved.

## Known limits

- Much of the text this mod adds is English only. Chat carries any text, but the game's font decides what can be
  drawn.
- With your **Player activity indicators** off, other players show as *Player N* (the host as *Host*).
- Chat is text only: no private messages, no emoji picker, no editing.
- Ping is measured about once a second. The panel doesn't show packet loss or bandwidth.
- The panel lists who is connected. A colony whose player is away shows in the colonies window (Y) instead.

How ping is measured and why the panel can't affect the game: [DEVELOPING.md](DEVELOPING.md#connection-panel-chat-and-player-cursors).
