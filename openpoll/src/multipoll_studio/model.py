"""Poll window model: read definition, display options and address labels."""
from __future__ import annotations

from dataclasses import asdict, dataclass, field, fields

from .formats import DisplayFormat
from .protocol import BIT_FUNCTIONS, FC, MAX_READ_BITS, MAX_READ_REGISTERS, READ_FUNCTIONS

# PLC reference prefix per read function: 0xxxx coils, 1xxxx inputs, 3xxxx input regs, 4xxxx holding.
PLC_PREFIX = {FC.READ_COILS: 0, FC.READ_DISCRETE_INPUTS: 1, FC.READ_INPUT_REGISTERS: 3,
              FC.READ_HOLDING_REGISTERS: 4}

FUNCTION_LABELS = {
    FC.READ_COILS: "01 Read Coils (0x)",
    FC.READ_DISCRETE_INPUTS: "02 Read Discrete Inputs (1x)",
    FC.READ_HOLDING_REGISTERS: "03 Read Holding Registers (4x)",
    FC.READ_INPUT_REGISTERS: "04 Read Input Registers (3x)",
}

ROW_CHOICES = (10, 20, 50, 100)


@dataclass
class PollDefinition:
    slave_id: int = 1
    function: int = FC.READ_HOLDING_REGISTERS
    address: int = 0  # protocol address (base 0)
    quantity: int = 10
    scan_rate_ms: int = 1000
    enabled: bool = True

    @property
    def is_bits(self) -> bool:
        return self.function in BIT_FUNCTIONS

    @property
    def writable(self) -> bool:
        return self.function in (FC.READ_COILS, FC.READ_HOLDING_REGISTERS)

    def validate(self) -> None:
        if not 0 <= self.slave_id <= 255:
            raise ValueError("Slave ID must be 0..255")
        if self.function not in READ_FUNCTIONS:
            raise ValueError("Function must be 01, 02, 03 or 04")
        limit = MAX_READ_BITS if self.is_bits else MAX_READ_REGISTERS
        if not 1 <= self.quantity <= limit:
            raise ValueError(f"Quantity must be 1..{limit} for this function")
        if not 0 <= self.address <= 0xFFFF or self.address + self.quantity > 0x10000:
            raise ValueError("Address + quantity must stay within 0..65535")
        if not 1 <= self.scan_rate_ms <= 3_600_000:
            raise ValueError("Scan rate must be 1..3600000 ms")


@dataclass
class ViewOptions:
    display: DisplayFormat = DisplayFormat.SIGNED
    plc_addresses: bool = False
    rows: int = 10
    hide_alias: bool = False
    address_in_cell: bool = False
    cell_formats: dict[int, DisplayFormat] = field(default_factory=dict)  # offset -> format override

    def format_at(self, offset: int) -> DisplayFormat:
        return self.cell_formats.get(offset, self.display)


@dataclass
class PollDocument:
    definition: PollDefinition = field(default_factory=PollDefinition)
    view: ViewOptions = field(default_factory=ViewOptions)
    aliases: dict[int, str] = field(default_factory=dict)  # offset -> alias text

    def to_dict(self) -> dict:
        view = asdict(self.view)
        view["display"] = self.view.display.value
        view["cell_formats"] = {str(k): v.value for k, v in self.view.cell_formats.items()}
        return {
            "format": "multipoll-document",
            "version": 1,
            "definition": asdict(self.definition),
            "view": view,
            "aliases": {str(k): v for k, v in self.aliases.items() if v},
        }

    @classmethod
    def from_dict(cls, data: dict) -> "PollDocument":
        if data.get("format") != "multipoll-document":
            raise ValueError("not a MultiPoll Studio document")
        d = data.get("definition", {})
        definition = PollDefinition(**{f.name: d[f.name] for f in fields(PollDefinition) if f.name in d})
        v = data.get("view", {})
        view = ViewOptions(**{f.name: v[f.name] for f in fields(ViewOptions)
                              if f.name in v and f.name not in ("display", "cell_formats")})
        if "display" in v:
            view.display = DisplayFormat(v["display"])
        view.cell_formats = {int(k): DisplayFormat(f) for k, f in v.get("cell_formats", {}).items()}
        if view.rows not in ROW_CHOICES:
            view.rows = 10
        definition.validate()
        return cls(definition, view, {int(k): str(a) for k, a in data.get("aliases", {}).items()})


def address_label(function: int, address: int, plc: bool) -> str:
    """Address text as shown by Modbus Poll: ``40001`` (PLC, base 1) or ``00000`` (protocol, base 0)."""
    if not plc:
        return f"{address:05d}"
    number = address + 1
    width = 4 if number <= 9999 else 5
    return f"{PLC_PREFIX[FC(function)]}{number:0{width}d}"


def parse_plc_address(text: str) -> tuple[int, int]:
    """Parse ``40001``/``4x0001``/``400001`` style references into (function, protocol address)."""
    t = text.strip().lower().replace("x", "")
    if not t.isdigit() or len(t) < 2:
        raise ValueError(f"not a PLC address: {text!r}")
    prefix, number = int(t[0]), int(t[1:])
    by_prefix = {v: k for k, v in PLC_PREFIX.items()}
    if prefix not in by_prefix or not 1 <= number <= 0x10000:
        raise ValueError(f"not a PLC address: {text!r}")
    return int(by_prefix[prefix]), number - 1
