import re
import sys

from strings import KEYS, L

GAME = ["en", "de", "pl", "cz", "ch", "ko", "ja", "th", "vn", "ru", "ua", "by", "es", "br", "pt", "fr", "it", "ro", "hr",
        "tr", "gr", "ka", "sk", "cs", "hu", "nl", "id", "sv", "no", "da", "fa", "ar", "he", "fn", "ph", "hi", "lb", "lt"]

errors = []
missing = [g for g in GAME if g not in L]
if missing: errors.append("missing languages: " + ", ".join(missing))
for lang, rows in L.items():
    if len(rows) != len(KEYS):
        errors.append("%s: %d strings, want %d" % (lang, len(rows), len(KEYS)))
        continue
    for key, en, text in zip(KEYS, L["en"], rows):
        want = sorted(re.findall(r"\{\d\}", en))
        got = sorted(re.findall(r"\{\d\}", text))
        if want != got: errors.append("%s.%s: placeholders %s, want %s" % (lang, key, got, want))
        if not text.strip(): errors.append("%s.%s: empty" % (lang, key))
if errors:
    print("\n".join(errors))
    sys.exit(1)


def cs(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


out = []
out.append("using System.Collections.Generic;")
out.append("")
out.append("namespace WorldBoxMP")
out.append("{")
out.append("    public static partial class Lang")
out.append("    {")
out.append("        static readonly string[] Keys =")
out.append("        {")
for i in range(0, len(KEYS), 6):
    out.append("            " + ", ".join(cs(k) for k in KEYS[i:i + 6]) + ",")
out.append("        };")
out.append("")
out.append("        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>")
out.append("        {")
for lang in GAME:
    out.append("            { %s, new[] {" % cs(lang))
    for t in L[lang]:
        out.append("                " + cs(t) + ",")
    out.append("            } },")
out.append("        };")
out.append("    }")
out.append("}")
open(sys.argv[1], "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
print("ok: %d languages x %d strings" % (len(GAME), len(KEYS)))
