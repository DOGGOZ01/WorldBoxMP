using System.IO;
using Newtonsoft.Json;

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
    }
}
