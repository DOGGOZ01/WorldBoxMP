# 🌍 WorldBoxMP

Multiplayer mod for WorldBox. Up to 4 players in one world.

![C#](https://img.shields.io/badge/C%23-512BD4?logo=dotnet&logoColor=white)
![Unity](https://img.shields.io/badge/Unity-000000?logo=unity&logoColor=white)
![Harmony](https://img.shields.io/badge/Harmony-6C3483)
![Steamworks](https://img.shields.io/badge/Steamworks-000000?logo=steam&logoColor=white)
![NeoModLoader](https://img.shields.io/badge/NeoModLoader-2E7D32)

## 📖 About

The host runs the world. Other players get a copy of it and use god powers in it. Their actions are applied on the host and sent back to everyone.

You can connect through a Steam lobby, an invite code or an IP address. The mod supports all 38 game languages.

## 📥 Install

**Steam Workshop:** subscribe to [WorldBoxMP](https://steamcommunity.com/sharedfiles/filedetails/?id=3820448359). [NeoModLoader](https://steamcommunity.com/sharedfiles/filedetails/?id=3080294469) is installed with it.

**Manual:**
1. Install [NeoModLoader](https://steamcommunity.com/sharedfiles/filedetails/?id=3080294469), start WorldBox once and close it.
2. Download the zip from [Releases](https://github.com/DOGGOZ01/WorldBoxMP/releases).
3. Put the `WorldBoxMP` folder into `<WorldBox>/Mods`.

All players need the same WorldBox and mod version. Steam invites need the Steam overlay.

## 🎮 How to play

The mod adds a **Multiplayer** tab to the powers bar.

**Host:** open a world, press **Multiplayer → Create lobby**, set visibility and password, press **Open lobby**. Share the invite code or use **Invite friend**.

**Join:** press **Multiplayer → Join** and pick a lobby, or enter an invite code or IP. Steam invites also work.

**Leave:** **Exit Lobby**. The host closes the lobby for everyone. A player gets the save menu.

| Key | Action |
|---|---|
| `T` | Chat |
| `Alt` + click | Ping on the map |
| `F9` | Reload the world from the host |
| `F6` | Leave the lobby |

## ✨ Features

- Lobby browser with host name, players, map size and world year
- Public, friends-only and invite-only lobbies, optional password
- Player cursors and names in player colours
- Unit possession, unit orders, renaming, game speed
- Power Rules: the host chooses which powers players can use, for everyone or per player
- Chat and map pings

## ⚠️ Performance

Tested on high-end PCs. Recommended: **standard map size or smaller**, **up to 2000 units**. Larger worlds can lag.

## 🛠️ Technical details

Sync, project structure, build and tests: [docs/TECHNICAL.md](docs/TECHNICAL.md).

## 📬 Contact

[LinkedIn](https://www.linkedin.com/in/aleksandr-kuznetsov-018006358/) · [doggoz01@proton.me](mailto:doggoz01@proton.me)

## 📄 License

[PolyForm Noncommercial 1.0.0](LICENSE): free for personal and other noncommercial use. Selling the code or using it in a commercial product is not allowed.
