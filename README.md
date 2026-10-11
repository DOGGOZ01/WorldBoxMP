# WorldBoxMP

Two-player online WorldBox. One player hosts and runs the world; the second player sees the same world live and
uses god powers in it. Built on [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader).

> Early version. Both players need the same WorldBox version and the same WorldBoxMP version.

## How it works
- The **host** runs the real simulation.
- The **second player's** own simulation is paused. The host streams changes about 10 times a second: terrain,
  fire, buildings, creatures and their positions; and once a second kingdoms, cities and their borders,
  cultures, species, wars, alliances, diplomacy and the world date and counters.
- When the second player uses a god power, the click is sent to the host and happens in the host's world, so
  both players see the result.
- Every `full_resync_seconds` (and on F9) the host also sends the whole world, as a safety net for details the
  stream does not carry yet (genetic mutations, effects like arrows and emote bubbles).
- The second player's saves are never touched: autosave is off while connected.

## Install (both players)
1. Install NeoModLoader (Steam Workshop or its GitHub releases).
2. Copy `WorldBoxMP.dll` and `mod.json` into `<WorldBox>/Mods/WorldBoxMP/`.
3. Start WorldBox once; `Mods/WorldBoxMP/WorldBoxMP.cfg` is created.

## Play
### The "Мультиплеер" tab
A tab in the bottom power bar (the last one) has every action as a button — Создать игру, Пригласить друга,
Подключиться по IP, Синхронизировать мир, Отключиться — and a status line (who is connected, stream KiB/s,
world checks). The hotkeys below do the same.

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

## Build
```
dotnet build -c Release -p:Deploy=true
```
References the game's and NeoModLoader's DLLs from your own install (`GameDir` property); nothing from the game
is copied into this repo. `-p:Deploy=true` copies the mod into `<WorldBox>/Mods/WorldBoxMP`.

Testing on one PC: start two instances from `worldbox.exe` with `-logFile <path> -wbmp host` and
`-logFile <path> -wbmp client -wbmptest` (see `src/TestHarness.cs`).

