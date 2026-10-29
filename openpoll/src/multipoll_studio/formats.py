"""Display formats for register/coil values, and value parsing for writes.

Multi-register formats store bytes in big-endian order within each register.
The reversed-word variants reverse the order of the 16-bit registers.
"""
from __future__ import annotations

import math
import struct
from enum import Enum


class DisplayFormat(str, Enum):
    SIGNED = "signed"
    UNSIGNED = "unsigned"
    HEX = "hex"
    BINARY = "binary"
    LONG = "long"
    LONG_INVERSE = "long_inverse"
    FLOAT = "float"
    FLOAT_INVERSE = "float_inverse"
    DOUBLE = "double"
    DOUBLE_INVERSE = "double_inverse"

    @property
    def label(self) -> str:
        return _LABELS[self]

    @property
    def width(self) -> int:
        """Number of 16-bit registers one value occupies."""
        if self in (DisplayFormat.LONG, DisplayFormat.LONG_INVERSE, DisplayFormat.FLOAT, DisplayFormat.FLOAT_INVERSE):
            return 2
        if self in (DisplayFormat.DOUBLE, DisplayFormat.DOUBLE_INVERSE):
            return 4
        return 1

    @property
    def inverse(self) -> bool:
        return self.value.endswith("_inverse")


_LABELS = {
    DisplayFormat.SIGNED: "Signed 16-bit",
    DisplayFormat.UNSIGNED: "Unsigned 16-bit",
    DisplayFormat.HEX: "Hex 16-bit",
    DisplayFormat.BINARY: "Binary 16-bit",
    DisplayFormat.LONG: "Signed 32-bit (high word first)",
    DisplayFormat.LONG_INVERSE: "Signed 32-bit (low word first)",
    DisplayFormat.FLOAT: "Float 32-bit (high word first)",
    DisplayFormat.FLOAT_INVERSE: "Float 32-bit (low word first)",
    DisplayFormat.DOUBLE: "Float 64-bit (high word first)",
    DisplayFormat.DOUBLE_INVERSE: "Float 64-bit (low word first)",
}


def _words_to_bytes(words: list[int], inverse: bool) -> bytes:
    ordered = list(reversed(words)) if inverse else words
    return struct.pack(f">{len(ordered)}H", *ordered)


def _bytes_to_words(data: bytes, inverse: bool) -> list[int]:
    words = list(struct.unpack(f">{len(data) // 2}H", data))
    return list(reversed(words)) if inverse else words


def decode(words: list[int], fmt: DisplayFormat) -> int | float:
    """Decode ``fmt.width`` registers into one value."""
    if len(words) != fmt.width:
        raise ValueError(f"{fmt.label} needs {fmt.width} registers, got {len(words)}")
    w = words[0]
    if fmt in (DisplayFormat.UNSIGNED, DisplayFormat.HEX, DisplayFormat.BINARY):
        return w
    if fmt is DisplayFormat.SIGNED:
        return w - 0x10000 if w & 0x8000 else w
    raw = _words_to_bytes(words, fmt.inverse)
    if fmt in (DisplayFormat.LONG, DisplayFormat.LONG_INVERSE):
        return struct.unpack(">i", raw)[0]
    if fmt in (DisplayFormat.FLOAT, DisplayFormat.FLOAT_INVERSE):
        return struct.unpack(">f", raw)[0]
    return struct.unpack(">d", raw)[0]


def encode(value: int | float, fmt: DisplayFormat) -> list[int]:
    """Encode one value into ``fmt.width`` registers. Raises ValueError when out of range."""
    if fmt in (DisplayFormat.UNSIGNED, DisplayFormat.HEX, DisplayFormat.BINARY):
        v = int(value)
        if not 0 <= v <= 0xFFFF:
            raise ValueError(f"{v} is outside 0..65535")
        return [v]
    if fmt is DisplayFormat.SIGNED:
        v = int(value)
        if not -32768 <= v <= 32767:
            raise ValueError(f"{v} is outside -32768..32767")
        return [v & 0xFFFF]
    if fmt in (DisplayFormat.LONG, DisplayFormat.LONG_INVERSE):
        v = int(value)
        if not -(2**31) <= v <= 2**31 - 1:
            raise ValueError(f"{v} is outside the 32-bit signed range")
        raw = struct.pack(">i", v)
    elif fmt in (DisplayFormat.FLOAT, DisplayFormat.FLOAT_INVERSE):
        try:
            raw = struct.pack(">f", float(value))
        except OverflowError:
            raise ValueError(f"{value} does not fit a 32-bit float") from None
    else:
        raw = struct.pack(">d", float(value))
    return _bytes_to_words(raw, fmt.inverse)


def format_value(value: int | float, fmt: DisplayFormat) -> str:
    if fmt is DisplayFormat.HEX:
        return f"0x{int(value):04X}"
    if fmt is DisplayFormat.BINARY:
        v = int(value)
        return " ".join(f"{(v >> s) & 0xF:04b}" for s in (12, 8, 4, 0))
    if fmt in (DisplayFormat.FLOAT, DisplayFormat.FLOAT_INVERSE):
        return _format_float(float(value), 7)
    if fmt in (DisplayFormat.DOUBLE, DisplayFormat.DOUBLE_INVERSE):
        return _format_float(float(value), 15)
    return str(int(value))


def _format_float(v: float, digits: int) -> str:
    if math.isnan(v):
        return "NaN"
    if math.isinf(v):
        return "Inf" if v > 0 else "-Inf"
    return f"{v:.{digits}g}"


def parse_value(text: str, fmt: DisplayFormat) -> int | float:
    """Parse user input for ``fmt``. Hex and binary accept optional 0x / 0b prefixes and spaces."""
    t = text.strip().replace(" ", "").replace("_", "")
    if not t:
        raise ValueError("empty value")
    if fmt is DisplayFormat.HEX:
        return int(t[2:] if t.lower().startswith("0x") else t, 16)
    if fmt is DisplayFormat.BINARY:
        return int(t[2:] if t.lower().startswith("0b") else t, 2)
    if fmt in (DisplayFormat.FLOAT, DisplayFormat.FLOAT_INVERSE, DisplayFormat.DOUBLE, DisplayFormat.DOUBLE_INVERSE):
        return float(t)
    return int(t, 0)


def parse_coil(text: str) -> int:
    t = text.strip().lower()
    if t in ("1", "on", "true"):
        return 1
    if t in ("0", "off", "false"):
        return 0
    raise ValueError(f"coil value must be 0 or 1, got {text!r}")
