import struct
import zlib


class Image:
    def __init__(self, w, h, bg):
        self.w, self.h = w, h
        self.px = [tuple(bg) for _ in range(w * h)]

    @staticmethod
    def new(mode, size, bg):
        return Image(size[0], size[1], bg)

    def putpixel(self, xy, c):
        self.px[xy[1] * self.w + xy[0]] = tuple(c)

    def save(self, path):
        rows = []
        for y in range(self.h):
            row = bytearray([0])
            for p in self.px[y * self.w:(y + 1) * self.w]:
                row.extend(p)
            rows.append(bytes(row))
        raw = b"".join(rows)

        def chunk(t, d):
            return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

        header = struct.pack(">IIBBBBB", self.w, self.h, 8, 6, 0, 0, 0)
        with open(path, "wb") as f:
            f.write(bytes([137, 80, 78, 71, 13, 10, 26, 10]))
            f.write(chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
