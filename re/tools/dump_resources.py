"""Dump menus, dialogs and string tables from a PE32 image to plain text.

Usage: python re/tools/dump_resources.py re/artifacts/mbpoll.exe re/out
"""
import struct
import sys
from pathlib import Path

import pefile

RT_MENU, RT_DIALOG, RT_STRING = 4, 5, 6


def utf16z(buf, off):
    end = off
    while buf[end:end + 2] != b"\x00\x00":
        end += 2
    return buf[off:end].decode("utf-16le"), end + 2


def sz_or_ord(buf, off):
    if buf[off:off + 2] == b"\xff\xff":
        return f"#{struct.unpack_from('<H', buf, off + 2)[0]}", off + 4
    if buf[off:off + 2] == b"\x00\x00":
        return "", off + 2
    return utf16z(buf, off)


def align4(off):
    return (off + 3) & ~3


def parse_menu(buf):
    """Standard MENUTEMPLATE: MF_POPUP=0x10 opens a level, MF_END=0x80 closes one."""
    out = []

    def level(off, depth):
        while True:
            flags = struct.unpack_from("<H", buf, off)[0]
            off += 2
            popup = flags & 0x10
            mid = None
            if not popup:
                mid = struct.unpack_from("<H", buf, off)[0]
                off += 2
            text, off = utf16z(buf, off)
            label = text.replace("\t", "  [") + ("]" if "\t" in text else "")
            if popup:
                out.append("  " * depth + f"{label} >")
                off = level(off, depth + 1)
            else:
                out.append("  " * depth + (f"{label} (id={mid})" if label else "----"))
            if flags & 0x80:
                return off

    level(4, 0)  # skip MENUHEADER (version, header size)
    return out


def parse_dialog(buf):
    lines = []
    if struct.unpack_from("<HH", buf, 0) == (1, 0xFFFF):  # DLGTEMPLATEEX
        _, _, _, _, style, count, x, y, cx, cy = struct.unpack_from("<HHIIIHhhhh", buf, 0)
        off = 26
        ex = True
    else:
        style, _, count, x, y, cx, cy = struct.unpack_from("<IIHhhhh", buf, 0)
        off = 18
        ex = False
    menu, off = sz_or_ord(buf, off)
    cls, off = sz_or_ord(buf, off)
    title, off = utf16z(buf, off)
    if style & 0x40:  # DS_SETFONT
        off += 6 if ex else 2
        _, off = utf16z(buf, off)
    lines.append(f'DIALOG "{title}" {cx}x{cy}')
    for _ in range(count):
        off = align4(off)
        if ex:
            _, _, cstyle, cx_, cy_, w, h, cid = struct.unpack_from("<IIIhhhhI", buf, off)
            off += 24
        else:
            cstyle, _, cx_, cy_, w, h, cid = struct.unpack_from("<IIhhhhH", buf, off)
            off += 18
        ccls, off = sz_or_ord(buf, off)
        ctext, off = sz_or_ord(buf, off)
        extra = struct.unpack_from("<H", buf, off)[0]
        off += 2 + extra
        ccls = {"#128": "BUTTON", "#129": "EDIT", "#130": "STATIC",
                "#131": "LISTBOX", "#132": "SCROLLBAR", "#133": "COMBOBOX"}.get(ccls, ccls)
        lines.append(f"  {ccls:<18} id={cid:<6} '{ctext}'")
    return lines


def parse_strings(block_id, buf):
    out, off = [], 0
    for i in range(16):
        n = struct.unpack_from("<H", buf, off)[0]
        off += 2
        if n:
            out.append(((block_id - 1) * 16 + i, buf[off:off + 2 * n].decode("utf-16le")))
        off += 2 * n
    return out


def main(exe, outdir):
    pe = pefile.PE(exe)
    out = Path(outdir)
    out.mkdir(parents=True, exist_ok=True)
    sections = {RT_MENU: [], RT_DIALOG: [], RT_STRING: []}
    for t in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if t.id not in sections:
            continue
        for n in t.directory.entries:
            for lang in n.directory.entries:
                d = lang.data.struct
                buf = pe.get_data(d.OffsetToData, d.Size)
                if t.id == RT_MENU:
                    sections[t.id].append(f"MENU {n.id}")
                    sections[t.id] += ["  " + s for s in parse_menu(buf)]
                elif t.id == RT_DIALOG:
                    sections[t.id].append(f"\n[{n.id}] " + "\n".join(parse_dialog(buf)))
                else:
                    sections[t.id] += [f"{sid}\t{s!r}" for sid, s in parse_strings(n.id, buf)]
    (out / "menus.txt").write_text("\n".join(sections[RT_MENU]) + "\n")
    (out / "dialogs.txt").write_text("\n".join(sections[RT_DIALOG]) + "\n")
    (out / "strings.txt").write_text("\n".join(sections[RT_STRING]) + "\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
