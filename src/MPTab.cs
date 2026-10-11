using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static class MPTab
    {
        static bool _created;
        static Text _status;
        static float _statusTimer;

        static readonly Dictionary<string, string> Texts = new Dictionary<string, string>
        {
            { "wbmp_tab", "Мультиплеер" },
            { "wbmp_tab_description", "Игра вдвоём: один игрок ведёт мир, второй видит его вживую и пользуется силами бога." },
            { "wbmp_host", "Создать игру" },
            { "wbmp_host_description", "Открыть свой мир для друга: лобби Steam (только для друзей) и порт для подключения по IP. Сразу откроется окно приглашения Steam." },
            { "wbmp_invite", "Пригласить друга" },
            { "wbmp_invite_description", "Снова открыть окно приглашения Steam. Друг принимает приглашение в Steam, и игра подключит его сама." },
            { "wbmp_join_ip", "Подключиться по IP" },
            { "wbmp_join_ip_description", "Подключиться к другу напрямую по адресу из файла Mods/WorldBoxMP/WorldBoxMP.cfg (строка host_ip). Нужен открытый порт или Radmin VPN / ZeroTier." },
            { "wbmp_resync", "Синхронизировать мир" },
            { "wbmp_resync_description", "Заново получить весь мир от хоста (у хоста — отправить его второму игроку)." },
            { "wbmp_logs", "Папка с логами" },
            { "wbmp_logs_description", "Открыть папку с журналами игры вдвоём (Mods/WorldBoxMP/logs). Пришлите оттуда последний файл, если что-то пошло не так." },
            { "wbmp_stop", "Отключиться" },
            { "wbmp_stop_description", "Завершить игру вдвоём. У второго игрока мир остаётся как был, его сохранения не тронуты." },
        };

        public static void TryCreate()
        {
            if (_created) return;
            _created = true;
            try
            {
                foreach (var kv in Texts) LM.AddToCurrentLocale(kv.Key, kv.Value);

                PowersTab tab = TabManager.CreateTab("wbmp_tab", "wbmp_tab", "wbmp_tab_description", Icon("ui/Icons/iconHumans"));
                tab.SetLayout(new List<string> { "session", "status" });
                Add(tab, "wbmp_host", "ui/Icons/iconFriendship", () => MPMod.Instance.Host());
                Add(tab, "wbmp_invite", "ui/Icons/iconAlliance", SteamSession.Invite);
                Add(tab, "wbmp_join_ip", "ui/Icons/iconAllianceList", () => MPMod.Instance.Join());
                Add(tab, "wbmp_resync", "ui/Icons/iconArrowDOWN", () => MPMod.Instance.Resync());
                Add(tab, "wbmp_stop", "ui/Icons/iconClose", () => MPMod.Instance.StopSession());
                Add(tab, "wbmp_logs", "ui/Icons/iconBooks", OpenLogs);

                _status = CreateStatusText(tab);
                tab.PutElement("status", _status.rectTransform, new Vector2(0f, 0f));
                tab.UpdateLayout();
                Debug.Log("[WorldBoxMP] multiplayer tab created");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[WorldBoxMP] could not create the multiplayer tab: " + e);
            }
        }

        static void Add(PowersTab tab, string id, string icon, UnityEngine.Events.UnityAction action)
        {
            PowerButton button = PowerButtonCreator.CreateSimpleButton(id, action, Icon(icon));
            tab.AddPowerButton("session", button);
        }

        static void OpenLogs()
        {
            string folder = MPMod.Instance.LogFolder;
            System.IO.Directory.CreateDirectory(folder);
            Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }

        static Sprite Icon(string path)
        {
            Sprite s = SpriteTextureLoader.getSprite(path);
            if (s == null) Debug.Log("[WorldBoxMP] missing icon " + path);
            return s;
        }

        static Text CreateStatusText(PowersTab tab)
        {
            var go = new GameObject("wbmp_status", typeof(RectTransform));
            go.transform.SetParent(tab.transform, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(280f, 72f);
            var text = go.AddComponent<Text>();
            Text sample = Resources.FindObjectsOfTypeAll<Text>().FirstOrDefault(t => t.font != null && t.gameObject.scene.IsValid());
            text.font = sample != null ? sample.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 9;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = new Color(1f, 0.85f, 0.45f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = MPMod.StatusText();
            return text;
        }

        public static void Update()
        {
            if (_status == null) return;
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer > 0f) return;
            _statusTimer = 0.5f;
            _status.text = MPMod.StatusText();
        }
    }
}
