# 🛠️ Technical details

## 🔄 Sync

Only the host simulates the world. Players have the simulation turned off and render what the host sends.

- **Delta stream:** every 50 ms the host sends changed tiles, buildings, units, statuses, effects, projectiles and sounds. Kingdoms, cities, wars and other meta objects are sent once a second.
- **Positions:** unit positions are sent separately as unreliable messages. Players render them with a small delay to keep movement smooth.
- **Priority:** players send their camera area to the host. Units in view are updated first. On a slow connection the host drops small effects first.
- **Repair:** both sides compare checksums every 2 seconds. Mismatched units, buildings, tiles or meta objects are requested again. The full world is sent only on join and on `F9`.
- **Player actions:** god powers, orders, edits, magnet and bowling ball go to the host and are applied there. The host checks every request against Power Rules.
- **Possession:** a player moves their unit locally and the host follows that position. Attacks, jumps and knockback are handled by the host. One unit can be controlled by one player.
- **Frozen simulation:** on the player side the mod runs animation time, unit facing, rotation reset after attacks and map layers. Attack effects outside the host's view are sent to players who can see them.

## 🌐 Connections

- Steam: relay connections through `SteamNetworkingSockets`, no port forwarding.
- IP: TCP, port 7777.
- Lobby data: host name, players, slots, password flag, other mods, map size, world year, protocol version.
- Lobby browser: public lobbies from the Steam lobby list, friends' lobbies from their game info.
- Invite code: the lobby Steam ID encoded in 7 characters.
- On join the player sends the protocol version and password. The host rejects a different protocol, a wrong password, kicked players and full lobbies.
- The host checks its Steam lobby every 5 seconds and creates a new one if Steam removed it.

## 📁 Project structure

| Path | Contents |
|---|---|
| `src/MPMod.cs` | Entry point, message handling, join and leave |
| `src/Net.cs` | Message types, TCP transport, protocol version |
| `src/SteamSession.cs` | Steam lobby, relay sockets, lobby browser, invite codes |
| `src/Lobby.cs` | Slots, passwords, kicking |
| `src/HostSync.cs`, `src/ClientSync.cs` | Delta stream |
| `src/PositionSync.cs` | Unit positions |
| `src/Checksum.cs`, `src/ObjectRepair.cs`, `src/MetaSync.cs` | Checksums and repair |
| `src/RemotePossession.cs`, `src/OwnUnit.cs` | Possession |
| `src/Rules.cs`, `src/HostOnly.cs` | Power Rules, host-only settings |
| `src/*Window.cs`, `src/MPTab.cs`, `src/Ui.cs` | UI |
| `src/Chat.cs`, `src/MapPings.cs` | Chat, pings |
| `src/Access.cs` | Access to private game fields and methods |
| `lang/` | UI strings, 38 languages |
| `art/` | Icons |

## 🔨 Build

Requires the .NET SDK and WorldBox with NeoModLoader.

```bash
dotnet build -c Release -p:Deploy=true
```

Game and NeoModLoader DLLs are referenced from the local install. Use `-p:GameDir=<path>` if WorldBox is not in the default Steam folder. `-p:Deploy=true` copies the mod to `<WorldBox>/Mods/WorldBoxMP`.

After editing `lang/strings.py`:

```bash
python lang/gen.py src/LangTable.cs
```

## 🧪 Tests

Tests are excluded from release builds. Build with `-p:Tests=true` and run several game instances:

```bash
worldbox.exe -logFile host.log -wbmp host -wbmpslot 4 -wbmplobby
worldbox.exe -logFile p1.log -wbmp client -wbmplobby
```

| Flag | Covers |
|---|---|
| `-wbmplobby` | Host and 3 players: join, kick, slots, password, chat, pings, world loading lock |
| `-wbmprules` | Power Rules, requests that bypass the UI, one controller per unit |
| `-wbmpanim` | Animations, possession, attacks, speed, bowling ball, lobby recovery |
| `-wbmpui <folder>` | UI screenshots; `-wbmpfake N` fills the lobby browser |

Results are logged as `test: ... PASS` or `FAIL`.

## 📋 Logs and config

Session logs are in `Mods/WorldBoxMP/logs`: errors, connection stats, traffic and a state summary every 10 seconds for comparing host and player logs.

`Mods/WorldBoxMP/WorldBoxMP.cfg`:

| Key | Default | Description |
|---|---|---|
| `host_ip`, `port` | `127.0.0.1`, `7777` | Last IP used |
| `tick_ms` | `50` | Delta interval |
| `info_refresh_seconds` | `20` | Refresh interval for units outside the view |
| `full_resync_seconds` | `0` | Periodic full resend, off |
| `show_names` | `1` | Player names above units |
