using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static class SessionLock
    {
        public static bool Active => Net.Role != Role.None;

        public static int Refused;

        static readonly Dictionary<Graphic, Color> _greyed = new Dictionary<Graphic, Color>();
        static float _timer;

        public static bool Refuse(string what)
        {
            if (!Active) return false;
            Refused++;
            Debug.Log("[WorldBoxMP] refused " + what + " while in a lobby");
            MPMod.Tip("tip_locked");
            return true;
        }

        public static void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.3f;
            if (!Active)
            {
                if (_greyed.Count == 0) return;
                foreach (var kv in _greyed) if (kv.Key != null) kv.Key.color = kv.Value;
                _greyed.Clear();
                return;
            }
            if (!ScrollWindow.isWindowActive()) return;
            ScrollWindow window = ScrollWindow.getCurrentWindow();
            if (window == null) return;
            foreach (Button b in window.GetComponentsInChildren<Button>(true))
            {
                if (!IsWorldButton(b) || b.targetGraphic == null || _greyed.ContainsKey(b.targetGraphic)) continue;
                _greyed[b.targetGraphic] = b.targetGraphic.color;
                b.targetGraphic.color = new Color(0.45f, 0.45f, 0.45f, b.targetGraphic.color.a);
            }
        }

        static bool IsWorldButton(Button b)
        {
            if (b.GetComponent<LoadWorldButton>() != null) return true;
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            {
                string m = b.onClick.GetPersistentMethodName(i);
                if (m == "clickGenerateMap" || m == "startLoadSaveSlot" || m == "clickGenerateNewMap" || m == "startLoadSlot") return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clickGenerateNewMap))]
    static class NoNewWorldInLobby
    {
        static bool Prefix() => !SessionLock.Refuse("a new world");
    }

    [HarmonyPatch(typeof(ButtonEvent), nameof(ButtonEvent.startLoadSaveSlot))]
    static class NoLoadButtonInLobby
    {
        static bool Prefix() => !SessionLock.Refuse("loading a save");
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.startLoadSlot))]
    static class NoLoadInLobby
    {
        static bool Prefix() => !SessionLock.Refuse("loading a save");
    }

    [HarmonyPatch(typeof(LoadWorldButton), "loadWorld")]
    static class NoWorkshopLoadInLobby
    {
        static bool Prefix() => !SessionLock.Refuse("loading a world");
    }
}
