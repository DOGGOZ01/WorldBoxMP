# WorldBoxMP

Two-player online WorldBox. One player hosts and runs the world; the second player sees the same world live and
uses god powers in it. Built on [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader).

> Early version. Both players need the same WorldBox version and the same WorldBoxMP version.

## How it works
- The **host** runs the real simulation.
- The **second player's** own simulation is paused. The host streams changes 20 times a second (`tick_ms`):
  terrain, fire, buildings, creatures and their positions (replayed smoothly a little in the past), status
  effects (burning, frozen, cursed...), effects, shots, falling drops (rain, acid, seeds) and world sounds;
  once a second kingdoms, cities and their borders, cultures, species, wars, alliances, diplomacy and the
  world date and counters (new ones at once); and every `info_refresh_seconds` each creature's and building's
  details (name, health, level, hunger, items...).
- Every 2 s the host sends a checksum. The second player compares it, and when kingdoms, cities or other meta
  objects differ, asks for that part again object by object (logged as "repair" lines).
- When the second player uses a god power, the click is sent to the host and happens in the host's world, so
  both players see the result.
- Every `full_resync_seconds` (and on F9) the host also sends the whole world, as a safety net for details the
  stream does not carry yet (genetic mutations, emote bubbles). It waits while the second player controls a unit.
- The second player's saves are never touched: autosave is off while connected.

## Install (both players)
1. Install NeoModLoader (Steam Workshop or its GitHub releases).
2. Copy `WorldBoxMP.dll` and `mod.json` into `<WorldBox>/Mods/WorldBoxMP/`.
3. Start WorldBox once; `Mods/WorldBoxMP/WorldBoxMP.cfg` is created.

## Play
### The Multiplayer tab and menu
A tab in the bottom power bar (the last one, two little players) has the menu, Invite friend, Disconnect and a
status line (who you play with, ping). The menu shows only what fits the moment: Host game and Join by IP (the
address is typed right there) when not connected; Invite friend, Reload world and Stop hosting for the host;
Reload world and Disconnect for the second player; the logs folder in the corner. The interface follows the
game's language (all 38 WorldBox languages, `lang/strings.py`). The hotkeys below do the same.

### Players (up to four)
The host plus up to three players. The "Players" window lists everyone in their colour (host orange, then blue,
green, purple; cursors and names match) with their ping. The host can remove a player (they cannot come back on
their own until the host presses "invite again"), close or open free slots (no free open slot: joining is refused),
and give a player their own rules or put them back on the shared ones. The world stream is built once per tick and
sent to every player, so each extra player costs the host little more than bandwidth.

### What the second player may do
The host's menu has "Second player can": ten switches (terrain, creatures, nature, destruction, other powers,
kingdom relations, renaming and editing, unit orders, unit control, speed and pause), all on when the game
starts, changeable any time. The second player's tabs and buttons for what is off turn grey, and the host checks
every request itself, so a client that gets around its interface still changes nothing. A unit one player controls
directly takes no orders and no possession from the other; selecting it and editing its name, traits and items
stays open to both. Items from the equipment editor are made by the host.

A gear next to each tab's switch opens its powers: every power's own icon, click to allow or deny it, allow all,
deny all, and a filter (all / allowed / denied). World laws and world ages are the host's only: greyed on the
second player's side with "only the host" in the tooltip, and their windows never open there. "Unlock everything"
(host) counts every trait, item, creature, gene and plot as discovered in this world for both players; it is a
flag in the world's save (MapStats.custom_data), the players' own progress stays as it is.

### With a Steam friend (no IP, no port forwarding)
1. The host presses **F7**: a friends-only Steam lobby opens together with Steam's invite window. Invite the friend.
2. The friend accepts the invite in Steam. If WorldBox is running they join right away; if not, Steam starts it
   and it joins once the world has loaded. The connection goes through Steam's relays.

### By IP
| Key | Who | What |
|---|---|---|
| F7 | host | start hosting (TCP port from the cfg, default 7777) and a Steam lobby |
| F8 | second player | connect to `host_ip` from the cfg |
| F9 | second player | ask for the whole world again |
| F6 | either | stop |

The second player must be able to reach the host's port: same LAN, a port forward of 7777/TCP on the host's
router, or a virtual LAN (Radmin VPN, ZeroTier, Hamachi) with the host's VPN address as `host_ip`.

## Session logs
Both players get a log per session in `Mods/WorldBoxMP/logs` (the scroll button in the menu opens the folder):
every WorldBoxMP line and game error, a COUNTERS line every 10 s (what was sent or applied, stream KiB/s per
section, host ms per section), and every 10 s a STATE dump with the same number on both sides (counts, every
kingdom and city with name, colour, king, capital, leader and zones, unit info sums, statuses).

Each line NET (every 10 s) has the measured round trip, KiB/s each way, the Steam send queue and, on the client,
delta apply time, backlog and replay delay; SYSTEM (session start) has OS, CPU, RAM, GPU, mods and settings.
`Mods/WorldBoxMP/СОБРАТЬ_ЛОГИ.bat` (from `package/`) zips the session logs, the cfg and the game's Player.log
onto the desktop, for a player to send back.

Compare a host log with a client log:
```
python tools/wbmp_logs.py HOST.log CLIENT.log
python tools/wbmp_logs.py --latest        # newest pair in the game's logs folder (both players on one PC)
```
The report lists errors grouped by kind, traffic and fps, every STATE dump where the two differ, each meta repair
("STILL DIFFERS after 3 repairs" marks a real bug), possession, and a merged timeline.

## Build
```
dotnet build -c Release -p:Deploy=true
```
References the game's and NeoModLoader's DLLs from your own install (`GameDir` property); nothing from the game
is copied into this repo. `-p:Deploy=true` copies the mod into `<WorldBox>/Mods/WorldBoxMP`.

Testing on one PC: start two instances from `worldbox.exe` with `-logFile <path> -wbmp host` and
`-logFile <path> -wbmp client -wbmptest` (see `src/TestHarness.cs` for `-wbmpfx`, `-wbmpfight`, `-wbmppossess`,
`-wbmplag`, `-wbmpslot`, `-wbmpspeed`).

