using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class LobbyTest
    {
        public static bool On;
        public static string LastLeave;
        static float _clock = -1f;
        static float _kickAt = -1f;
        static string _kickedKey;
        static int _phase;
        static float _retryTimer;
        static readonly List<string> _attempts = new List<string>();
        static bool _done;
        static bool _sawFour;
        static int _humans;
        static float _sawFourAt;
        static bool _spawned;
        static bool _counted;
        static bool _shot;
        static float _lobbyAt = -1f;
        static string _password;
        static bool _enterNext;

        static readonly System.Action<SaveManager> StartLoad =
            HarmonyLib.AccessTools.MethodDelegate<System.Action<SaveManager>>(HarmonyLib.AccessTools.Method(typeof(SaveManager), "startLoadSlot"));

        static bool Said(byte slot, string text)
        {
            foreach (Chat.Line l in Chat.Lines) if (l.Slot == slot && l.Text == text) return true;
            return false;
        }

        static void TryWorlds(string who)
        {
            int before = SessionLock.Refused;
            SaveManager.setCurrentSlot(1);
            StartLoad(HarmonyLib.AccessTools.FieldRefAccess<MapBox, SaveManager>("save_manager")(World.world));
            World.world.clickGenerateNewMap();
            Check(who + ": no load or new world in the lobby", SessionLock.Refused == before + 2 && !SmoothLoader.isLoading() && Net.Role != Role.None,
                (SessionLock.Refused - before) + " refused");
        }

        static int Count(string asset)
        {
            int n = 0;
            foreach (Actor a in World.world.units) if (a.isAlive() && a.asset.id == asset) n++;
            return n;
        }

        static void Check(string what, bool ok, string detail = "") =>
            Debug.Log("[WorldBoxMP] test: lobby " + (ok ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? " (" + detail + ")" : ""));

        static Actor Pick(int index)
        {
            var list = new List<Actor>();
            foreach (Actor a in World.world.units)
                if (a.isAlive() && a.asset.allow_possession && a.canBePossessed() && !SpecialActors.IsSpecial(a) && !a.isInsideSomething()) list.Add(a);
            list.Sort((x, y) => x.getID().CompareTo(y.getID()));
            return index < list.Count ? list[index] : null;
        }

        public static void Update(float dt)
        {
            if (!On || _done || World.world == null) return;
            if (Net.Role == Role.Host) Host(dt);
            else Player(dt);
        }

        static void Host(float dt)
        {
            int ready = 0;
            foreach (Guest g in Net.Guests) if (g.Ready) ready++;
            if (_phase == 0)
            {
                if (ready < 3 || SmoothLoader.isLoading()) return;
                _phase = 1;
                _clock = 0f;
                _humans = Count("human");
                Debug.Log("[WorldBoxMP] test: lobby host: three players in");
            }
            float before = _clock;
            _clock += dt;
            bool At(float t) => before < t && _clock >= t;
            if (At(6f))
            {
                Roster.FromHost();
                Check("host: four in the list", Roster.Players.Count == 4, Roster.Players.Count.ToString());
                bool pings = true;
                foreach (Guest g in Net.Guests) if (g.PingMs <= 0) pings = false;
                Check("host: a ping for every player", pings);
            }
            if (At(10f))
            {
                Rules.Editing = Net.GuestIn(2);
                Rules.Toggle(Perm.Creatures);
                Rules.Editing = null;
                Check("host: slot 2 has its own rules", Net.GuestIn(2).OwnRules && (Net.GuestIn(2).Perms & Perm.Creatures) == 0 && Rules.HostAllows(Perm.Creatures));
            }
            if (At(24f))
            {
                LobbyWindow.Open();
            }
            if (At(26f))
            {
                Chat.Send("hello from host");
                MapPings.Place(new Vector2(MapBox.width / 2f, MapBox.height / 2f));
            }
            if (At(40f))
            {
                Check("host: chat from slot 1", Said(1, "hello from 1"));
                Check("host: ping from slot 2", MapPings.Seen.Contains(2), MapPings.Seen.Count + " slots");
            }
            if (At(42f)) TryWorlds("host");
            if (At(27f))
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(TestHarness.UiFolder, "lobby_host.png"));
            }
            if (At(29f))
            {
                Powers.AppliedBySlot.TryGetValue(1, out int one);
                Powers.AppliedBySlot.TryGetValue(2, out int two);
                Check("host: slot 1's power applied, slot 2's refused", one >= 1 && two == 0, one + " / " + two);
            }
            if (At(28.5f)) ScrollWindow.hideAllEvent(false);
            if (At(30f))
            {
                Guest c = Net.GuestIn(3);
                _kickedKey = c?.BanKey;
                Lobby.Kick(3);
                Check("host: slot 3 removed", Net.GuestIn(3) == null && _kickedKey != null && Lobby.Kicked.ContainsKey(_kickedKey), _kickedKey);
            }
            if (At(48f))
            {
                Lobby.ToggleSlot(2);
                Lobby.InviteAgain(_kickedKey);
                Check("host: slot 3 closed and the player invited again", !Lobby.SlotOpen[2] && !Lobby.Kicked.ContainsKey(_kickedKey ?? ""));
            }
            if (At(64f))
            {
                Lobby.ToggleSlot(2);
                Check("host: slot 3 open again", Lobby.SlotOpen[2]);
            }
            if (At(90f))
            {
                Check("host: slot 3 back", Net.GuestIn(3) != null && Net.GuestIn(3).Ready, Net.GuestIn(3)?.Label);
                Debug.Log("[WorldBoxMP] test: lobby host done");
                _done = true;
            }
        }

        static void Player(float dt)
        {
            if (_enterNext)
            {
                _enterNext = false;
                PasswordWindow.Enter(_password);
                return;
            }
            if (!Net.IsClientConnected && Net.Role == Role.None && _phase >= 1 && Roster.LocalSlot == 0 && LastLeave != null)
            {
                _retryTimer -= dt;
                if (_retryTimer > 0f) return;
                _retryTimer = 6f;
                _attempts.Add(LastLeave);
                Debug.Log("[WorldBoxMP] test: lobby player 3: left (" + LastLeave + "), joining again");
                if (LastLeave == "wrong_password")
                {
                    LastLeave = null;
                    Check("player 3: asked for the password", PasswordWindow.Pending);
                    ScreenCapture.CaptureScreenshot(Path.Combine(TestHarness.UiFolder, "password.png"));
                    _enterNext = true;
                    return;
                }
                LastLeave = null;
                if (_attempts.Count > 14) { Report(); return; }
                if (_password == null) _password = Net.JoinPassword;
                bool wrong = _attempts.Contains("lobby_full") && !_attempts.Contains("wrong_password");
                Net.JoinPassword = wrong ? "not-" + _password : _password;
                MPMod.Instance.Join();
                return;
            }
            if (!Net.IsClientConnected || SmoothLoader.isLoading() || Roster.LocalSlot == 0) return;
            if (_phase == 0) { _phase = 1; _clock = 0f; Debug.Log("[WorldBoxMP] test: lobby player in slot " + Roster.LocalSlot); }
            if (_phase == 2 && Roster.LocalSlot == 3 && _attempts.Count > 0) { Report(); return; }
            float before = _clock;
            _clock += dt;
            bool At(float t) => before < t && _clock >= t;
            if (!_sawFour && (Roster.Players.Count == 4 || _clock > 60f))
            {
                _sawFour = true;
                _sawFourAt = _clock;
                Check("player " + Roster.LocalSlot + ": four in the list", Roster.Players.Count == 4, Roster.Players.Count.ToString());
            }
            if (Roster.LocalSlot == 1 && At(22f))
            {
                Actor x = Pick(0);
                ControllableUnit.setControllableCreature(x);
                Check("player 1: holds X", ControllableUnit.isControllingUnit(x), x?.getID().ToString());
            }
            if (Roster.LocalSlot == 2 && At(28f))
            {
                Actor x = Pick(0);
                Check("player 2: knows player 1 holds X", Roster.OtherControls(x.getID()));
                ControllableUnit.setControllableCreature(x);
                Check("player 2: cannot take X", !ControllableUnit.isControllingUnit(x));
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                w.Write(1);
                w.Write(x.getID());
                Net.Peer.Send(Msg.Possess, ms.ToArray());
                Orders.Send(World.world.GetTile(MapBox.width / 2, MapBox.height / 2), false, null, new List<Actor> { x });
                Check("player 2: sees player 1's hand", RemoteCursorSeen(1));
            }
            bool myTurn = !_spawned && _sawFour && (Roster.LocalSlot == 2 ? (Rules.Client & Perm.Creatures) == 0 : Roster.LocalSlot == 1 && _clock > _sawFourAt + 4f);
            if (myTurn)
            {
                _spawned = true;
                bool may = (Rules.Client & Perm.Creatures) != 0;
                Check("player " + Roster.LocalSlot + ": creatures " + (Roster.LocalSlot == 2 ? "off" : "on"), may == (Roster.LocalSlot == 1), Rules.Client.ToString());
                Powers.SendClick(new Vector2Int(MapBox.width / 2 + Roster.LocalSlot * 3, MapBox.height / 2 + 8), AssetManager.powers.get("human"));
            }
            if (Roster.LocalSlot == 2 && _spawned && _lobbyAt < 0f) { _lobbyAt = _clock; LobbyWindow.Open(); }
            if (Roster.LocalSlot == 2 && _lobbyAt > 0f && _clock > _lobbyAt + 3f && !_shot)
            {
                _shot = true;
                ScreenCapture.CaptureScreenshot(Path.Combine(TestHarness.UiFolder, "lobby_player.png"));
            }
            if (Roster.LocalSlot == 1 && At(34f))
                Check("player 1: still holds X", ControllableUnit.isControllingUnit(Pick(0)));
            if (Roster.LocalSlot == 1 && At(26f)) Chat.Send("hello from 1");
            if (Roster.LocalSlot == 2 && At(26f)) MapPings.Place(new Vector2(MapBox.width / 2f + 5f, MapBox.height / 2f));
            if ((Roster.LocalSlot == 1 || Roster.LocalSlot == 2) && At(40f))
            {
                Check("player " + Roster.LocalSlot + ": chat from the host and slot 1", Said(0, "hello from host") && Said(1, "hello from 1"), Chat.Lines.Count.ToString());
                Check("player " + Roster.LocalSlot + ": the host's ping", MapPings.Seen.Contains(0));
                if (Roster.LocalSlot == 1) Check("player 1: slot 2's ping", MapPings.Seen.Contains(2));
            }
            if (Roster.LocalSlot == 1 && At(44f)) TryWorlds("player 1");
            if (Roster.LocalSlot == 3 && _clock > 25f) _phase = 2;
            if (Roster.LocalSlot != 3 && At(80f))
            {
                Debug.Log("[WorldBoxMP] test: lobby player " + Roster.LocalSlot + " done");
                _done = true;
            }
        }

        static bool RemoteCursorSeen(byte slot) => RemoteCursor.HasHand(slot);

        static void Report()
        {
            string seen = string.Join(",", _attempts.ToArray());
            Check("player 3: removed", _attempts.Count > 0 && _attempts[0] == "tip_kicked", seen);
            Check("player 3: could not come back on its own", _attempts.Contains("lobby_kicked"), seen);
            Check("player 3: no free slot while closed", _attempts.Contains("lobby_full"), seen);
            Check("player 3: a wrong password refused", _attempts.Contains("wrong_password"), seen);
            Check("player 3: back after the invite", Net.IsClientConnected && Roster.LocalSlot == 3, seen);
            Debug.Log("[WorldBoxMP] test: lobby player 3 done");
            _done = true;
        }
    }
}
