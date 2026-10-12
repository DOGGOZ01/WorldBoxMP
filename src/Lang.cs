using System;
using System.Collections.Generic;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static partial class Lang
    {
        const string Prefix = "wbmp_";
        static bool _registered;

        static readonly HarmonyLib.AccessTools.FieldRef<LocalizedTextManager, Dictionary<string, string>> Texts =
            HarmonyLib.AccessTools.FieldRefAccess<LocalizedTextManager, Dictionary<string, string>>("_localized_text");

        static readonly KeyValuePair<string, string>[] Aliases =
        {
            new KeyValuePair<string, string>("disconnect_description", "leave_description"),
            new KeyValuePair<string, string>("stop_host_description", "leave_description"),
        };

        static readonly Dictionary<string, string> _live = new Dictionary<string, string>();

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            Fill(LocalizedTextManager.current_language?.id);
        }

        static void Fill(string lang)
        {
            if (LocalizedTextManager.instance == null) return;
            Dictionary<string, string> texts = Texts(LocalizedTextManager.instance);
            if (texts == null) return;
            string[] rows = lang != null && Table.TryGetValue(lang, out string[] r) ? r : Table["en"];
            for (int i = 0; i < Keys.Length; i++) texts[Prefix + Keys[i]] = rows[i];
            foreach (var alias in Aliases) texts[Prefix + alias.Key] = rows[Array.IndexOf(Keys, alias.Value)];
            texts[Prefix + "live_empty"] = "";
            foreach (var kv in _live) texts[kv.Key] = kv.Value;
        }

        [HarmonyLib.HarmonyPatch(typeof(LocalizedTextManager), nameof(LocalizedTextManager.loadLocalizedText))]
        static class FillAfterLoad
        {
            static void Postfix(string pLocaleID) => Fill(pLocaleID);
        }

        public static string Id(string key) => Prefix + key;

        public static bool Has(string key) => Array.IndexOf(Keys, key) >= 0;

        public static string T(string key, params object[] args) => Format(Current(key), args);

        public static string En(string key, params object[] args) => Format(English(key), args);

        static string Current(string key)
        {
            Dictionary<string, string> texts = LocalizedTextManager.instance != null ? Texts(LocalizedTextManager.instance) : null;
            if (texts != null && texts.TryGetValue(Prefix + key, out string s) && !string.IsNullOrEmpty(s)) return s;
            return English(key);
        }

        static string English(string key)
        {
            int i = Array.IndexOf(Keys, key);
            return i >= 0 ? Table["en"][i] : key;
        }

        static string Format(string s, object[] args)
        {
            if (args == null || args.Length == 0) return s;
            try { return string.Format(s, args); }
            catch (FormatException) { return s; }
        }

        public static void Live(LocalizedText label, string liveKey, string text)
        {
            if (label == null || LocalizedTextManager.instance == null) return;
            Dictionary<string, string> texts = Texts(LocalizedTextManager.instance);
            string id = Prefix + "live_" + liveKey;
            if (texts.TryGetValue(id, out string was) && was == text && label.key == id) return;
            texts[id] = text;
            _live[id] = text;
            label.setKeyAndUpdate(id);
        }
    }
}
