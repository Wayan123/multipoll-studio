"""Command-line entry point.

``multipoll-studio`` with no subcommand starts the GUI. Subcommands give a scriptable
replacement for Modbus Poll's Windows-only OLE automation interface:

    multipoll-studio read  --tcp 192.168.1.10 -f 3 -a 0 -n 10 --format float
    multipoll-studio write --rtu /dev/ttyUSB0 -b 19200 -a 5 -v 1234
    multipoll-studio poll  --tcp 10.0.0.5 -a 0 -n 4 --rate 500 --count 10
"""
from __future__ import annotations

import argparse
import sys
import time

from . import __version__, protocol
from .formats import DisplayFormat, decode, encode, format_value, parse_coil, parse_value
from .model import address_label
from .protocol import ModbusError
from .support import traffic_line
from .transport import ConnectionKind, ConnectionSettings, ModbusMaster, SerialMode


def _connection_args(p: argparse.ArgumentParser) -> None:
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("--tcp", metavar="HOST", help="Modbus TCP")
    g.add_argument("--udp", metavar="HOST", help="Modbus UDP")
    g.add_argument("--rtu", metavar="PORT", help="Modbus RTU on a serial port")
    g.add_argument("--ascii", metavar="PORT", help="Modbus ASCII on a serial port")
    g.add_argument("--rtu-tcp", metavar="HOST", help="RTU frames over TCP")
    g.add_argument("--rtu-udp", metavar="HOST", help="RTU frames over UDP")
    p.add_argument("-p", "--port", type=int, default=502, help="TCP/UDP port (default 502)")
    p.add_argument("-b", "--baud", type=int, default=9600)
    p.add_argument("--parity", choices="NEO", default="N")
    p.add_argument("--databits", type=int, choices=(7, 8), default=8)
    p.add_argument("--stopbits", type=int, choices=(1, 2), default=1)
    p.add_argument("-t", "--timeout", type=int, default=1000, help="response timeout in ms")
    p.add_argument("-s", "--slave", type=int, default=1, help="slave / unit id (0 = broadcast on serial)")
    p.add_argument("--plc", action="store_true",
                   help="addresses are base 1 (Modbus Poll 'PLC Addresses'); -a 1 means protocol address 0")
    p.add_argument("--trace", action="store_true", help="print Tx/Rx frames to stderr")


def _settings(a: argparse.Namespace) -> ConnectionSettings:
    s = ConnectionSettings(baudrate=a.baud, parity=a.parity, databits=a.databits, stopbits=a.stopbits,
                           tcp_port=a.port, response_timeout_ms=a.timeout)
    for attr, kind, mode in (("tcp", ConnectionKind.TCP, SerialMode.RTU), ("udp", ConnectionKind.UDP, SerialMode.RTU),
                             ("rtu", ConnectionKind.SERIAL, SerialMode.RTU),
                             ("ascii", ConnectionKind.SERIAL, SerialMode.ASCII),
                             ("rtu_tcp", ConnectionKind.SERIAL_OVER_TCP, SerialMode.RTU),
                             ("rtu_udp", ConnectionKind.SERIAL_OVER_UDP, SerialMode.RTU)):
        target = getattr(a, attr)
        if target:
            s.kind, s.serial_mode = kind, mode
            if kind is ConnectionKind.SERIAL:
                s.port = target
            else:
                s.host = target
    return s


def _address(a: argparse.Namespace, raw: int) -> int:
    return raw - 1 if a.plc else raw


def _print_values(a, fc: int, address: int, values: list[int]) -> None:
    fmt = DisplayFormat(a.format)
    step = 1 if fc in protocol.BIT_FUNCTIONS else fmt.width
    for i in range(0, len(values) - step + 1, step):
        label = address_label(fc, address + i, a.plc)
        if fc in protocol.BIT_FUNCTIONS:
            print(f"{label}\t{values[i]}")
        else:
            print(f"{label}\t{format_value(decode(values[i:i + step], fmt), fmt)}")


