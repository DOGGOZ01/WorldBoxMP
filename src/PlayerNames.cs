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
        private static readonly Color HostColor = new Color(1f, 0.66f, 0.2f);

        private static readonly Color ClientColor = new Color(0.45f, 0.85f, 1f);

        private static readonly HashSet<long> _remoteUnits = new HashSet<long>();

        private static string _remoteName = "";

        public static string RemoteName => _remoteName;

        private static float _sendTimer;

        private static int _sentHash;

        private static GUIStyle _style;

        private static readonly GUIContent _content = new GUIContent();

        public static bool InSession => Net.IsClientConnected || Net.IsHostWithPeer;

        public static string LocalName
        {
            get
            {
                string text = MPMod.Cfg?.PlayerName;
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
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
            _remoteUnits.Clear();
            _remoteName = "";
            _sentHash = 0;
            _sendTimer = 0f;
        }

        public static void Update()
        {
            if (!InSession)
            {
                if (_remoteUnits.Count > 0 || _sentHash != 0)
                {
                    Clear();
                }
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
            BinaryReader binaryReader = new BinaryReader(new MemoryStream(data));
            string text = binaryReader.ReadString();
            int num = binaryReader.ReadInt32();
            if (text != _remoteName)
            {
                SessionLog.Write("other player's name: " + text);
            }
            _remoteName = text;
            _remoteUnits.Clear();
            for (int i = 0; i < num; i++)
            {
                _remoteUnits.Add(binaryReader.ReadInt64());
            }
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
            bool firstPerson = Worldfall.Begin();
            bool flag = Net.Role == Role.Host;
            HashSet<Actor> mine = ControllableUnit.getCotrolledUnits();
            if (mine != null)
            {
                string localName = LocalName;
                foreach (Actor item in mine)
                {
                    DrawFor(item, localName, flag ? HostColor : ClientColor, firstPerson);
                }
            }
            if (_remoteUnits.Count == 0)
            {
                return;
            }
            foreach (long remoteUnit in _remoteUnits)
            {
                Actor actor = World.world.units.get(remoteUnit);
                DrawFor(actor, _remoteName, flag ? ClientColor : HostColor, firstPerson);
            }
        }

        private static void DrawFor(Actor a, string name, Color color, bool firstPerson)
        {
            if (a == null || !a.isAlive() || string.IsNullOrEmpty(name) || a.isInsideSomething())
            {
                return;
            }
            float x;
            float y;
            float size;
            float alpha;
            if (firstPerson)
            {
                if (!Worldfall.Place(a, out x, out y, out size, out alpha))
                {
                    return;
                }
            }
            else
            {
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
                x = vector.x;
                y = (float)Screen.height - vector.y - 10f * Scale;
                size = 15f * Scale;
                alpha = 1f;
            }
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

        private static class Worldfall
        {
            private static bool _looked;

            private static bool _broken;

            private static PropertyInfo _instance;

            private static FieldInfo _active;

            private static PropertyInfo _shownFrame;

            private static PropertyInfo _shown;

            private static PropertyInfo _host;

            private static MethodInfo _starHead;

            private static MethodInfo _project;

            private static FieldInfo _width;

            private static FieldInfo _height;

            private static FieldInfo _focal;

            private static object _mod;

            private static object _projection;

            private static Actor _hostActor;

            private static int _w;

            private static int _h;

            private static float _f;

            private static readonly object[] _headArgs = new object[2];

            private static readonly object[] _projectArgs = new object[6];

            public static bool Begin()
            {
                if (_broken)
                {
                    return false;
                }
                try
                {
                    if (!_looked)
                    {
                        _looked = true;
                        Type type = AccessTools.TypeByName("FirstPerson.WorldBoxMod");
                        Type type2 = AccessTools.TypeByName("FirstPerson.Core.FrameProjection");
                        if (type == null || type2 == null)
                        {
                            _broken = true;
                            return false;
                        }
                        _instance = AccessTools.Property(type, "Instance");
                        _active = AccessTools.Field(type, "_active");
                        _shownFrame = AccessTools.Property(type, "ShownFrame");
                        _shown = AccessTools.Property(type, "Shown");
                        _host = AccessTools.Property(type, "Host");
                        _starHead = AccessTools.Method(type, "StarHead");
                        _project = AccessTools.Method(type2, "Project");
                        _width = AccessTools.Field(type2, "Width");
                        _height = AccessTools.Field(type2, "Height");
                        _focal = AccessTools.Field(type2, "Focal");
                        if (_instance == null || _active == null || _shownFrame == null || _shown == null || _starHead == null || _project == null || _width == null || _height == null)
                        {
                            Debug.Log("[WorldBoxMP] Worldfall found, but not the parts needed for names in first person");
                            _broken = true;
                            return false;
                        }
                        Debug.Log("[WorldBoxMP] Worldfall found: names are drawn in first person too");
                    }
                    _mod = _instance.GetValue(null, null);
                    if (_mod == null || !(bool)_active.GetValue(_mod) || _shownFrame.GetValue(_mod, null) == null)
                    {
                        return false;
                    }
                    _projection = _shown.GetValue(_mod, null);
                    _w = (int)_width.GetValue(_projection);
                    _h = (int)_height.GetValue(_projection);
                    _f = ((_focal != null) ? ((float)_focal.GetValue(_projection)) : ((float)_h));
                    _hostActor = ((_host != null) ? (_host.GetValue(_mod, null) as Actor) : null);
                    return _w > 0 && _h > 0;
                }
                catch (Exception ex)
                {
                    _broken = true;
                    Debug.Log("[WorldBoxMP] names in first person turned off: " + ex.GetBaseException().Message);
                    return false;
                }
            }

            public static bool Place(Actor a, out float x, out float y, out float size, out float alpha)
            {
                x = (y = (size = (alpha = 0f)));
                if (a == _hostActor)
                {
                    return false;
                }
                try
                {
                    _headArgs[0] = a;
                    _headArgs[1] = null;
                    if (!(bool)_starHead.Invoke(_mod, _headArgs))
                    {
                        return false;
                    }
                    Vector3 vector = (Vector3)_headArgs[1];
                    _projectArgs[0] = vector.x;
                    _projectArgs[1] = vector.y;
                    _projectArgs[2] = vector.z + 0.32f;
                    _projectArgs[3] = null;
                    _projectArgs[4] = null;
                    _projectArgs[5] = null;
                    if (!(bool)_project.Invoke(_projection, _projectArgs))
                    {
                        return false;
                    }
                    float num = (float)_projectArgs[3];
                    float num2 = (float)_projectArgs[4];
                    float num3 = (float)_projectArgs[5];
                    alpha = 1f - Mathf.Clamp01((num3 - 45f) / 15f);
                    if (alpha <= 0f)
                    {
                        return false;
                    }
                    x = num / (float)_w * (float)Screen.width;
                    y = num2 / (float)_h * (float)Screen.height;
                    float num4 = _f / (float)_h * (float)Screen.height;
                    size = Mathf.Clamp(0.09f * num4 / num3, 11f * Scale, 30f * Scale);
                    return true;
                }
                catch (Exception ex)
                {
                    _broken = true;
                    Debug.Log("[WorldBoxMP] names in first person turned off: " + ex.GetBaseException().Message);
                    return false;
                }
            }
        }
    }
}
