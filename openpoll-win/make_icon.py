"""Generate openpoll.ico (16/32/48 px): a register grid with a signal trace. Run once; the .ico is committed."""
import struct
import zlib
from pathlib import Path


def png(size: int) -> bytes:
    bg, grid, cell, trace = (0x1F, 0x4E, 0x79, 255), (0xE8, 0xEE, 0xF4, 255), (0xFF, 0xFF, 0xFF, 255), (0xF2, 0x8C, 0x28, 255)
    px = [[(0, 0, 0, 0)] * size for _ in range(size)]
    r = max(2, size // 8)
    for y in range(size):
        for x in range(size):
            # rounded square background
            cx = min(x, size - 1 - x)
            cy = min(y, size - 1 - y)
            if cx < r and cy < r and (r - cx - 0.5) ** 2 + (r - cy - 0.5) ** 2 > r * r:
                continue
            px[y][x] = bg
    m = size // 6
    cols, rows = 3, 3
    gw = (size - 2 * m)
    for i in range(rows):
        for j in range(cols):
            x0 = m + j * gw // cols + 1
            x1 = m + (j + 1) * gw // cols - 1
            y0 = m + i * gw // rows + 1
            y1 = m + (i + 1) * gw // rows - 1
            for y in range(y0, y1):
                for x in range(x0, x1):
                    px[y][x] = cell if (i + j) % 2 == 0 else grid
    # step-shaped trace across the grid
    pts = [(m, size * 2 // 3), (size // 3, size * 2 // 3), (size // 3, size // 3), (size * 2 // 3, size // 3),
           (size * 2 // 3, size * 2 // 3), (size - m, size * 2 // 3)]
    w = max(1, size // 16)
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        for t in range(0, 101):
            x = round(x0 + (x1 - x0) * t / 100)
            y = round(y0 + (y1 - y0) * t / 100)
            for dy in range(-w, w + 1):
                for dx in range(-w, w + 1):
                    if 0 <= x + dx < size and 0 <= y + dy < size:
                        px[y + dy][x + dx] = trace
    raw = b"".join(b"\x00" + b"".join(bytes(p) for p in row) for row in px)

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


sizes = [16, 32, 48]
images = [png(s) for s in sizes]
header = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
entries = b""
for s, img in zip(sizes, images):
    entries += struct.pack("<BBBBHHII", s, s, 0, 0, 1, 32, len(img), offset)
    offset += len(img)
Path(__file__).with_name("openpoll.ico").write_bytes(header + entries + b"".join(images))
print("openpoll.ico", offset, "bytes")
