using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class PlayerLook
    {
        private const float SendInterval = 0.05f;

        private static float _sendTimer;

        private static long _remoteId = -1L;

        private static float _remoteYaw;

        private static double _remoteAt;

        private static Actor _shown;

        public static void Clear()
        {
            if (_shown != null)
            {
                WorldfallBridge.ClearLook(_shown);
            }
            _shown = null;
            _remoteId = -1L;
        }

        public static void Receive(byte[] data)
        {
            BinaryReader binaryReader = new BinaryReader(new MemoryStream(data));
            _remoteId = binaryReader.ReadInt64();
            _remoteYaw = binaryReader.ReadSingle();
            _remoteAt = Clock.Now;
        }

        public static void Update()
        {
            if (!PlayerNames.InSession || !WorldfallBridge.Present)
            {
                if (_shown != null || _remoteId >= 0)
                {
                    Clear();
                }
                return;
            }
            _sendTimer -= Time.unscaledDeltaTime;
            Actor controllableUnit = ControllableUnit.getControllableUnit();
            if (_sendTimer <= 0f && controllableUnit != null && WorldfallBridge.InFirstPerson)
            {
                _sendTimer = SendInterval;
                MemoryStream memoryStream = new MemoryStream(12);
                BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
                binaryWriter.Write(controllableUnit.getID());
                binaryWriter.Write(WorldfallBridge.Yaw);
                Net.Peer?.Send(Msg.Look, memoryStream.ToArray());
            }
            Actor actor = ((_remoteId >= 0 && Clock.Now - _remoteAt < 0.5) ? World.world.units.get(_remoteId) : null);
            if (actor != _shown && _shown != null)
            {
                WorldfallBridge.ClearLook(_shown);
            }
            _shown = actor;
            if (actor != null && actor.isAlive())
            {
                Vector2 vector = new Vector2(Mathf.Cos(_remoteYaw), Mathf.Sin(_remoteYaw));
                WorldfallBridge.SetLook(actor, actor.current_position + vector * 3f);
            }
        }
    }
}
