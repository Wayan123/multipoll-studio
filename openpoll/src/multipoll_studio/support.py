"""Data logging, traffic formatting and frame console helpers."""
from __future__ import annotations

import csv
import io
import time
from dataclasses import dataclass
from enum import Enum
from pathlib import Path

from . import framing
from .engine import PollResult
from .formats import DisplayFormat, decode, format_value
from .model import ViewOptions, address_label
from .protocol import Status

# --- communication traffic -----------------------------------------------------

def hex_bytes(data: bytes) -> str:
    return " ".join(f"{b:02X}" for b in data)


def traffic_line(seq: int, direction: str, data: bytes) -> str:
    """Format one traffic record with a six-digit sequence and direction."""
    return f"{seq % 1_000_000:06d} | {direction.upper()} | {hex_bytes(data)}"


# --- Frame Console -----------------------------------------------------------------

FRAME_LIST_HEADER = "MultiPoll Studio Frame List"


class CheckKind(str, Enum):
    NONE = "none"
    CRC = "crc"
    LRC = "lrc"


def parse_hex_string(text: str) -> bytes:
    """Parse hex bytes separated by ',', '.', or whitespace."""
    tokens = text.replace(",", " ").replace(".", " ").split()
    out = bytearray()
    for tok in tokens:
        t = tok[2:] if tok.lower().startswith("0x") else tok
        if not 1 <= len(t) <= 2:
            raise ValueError(f"{tok!r} is not a byte")
        out.append(int(t, 16))
    if not out:
        raise ValueError("no bytes entered")
    return bytes(out)


def add_check(data: bytes, kind: CheckKind) -> bytes:
    """Append CRC (RTU) or LRC+CRLF (ASCII, computed over the hex-decoded body after ':')."""
    if kind is CheckKind.CRC:
        crc = framing.crc16(data)
        return data + bytes([crc & 0xFF, crc >> 8])
    if kind is CheckKind.LRC:
        if not data.startswith(b":"):
            raise ValueError("ASCII frames must start with 3A (':')")
        body = bytes.fromhex(data[1:].decode("ascii"))
        return data + f"{framing.lrc(body):02X}".encode() + b"\r\n"
    return data


def save_test_list(path: str | Path, entries: list[str]) -> None:
    Path(path).write_text("\n".join([FRAME_LIST_HEADER, *entries]) + "\n", encoding="utf-8")


def load_test_list(path: str | Path) -> list[str]:
    lines = Path(path).read_text(encoding="utf-8").splitlines()
    if not lines or lines[0].strip() != FRAME_LIST_HEADER:
        raise ValueError(f"first line must be {FRAME_LIST_HEADER!r}")
    return [ln.strip() for ln in lines[1:] if ln.strip()]


# --- value rendering shared by the grid and logs ---------------------------------------

def render_values(result: PollResult, view: ViewOptions) -> list[tuple[int, str]]:
    """Return (offset, text) for every displayed cell of one poll result.

    Multi-register formats occupy their first offset; the following offsets are
    reported with an empty string, as in the original grid.
    """
    values = result.values or []
    if result.definition.is_bits:
        return [(i, str(v)) for i, v in enumerate(values)]
    out: list[tuple[int, str]] = []
    i = 0
    while i < len(values):
        fmt = view.format_at(i)
        if fmt.width > 1 and i + fmt.width <= len(values):
            out.append((i, format_value(decode(values[i:i + fmt.width], fmt), fmt)))
            out.extend((i + k, "") for k in range(1, fmt.width))
            i += fmt.width
            continue
        if fmt.width > 1:
            fmt = DisplayFormat.SIGNED if fmt in (DisplayFormat.LONG, DisplayFormat.LONG_INVERSE) \
                else DisplayFormat.HEX  # trailing register without a partner
        out.append((i, format_value(decode([values[i]], fmt), fmt)))
        i += 1
    return out


# --- text / CSV data log ------------------------------------------------------------------

class Delimiter(str, Enum):
    FIXED = "fixed"
    COMMA = "comma"
    TAB = "tab"


@dataclass
class LogSettings:
    path: str
    each_poll: bool = True
    rate_s: int = 1
    delimiter: Delimiter = Delimiter.FIXED
    log_errors: bool = True
    log_date: bool = False
    changed_only: bool = False


class DataLogger:
    """Writes one line per logged poll. Not thread-safe; call from one thread."""

    def __init__(self, settings: LogSettings, view: ViewOptions):
        self.settings = settings
        self.view = view
        self._file = open(settings.path, "a", encoding="utf-8", newline="")
        self._last_logged = 0.0
        self._last_values: list[int] | None = None
        self.lines_written = 0

    def close(self) -> None:
        self._file.close()

    def log(self, result: PollResult) -> bool:
        s = self.settings
        if result.status is not Status.OK and not s.log_errors:
            return False
        if not s.each_poll and result.timestamp - self._last_logged < s.rate_s:
            return False
        if s.changed_only and result.status is Status.OK and result.values == self._last_values:
            return False
        stamp = time.strftime("%Y-%m-%d %H:%M:%S" if s.log_date else "%H:%M:%S", time.localtime(result.timestamp))
        d = result.definition
        first = address_label(d.function, d.address, self.view.plc_addresses)
        if result.status is Status.OK:
            cells = [text for _, text in render_values(result, self.view) if text != ""]
        else:
            cells = [result.status.text]
        if s.delimiter is Delimiter.FIXED:
            line = f"{stamp} <{first}> " + "".join(f"{c:>7}" if len(c) < 7 else f" {c}" for c in cells)
        else:
            buf = io.StringIO()
            csv.writer(buf, delimiter="," if s.delimiter is Delimiter.COMMA else "\t",
                       lineterminator="").writerow([stamp, first, *cells])
            line = buf.getvalue()
        self._file.write(line + "\n")
        self._file.flush()
        self._last_logged = result.timestamp
        if result.status is Status.OK:
            self._last_values = list(result.values or [])
        self.lines_written += 1
        return True
