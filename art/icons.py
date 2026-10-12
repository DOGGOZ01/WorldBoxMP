"""16x16 pixel icons for WorldBoxMP, drawn as text. Renders a preview and emits C#."""
import sys, os
import zlib, struct

from minipng import Image

PAL = {
    '.': None,
    'k': (26, 20, 18), 'w': (255, 255, 255), 'l': (206, 206, 206), 'g': (128, 128, 128),
    'y': (255, 214, 64), 'o': (243, 150, 31), 'O': (186, 98, 18),
    'r': (226, 70, 58), 'd': (150, 40, 34),
    'b': (86, 170, 255), 'B': (40, 96, 176), 'c': (170, 220, 255),
    'G': (104, 204, 74), 'n': (46, 132, 56),
    'p': (244, 228, 186), 'P': (204, 172, 116), 'm': (140, 90, 44), 'M': (96, 58, 26),
    's': (255, 196, 150), 'S': (214, 140, 98),
}

ICONS = {
# two players: orange (host) and blue (second player)
'tab': [
"................",
"...kkk....kkk...",
"..kssSk..kssSk..",
"..kssSk..kssSk..",
"..kSSSk..kSSSk..",
"...kkk....kkk...",
"..kooOk..kbbBk..",
".kooooOkkbbbbBk.",
".koooooOkbbbbbk.",
".koOoooOkbBbbbk.",
".kkOoooOkbBbbkk.",
"..kOoooOkbBbbk..",
"..kooOOkkbbBBk..",
"..kkkkk..kkkkk..",
"................",
"................",
],
# host: the world with a flag on it
'host': [
"........kk......",
"........kykkk...",
"........kyyyyk..",
"........kyyykk..",
"....kkkkkkkk....",
"..kkbbGGkbbbkk..",
".kbbbGGGkbbbbbk.",
".kbbGGGGGbbGGbk.",
"kbbbbGGGbbGGGGbk",
"kbbbbbGbbbbGGbbk",
"kBbbbbbbbbbbbbBk",
"kBBbbGGbbbbbbBBk",
".kBBbGGGbbbbBBk.",
".kkBBBGBbbBBBkk.",
"...kkBBBBBBkk...",
".....kkkkkk.....",
],
# invite: envelope with a green plus
'invite': [
"................",
"................",
"kkkkkkkkkkkkkk..",
"kPppppppppppPk..",
"kpPppppppppPpk..",
"kppPppppppPppk..",
"kpppPppppPpppk..",
"kppppPppPppppk..",
"kpppppPPpppppk..",
"kpppppppppkkkkk.",
"kppppppppkGGwGGk",
"kppppppppkGGwGGk",
"kppppppppkwwwwwk",
"kPPPPPPPPkGGwGGk",
"kkkkkkkkkkGGwGGk",
"..........kkkkk.",
],
# join: a green arrow into the world
'join': [
"................",
"........kkkkkkkk",
"........kMmmmmMk",
"........kMmmmmMk",
"....k...kMmmmmMk",
"....kGk.kMmmmmMk",
"kkkkkGGkkMmmmmMk",
"kGGGGGGGGMmmmyMk",
"kkkkkGGkkMmmmyMk",
"....kGk.kMmmmmMk",
"....k...kMmmmmMk",
"........kMmmmmMk",
"........kMmmmmMk",
".......kkkkkkkkk",
"................",
"................",
],
# leave: a door with a red arrow out
'leave': [
"................",
"..kkkkkkkk......",
"..kMmmmmMk......",
"..kMmmmmMk......",
"..kMmmmmMk..k...",
"..kMmmmmMk..kk..",
"..kMmmmyMkkkrrk.",
"..kMmmmyMrrrrrrk",
"..kMmmmmMkkkrrk.",
"..kMmmmmMk..kk..",
"..kMmmmmMk..k...",
"..kMmmmmMk......",
"..kMmmmmMk......",
".kkkkkkkkkk.....",
"................",
"................",
],
# reload the world: two arrows in a circle
'reload': [
"................",
"....k....kkkkk..",
"...kbk...kGGGk..",
"..kbbbk..kGGGk..",
".kbbbbbk.kGGGk..",
".kkbbbkk.kGGGk..",
"..kbbbk..kGGGk..",
"..kbbbk..kGGGk..",
"..kbbbk..kGGGk..",
"..kbbbk..kGGGk..",
"..kbbbk.kkGGGkk.",
"..kbbbk.kGGGGGk.",
"..kbbbk..kGGGk..",
"..kbbbk...kGk...",
"..kkkkk....k....",
"................",
],
# logs: a scroll with lines
'logs': [
"................",
"...kkkkkkkkkk...",
"..kPppppppppPk..",
"..kPkkkkkkkpPk..",
"...kpppppppkk...",
"...kpkkkkkpk....",
"...kpppppppk....",
"...kpkkkkpkk....",
"...kpppppppk....",
"...kpkkkkkpk....",
"...kpppppppk....",
"..kkkkkkkkkkk...",
"..kPppppppppPk..",
"..kPPPPPPPPPPk..",
"...kkkkkkkkkk...",
"................",
],
# toggle on: a green tick in a box
'on': [
"................",
"................",
"..kkkkkkkkkkkk..",
"..kllllllllllk..",
"..kggggggggggk..",
"..kggggggggGgk..",
"..kgggggggGngk..",
"..kggggggGnggk..",
"..kgGgggGngggk..",
"..kgnGgGnggggk..",
"..kggnGngggggk..",
"..kgggnggggggk..",
"..kggggggggggk..",
"..kkkkkkkkkkkk..",
"................",
"................",
],
# toggle off: an empty box with a red cross
'off': [
"................",
"................",
"..kkkkkkkkkkkk..",
"..kllllllllllk..",
"..kgrggggggrgk..",
"..kggrggggrggk..",
"..kgggrggrgggk..",
"..kggggrrggggk..",
"..kggggrrggggk..",
"..kgggrggrgggk..",
"..kggrggggrggk..",
"..kgrggggggrgk..",
"..kggggggggggk..",
"..kkkkkkkkkkkk..",
"................",
"................",
],
'gear': [
"................",
".......kk.......",
"...kk.kllk.kk...",
"..kllkllllkllk..",
"..kllllllllllk..",
"...klllkklllk...",
"..klllk..klllk..",
".klllk....klllk.",
".kgggk....kgggk.",
"..kgggk..kgggk..",
"...kgggkkgggk...",
"..kggggggggggk..",
"..kggkggggkggk..",
"...kk.kggk.kk...",
".......kk.......",
"................",
],
'key': [
"................",
"................",
"................",
"....kk..........",
"..kkyykk........",
".kyyyyyyk.......",
".kyykkyykkkkkkk.",
"kyyk..kyyyyyyyyk",
"kyyk..kyOOOOOOOk",
".kyykkyykkkkOkOk",
".kyyyyyyk..kOkOk",
"..kkyykk....k.k.",
"....kk..........",
"................",
"................",
"................",
],
'cross': [
"................",
"................",
"...kk......kk...",
"..krrk....krrk..",
"...krrk..krrk...",
"....krrkkrrk....",
".....krrrrk.....",
"......krrk......",
".....krrrrk.....",
"....krrkkrrk....",
"...krrk..krrk...",
"..krrk....krrk..",
".krrk......krrk.",
"..kk........kk..",
"................",
"................",
]
}

