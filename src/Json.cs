using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldBoxMP
{
    static class Json
    {
        public static string Write(object o)
        {
            var sw = new StringWriter();
            JsonHelper.writer.Serialize(sw, o);
            return sw.ToString();
        }

        public static T Read<T>(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)))
                return JsonHelper.reader.Deserialize<T>(reader);
        }

        static JsonSerializer _all;
        static JsonSerializer _populate;

        static JsonSerializer All => _all ?? (_all = JsonSerializer.Create(new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Include,
        }));

        static JsonSerializer Populater
        {
            get
            {
                if (_populate != null) return _populate;
                var s = new JsonSerializerSettings
                {
                    DefaultValueHandling = DefaultValueHandling.Include,
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                };
                foreach (JsonConverter c in JsonHelper.read_settings.Converters) s.Converters.Add(c);
                return _populate = JsonSerializer.Create(s);
            }
        }

        public static Dictionary<string, int> Fields(object o, out JObject json)
        {
            json = JObject.FromObject(o, All);
            var d = new Dictionary<string, int>(json.Count);
            foreach (JProperty p in json.Properties()) d[p.Name] = p.Value.ToString(Formatting.None).GetHashCode();
            return d;
        }

        public static string Changed(JObject json, Dictionary<string, int> now, Dictionary<string, int> old, ICollection<string> skip, List<string> names)
        {
            JObject part = null;
            foreach (var kv in now)
            {
                if (skip != null && skip.Contains(kv.Key)) continue;
                if (old != null && old.TryGetValue(kv.Key, out int was) && was == kv.Value) continue;
                if (part == null) part = new JObject();
                part[kv.Key] = json[kv.Key];
                names?.Add(kv.Key);
            }
            return part?.ToString(Formatting.None);
        }

        public static void Populate(string json, object target)
        {
            using (var reader = new JsonTextReader(new StringReader(json)))
                Populater.Populate(reader, target);
        }
    }
}