def cmd_read(a, master: ModbusMaster) -> int:
    address = _address(a, a.address)
    values = master.transact(a.slave, protocol.read_request(a.function, address, a.quantity))
    _print_values(a, a.function, address, values)
    return 0


def cmd_poll(a, master: ModbusMaster) -> int:
    address = _address(a, a.address)
    n = 0
    while a.count == 0 or n < a.count:
        started = time.monotonic()
        stamp = time.strftime("%H:%M:%S")
        try:
            values = master.transact(a.slave, protocol.read_request(a.function, address, a.quantity))
            print(f"-- {stamp} poll {n + 1}")
            _print_values(a, a.function, address, values)
        except ModbusError as exc:
            print(f"-- {stamp} poll {n + 1}: {exc.status.text} {exc.detail}".rstrip())
            if not master.connected:
                master.connect()
        sys.stdout.flush()
        n += 1
        time.sleep(max(0.0, a.rate / 1000 - (time.monotonic() - started)))
    return 0


def cmd_write(a, master: ModbusMaster) -> int:
    address = _address(a, a.address)
    if a.coil:
        bits = [parse_coil(v) for v in a.value]
        pdu = (protocol.write_single_coil(address, bool(bits[0])) if len(bits) == 1 and not a.multiple
               else protocol.write_multiple_coils(address, bits))
    else:
        fmt = DisplayFormat(a.format)
        words = [w for v in a.value for w in encode(parse_value(v, fmt), fmt)]
        pdu = (protocol.write_single_register(address, words[0]) if len(words) == 1 and not a.multiple
               else protocol.write_multiple_registers(address, words))
    master.transact(a.slave, pdu)
    print("Response ok" if a.slave else "Broadcast sent")
    return 0


def main(argv: list[str] | None = None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    if not argv or argv[0] not in ("read", "poll", "write", "-h", "--help", "--version"):
        from .gui.mainwindow import run

        return run([sys.argv[0], *argv])

    parser = argparse.ArgumentParser(prog="multipoll-studio", description="Open-source Modbus master test tool")
    parser.add_argument("--version", action="version", version=f"multipoll-studio {__version__}")
    sub = parser.add_subparsers(dest="cmd", required=True)
    fmt_choices = [f.value for f in DisplayFormat]

    for name, helptext in (("read", "read once"), ("poll", "read repeatedly")):
        p = sub.add_parser(name, help=helptext)
        _connection_args(p)
        p.add_argument("-f", "--function", type=int, choices=(1, 2, 3, 4), default=3)
        p.add_argument("-a", "--address", type=int, default=0)
        p.add_argument("-n", "--quantity", type=int, default=10)
        p.add_argument("--format", choices=fmt_choices, default="signed")
        if name == "poll":
            p.add_argument("--rate", type=int, default=1000, help="scan rate in ms")
            p.add_argument("--count", type=int, default=0, help="number of polls (0 = forever)")

    p = sub.add_parser("write", help="write coils or registers")
    _connection_args(p)
    p.add_argument("-a", "--address", type=int, required=True)
    p.add_argument("-v", "--value", nargs="+", required=True)
    p.add_argument("--coil", action="store_true", help="write coils (05/15) instead of registers (06/16)")
    p.add_argument("--multiple", action="store_true", help="force function 15/16 for a single value")
    p.add_argument("--format", choices=fmt_choices, default="signed")

    a = parser.parse_args(argv)
    seq = [0]

    def trace(direction: str, data: bytes) -> None:
        seq[0] += 1
        print(traffic_line(seq[0], direction, data), file=sys.stderr)

    master = ModbusMaster(_settings(a), on_traffic=trace if a.trace else None)
    try:
        master.connect()
        return {"read": cmd_read, "poll": cmd_poll, "write": cmd_write}[a.cmd](a, master)
    except ModbusError as exc:
        print(f"error: {exc.status.text}" + (f" ({exc.detail})" if exc.detail and exc.detail != exc.status.text else ""),
              file=sys.stderr)
        return 2
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        return 130
    finally:
        master.close()


if __name__ == "__main__":
    sys.exit(main())