def check():
    for name, rows in ICONS.items():
        assert len(rows) == 16, (name, len(rows))
        for i, r in enumerate(rows):
            assert len(r) == 16, (name, i, len(r), r)
            for ch in r: assert ch in PAL, (name, ch)

def preview(path, scale=8):
    names = list(ICONS)
    img = Image.new('RGBA', (len(names) * 18 * scale, 18 * scale), (60, 50, 44, 255))
    for n, name in enumerate(names):
        for y, row in enumerate(ICONS[name]):
            for x, ch in enumerate(row):
                c = PAL[ch]
                if c is None: continue
                for dy in range(scale):
                    for dx in range(scale):
                        img.putpixel(((n * 18 + 1 + x) * scale + dx, (1 + y) * scale + dy), c + (255,))
    img.save(path)

def csharp():
    out = ["        // generated by art/icons.py: 16x16, rows top to bottom", "        static readonly Dictionary<char, Color32> Palette = new Dictionary<char, Color32>", "        {"]
    for k, v in PAL.items():
        if v is None: continue
        out.append("            { '%s', new Color32(%d, %d, %d, 255) }," % (k, v[0], v[1], v[2]))
    out.append("        };")
    out.append("        static readonly Dictionary<string, string[]> Art = new Dictionary<string, string[]>")
    out.append("        {")
    for name, rows in ICONS.items():
        out.append('            { "%s", new[] {' % name)
        for r in rows: out.append('                "%s",' % r)
        out.append('            } },')
    out.append("        };")
    return "\n".join(out)

if __name__ == '__main__':
    check()
    preview(sys.argv[1])
    if len(sys.argv) > 2:
        open(sys.argv[2], 'w').write(csharp())
    print('ok')
