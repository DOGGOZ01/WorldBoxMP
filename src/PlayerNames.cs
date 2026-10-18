using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace WorldBoxMP
{
    public static class PlayerNames
    {
        public static string RemoteName
        {
            get
            {
                var names = new List<string>();
                foreach (PlayerInfo p in Roster.Players)
                    if (p.Slot != Roster.LocalSlot && !string.IsNullOrEmpty(p.Name)) names.Add(p.Name);
                return string.Join(", ", names.ToArray());
            }
        }

        public static bool OtherControls(long id) => Roster.OtherControls(id);

        private static float _sendTimer;

        private static int _sentHash;

        private static GUIStyle _style;

        private static readonly GUIContent _content = new GUIContent();

        public static bool InSession => Net.IsClientConnected || Net.IsHostWithPeer;

        public static string LocalName
        {
            get
            {
                if (!string.IsNullOrEmpty(TestHooks.Name)) return TestHooks.Name;
                try
                {
                    if (SteamSession.Available && !string.IsNullOrEmpty(SteamClient.Name))
                    {
                        return SteamClient.Name;
                    }
                }
                catch
                {
                }
                return Environment.UserName;
            }
        }

        public static void Clear()
        {
            _sentHash = 0;
            _sendTimer = 0f;
        }

        public static void Update()
        {
            if (!Net.IsClientConnected)
            {
                if (_sentHash != 0) Clear();
                return;
            }
            string localName = LocalName;
            int num = localName.GetHashCode();
            HashSet<Actor> mine = ControllableUnit.getCotrolledUnits();
            if (mine != null)
            {
                foreach (Actor item in mine)
                {
                    if (item != null)
                    {
                        num = num * 31 + item.getID().GetHashCode();
                    }
                }
            }
            _sendTimer -= Time.unscaledDeltaTime;
            if (num == _sentHash && _sendTimer > 0f)
            {
                return;
            }
            _sentHash = num;
            _sendTimer = 1f;
            MemoryStream memoryStream = new MemoryStream();
            BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
            binaryWriter.Write(localName);
            List<long> list = new List<long>();
            if (mine != null)
            {
                foreach (Actor item2 in mine)
                {
                    if (item2 != null && item2.isAlive())
                    {
                        list.Add(item2.getID());
                    }
                }
            }
            binaryWriter.Write(list.Count);
            foreach (long item3 in list)
            {
                binaryWriter.Write(item3);
            }
            Net.Peer?.Send(Msg.Names, memoryStream.ToArray());
        }

        public static void Receive(byte[] data)
        {
            Guest g = Net.Current;
            if (g == null) return;
            BinaryReader binaryReader = new BinaryReader(new MemoryStream(data));
            string text = binaryReader.ReadString();
            int num = binaryReader.ReadInt32();
            if (text != g.Name)
            {
                SessionLog.Write("player in slot " + g.Slot + " is " + text);
            }
            g.Name = text;
            g.Units.Clear();
            for (int i = 0; i < num; i++)
            {
                g.Units.Add(binaryReader.ReadInt64());
            }
            Lobby.Changed();
        }

        public static void Draw()
        {
            if (MPMod.Cfg == null || !MPMod.Cfg.ShowNames || !InSession || !Config.game_loaded || World.world == null)
            {
                return;
            }
            if (Event.current.type != EventType.Repaint || SmoothLoader.isLoading())
            {
                return;
            }
            EnsureStyle();
            HashSet<Actor> mine = ControllableUnit.getCotrolledUnits();
            if (mine != null)
            {
                string localName = LocalName;
                Color own = Lobby.ColorOf(Roster.LocalSlot);
                foreach (Actor item in mine)
                {
                    if (Net.Role == Role.Host && RemotePossession.IsRemote(item)) continue;
                    DrawFor(item, localName, own);
                }
            }
            foreach (PlayerInfo p in Roster.Players)
            {
                if (p.Slot == Roster.LocalSlot) continue;
                Color color = Lobby.ColorOf(p.Slot);
                foreach (long id in p.Units)
                {
                    Actor actor = World.world.units.get(id);
                    DrawFor(actor, p.Name, color);
                }
            }
        }

        private static void DrawFor(Actor a, string name, Color color)
        {
            if (a == null || !a.isAlive() || string.IsNullOrEmpty(name) || a.isInsideSomething())
            {
                return;
            }
            Camera main = Camera.main;
            if (main == null)
            {
                return;
            }
            Vector3 vector = main.WorldToScreenPoint(a.getHeadOffsetPositionForFunRendering());
            if (vector.z < 0f)
            {
                return;
            }
            float x = vector.x;
            float y = (float)Screen.height - vector.y - 10f * Scale;
            float size = 15f * Scale;
            float alpha = 1f;
            if (x < -200f || y < -50f || x > (float)(Screen.width + 200) || y > (float)(Screen.height + 50))
            {
                return;
            }
            _style.fontSize = Mathf.Max(9, Mathf.RoundToInt(size));
            _content.text = name;
            Vector2 vector2 = _style.CalcSize(_content);
            Rect rect = new Rect(x - vector2.x * 0.5f, y - vector2.y, vector2.x, vector2.y);
            Color color2 = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f * alpha);
            GUI.DrawTexture(new Rect(rect.x - 4f, rect.y + 1f, rect.width + 8f, rect.height - 2f), Texture2D.whiteTexture);
            _style.normal.textColor = new Color(0f, 0f, 0f, 0.9f * alpha);
            for (int i = -1; i <= 1; i += 2)
            {
                GUI.Label(new Rect(rect.x + (float)i, rect.y, rect.width, rect.height), _content, _style);
                GUI.Label(new Rect(rect.x, rect.y + (float)i, rect.width, rect.height), _content, _style);
            }
            _style.normal.textColor = new Color(color.r, color.g, color.b, alpha);
            GUI.color = Color.white;
            GUI.Label(rect, _content, _style);
            GUI.color = color2;
        }

        private static float Scale => Mathf.Clamp((float)Screen.height / 1080f, 0.75f, 2.5f);

        private static void EnsureStyle()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                    clipping = TextClipping.Overflow
                };
                _style.padding = new RectOffset(0, 0, 0, 0);
                _style.margin = new RectOffset(0, 0, 0, 0);
            }
        }
    }
}
