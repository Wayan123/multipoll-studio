"""Dialogs for connection, poll setup, writes, logging and frame console."""
from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QCheckBox, QComboBox, QDialog, QDialogButtonBox, QFileDialog, QFormLayout, QGridLayout,
    QGroupBox, QHBoxLayout, QLabel, QLineEdit, QListWidget, QMessageBox, QPushButton,
    QRadioButton, QSpinBox, QVBoxLayout, QWidget, QAbstractItemView, QApplication,
)

from .. import protocol
from ..formats import DisplayFormat, encode, format_value, parse_coil, parse_value
from ..model import FUNCTION_LABELS, ROW_CHOICES, PollDefinition, ViewOptions
from ..protocol import ModbusError
from ..support import CheckKind, Delimiter, LogSettings, add_check, hex_bytes, load_test_list, parse_hex_string, save_test_list
from ..transport import ConnectionKind, ConnectionSettings, SerialMode
from .bridge import post_to_gui


def _spin(lo: int, hi: int, value: int, suffix: str = "") -> QSpinBox:
    s = QSpinBox()
    s.setRange(lo, hi)
    s.setValue(value)
    if suffix:
        s.setSuffix(suffix)
    return s


def _serial_ports() -> list[str]:
    try:
        from serial.tools import list_ports
    except ImportError:
        return []
    return [p.device for p in list_ports.comports()]


def show_error(parent: QWidget, exc: BaseException) -> None:
    if isinstance(exc, ModbusError):
        text = exc.status.text + (f"\n{exc.detail}" if exc.detail and exc.detail != exc.status.text else "")
    else:
        text = str(exc)
    QMessageBox.warning(parent, "MultiPoll Studio", text)


# --- Connection ------------------------------------------------------------------

class ConnectionDialog(QDialog):
    def __init__(self, settings: ConnectionSettings, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Connection Setup")
        s = settings

        self.kind = QComboBox()
        for k in ConnectionKind:
            self.kind.addItem(k.label, k)
        self.kind.setCurrentIndex(list(ConnectionKind).index(s.kind))

        serial_box = QGroupBox("Serial Settings")
        sf = QFormLayout(serial_box)
        self.port = QComboBox()
        self.port.setEditable(True)
        self.port.addItems(_serial_ports())
        self.port.setCurrentText(s.port)
        self.baud = QComboBox()
        self.baud.setEditable(True)
        self.baud.addItems([str(b) for b in (300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 230400)])
        self.baud.setCurrentText(str(s.baudrate))
        self.databits = QComboBox()
        self.databits.addItems(["7", "8"])
        self.databits.setCurrentText(str(s.databits))
        self.parity = QComboBox()
        for code, name in (("N", "None"), ("E", "Even"), ("O", "Odd")):
            self.parity.addItem(name, code)
        self.parity.setCurrentIndex(["N", "E", "O"].index(s.parity))
        self.stopbits = QComboBox()
        self.stopbits.addItems(["1", "2"])
        self.stopbits.setCurrentText(str(s.stopbits))
        sf.addRow("Port:", self.port)
        sf.addRow("Baud:", self.baud)
        sf.addRow("Data bits:", self.databits)
        sf.addRow("Parity:", self.parity)
        sf.addRow("Stop bits:", self.stopbits)

        mode_box = QGroupBox("Mode")
        ml = QHBoxLayout(mode_box)
        self.rtu = QRadioButton("RTU")
        self.ascii = QRadioButton("ASCII")
        (self.rtu if s.serial_mode is SerialMode.RTU else self.ascii).setChecked(True)
        ml.addWidget(self.rtu)
        ml.addWidget(self.ascii)

        net_box = QGroupBox("Remote Server")
        nf = QFormLayout(net_box)
        self.host = QLineEdit(s.host)
        self.tcp_port = _spin(1, 65535, s.tcp_port)
        self.connect_timeout = _spin(100, 60000, s.connect_timeout_ms, " ms")
        nf.addRow("IP Address:", self.host)
        nf.addRow("Port:", self.tcp_port)
        nf.addRow("Connect Timeout:", self.connect_timeout)

        timing = QGroupBox("Timing")
        tf = QFormLayout(timing)
        self.response_timeout = _spin(10, 60000, s.response_timeout_ms, " ms")
        self.delay = _spin(0, 60000, s.delay_between_polls_ms, " ms")
        tf.addRow("Response Timeout:", self.response_timeout)
        tf.addRow("Delay Between Polls:", self.delay)

        adv = QGroupBox("Advanced (serial)")
        af = QGridLayout(adv)
        self.rts_toggle = QCheckBox("RTS Toggle")
        self.rts_toggle.setChecked(s.rts_toggle)
        self.rts_delay = _spin(0, 1000, s.rts_disable_delay_ms, " ms RTS disable delay")
        self.dsr = QCheckBox("DSR flow control")
        self.dsr.setChecked(s.dsr_flow)
        self.cts = QCheckBox("CTS flow control")
        self.cts.setChecked(s.cts_flow)
        self.echo = QCheckBox("Remove Echo")
        self.echo.setChecked(s.remove_echo)
        af.addWidget(self.rts_toggle, 0, 0)
        af.addWidget(self.rts_delay, 0, 1)
        af.addWidget(self.dsr, 1, 0)
        af.addWidget(self.cts, 1, 1)
        af.addWidget(self.echo, 2, 0)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)

        grid = QGridLayout()
        grid.addWidget(serial_box, 0, 0, 2, 1)
        grid.addWidget(mode_box, 0, 1)
        grid.addWidget(timing, 1, 1)
        grid.addWidget(net_box, 2, 0)
        grid.addWidget(adv, 2, 1)
        layout = QVBoxLayout(self)
        form = QFormLayout()
        form.addRow("Connection:", self.kind)
        layout.addLayout(form)
        layout.addLayout(grid)
        layout.addWidget(buttons)

        self._serial_box, self._net_box, self._mode_box, self._adv = serial_box, net_box, mode_box, adv
        self.kind.currentIndexChanged.connect(self._sync)
        self._sync()

    def _sync(self) -> None:
        k = ConnectionKind(self.kind.currentData())
        self._serial_box.setEnabled(k is ConnectionKind.SERIAL)
        self._adv.setEnabled(k is ConnectionKind.SERIAL)
        self._net_box.setEnabled(k.is_network)
        self._mode_box.setEnabled(not k.uses_mbap)
        self.connect_timeout.setEnabled(k in (ConnectionKind.TCP, ConnectionKind.SERIAL_OVER_TCP))

    def settings(self) -> ConnectionSettings:
        try:
            baud = int(self.baud.currentText())
        except ValueError:
            baud = 9600
        return ConnectionSettings(
            kind=ConnectionKind(self.kind.currentData()),
            serial_mode=SerialMode.RTU if self.rtu.isChecked() else SerialMode.ASCII,
            port=self.port.currentText().strip(),
            baudrate=baud,
            databits=int(self.databits.currentText()),
            parity=self.parity.currentData(),
            stopbits=int(self.stopbits.currentText()),
            host=self.host.text().strip(),
            tcp_port=self.tcp_port.value(),
            connect_timeout_ms=self.connect_timeout.value(),
            response_timeout_ms=self.response_timeout.value(),
            delay_between_polls_ms=self.delay.value(),
            rts_toggle=self.rts_toggle.isChecked(),
            rts_disable_delay_ms=self.rts_delay.value(),
            dsr_flow=self.dsr.isChecked(),
            cts_flow=self.cts.isChecked(),
            remove_echo=self.echo.isChecked(),
        )


# --- Read/Write definition ----------------------------------------------------------

class DefinitionDialog(QDialog):
    """F8 dialog. ``Apply`` and ``Read/Write Once`` call back into the owning window."""

    def __init__(self, definition: PollDefinition, view: ViewOptions, on_apply, on_once, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Poll Setup")
        self._on_apply, self._on_once = on_apply, on_once
        d = definition
        self.slave = _spin(0, 255, d.slave_id)
        self.function = QComboBox()
        for fc, label in FUNCTION_LABELS.items():
            self.function.addItem(label, int(fc))
        self.function.setCurrentIndex(self.function.findData(int(d.function)))
        self.plc = QCheckBox("PLC Addresses (Base 1)")
        self.plc.setChecked(view.plc_addresses)
        self.address = _spin(0, 65536, d.address + (1 if view.plc_addresses else 0))
        self.quantity = _spin(1, 2000, d.quantity)
        self.scan = _spin(1, 3_600_000, d.scan_rate_ms, " ms")
        self.enabled = QCheckBox("Polling Enabled")
        self.enabled.setChecked(d.enabled)
        self.display = QComboBox()
        for f in DisplayFormat:
            self.display.addItem(f.label, f)
        self.display.setCurrentIndex(list(DisplayFormat).index(view.display))
        self.rows = QComboBox()
        for r in ROW_CHOICES:
            self.rows.addItem(str(r), r)
        self.rows.setCurrentIndex(ROW_CHOICES.index(view.rows) if view.rows in ROW_CHOICES else 0)
        self.hide_alias = QCheckBox("Hide Alias Columns")
        self.hide_alias.setChecked(view.hide_alias)
        self.addr_in_cell = QCheckBox("Address in Cell")
        self.addr_in_cell.setChecked(view.address_in_cell)
        self.plc.toggled.connect(self._plc_toggled)

        form = QFormLayout()
        form.addRow("Unit:", self.slave)
        form.addRow("Function:", self.function)
        form.addRow("Address:", self.address)
        form.addRow("", self.plc)
        form.addRow("Quantity:", self.quantity)
        form.addRow("Interval:", self.scan)
        form.addRow("", self.enabled)
        view_box = QGroupBox("View")
        vf = QFormLayout(view_box)
        vf.addRow("Rows:", self.rows)
        vf.addRow("Display:", self.display)
        vf.addRow(self.hide_alias)
        vf.addRow(self.addr_in_cell)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel | QDialogButtonBox.Apply)
        once = buttons.addButton("&Read/Write Once", QDialogButtonBox.ActionRole)
        buttons.accepted.connect(self._ok)
        buttons.rejected.connect(self.reject)
        buttons.button(QDialogButtonBox.Apply).clicked.connect(self._apply)
        once.clicked.connect(self._once)

        layout = QVBoxLayout(self)
        layout.addLayout(form)
        layout.addWidget(view_box)
        layout.addWidget(buttons)

    def _plc_toggled(self, on: bool) -> None:
        self.address.setValue(max(0, self.address.value() + (1 if on else -1)))

    def values(self) -> tuple[PollDefinition, dict]:
        plc = self.plc.isChecked()
        address = self.address.value() - (1 if plc else 0)
        d = PollDefinition(slave_id=self.slave.value(), function=self.function.currentData(), address=address,
                           quantity=self.quantity.value(), scan_rate_ms=self.scan.value(),
                           enabled=self.enabled.isChecked())
        d.validate()
        view = dict(display=DisplayFormat(self.display.currentData()), plc_addresses=plc, rows=self.rows.currentData(),
                    hide_alias=self.hide_alias.isChecked(), address_in_cell=self.addr_in_cell.isChecked())
        return d, view

    def _apply(self) -> bool:
        try:
            self._on_apply(*self.values())
        except ValueError as exc:
            show_error(self, exc)
            return False
        return True

    def _ok(self) -> None:
        if self._apply():
            self.accept()

    def _once(self) -> None:
        if self._apply():
            self._on_once()


# --- Write functions ----------------------------------------------------------------

class _WriteDialog(QDialog):
    """Base: slave/address inputs, Send, result label, optional close-on-success."""

    def __init__(self, title: str, engine, slave: int, address: int, plc: bool, parent=None):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.engine = engine
        self.plc = plc
        self.slave = _spin(0, 255, slave)
        self.address = _spin(0, 65536, address + (1 if plc else 0))
        self.result = QLabel("N/A")
        self.close_on_ok = QCheckBox('Close dialog on "Response ok"')
        self.form = QFormLayout()
        self.form.addRow("Slave ID:", self.slave)
        self.form.addRow("Address:" + (" (PLC, base 1)" if plc else ""), self.address)
        self.send = QPushButton("&Send")
        self.send.setDefault(True)
        self.send.clicked.connect(self._send)
        cancel = QPushButton("Close")
        cancel.clicked.connect(self.reject)
        row = QHBoxLayout()
        row.addWidget(self.close_on_ok)
        row.addStretch()
        row.addWidget(self.send)
        row.addWidget(cancel)
        self.layout_ = QVBoxLayout(self)
        self.layout_.addLayout(self.form)
        self.layout_.addWidget(self._group("Result", self.result))
        self.layout_.addLayout(row)

    @staticmethod
    def _group(title: str, widget: QWidget) -> QGroupBox:
        g = QGroupBox(title)
        QVBoxLayout(g).addWidget(widget)
        return g

    def protocol_address(self) -> int:
        a = self.address.value() - (1 if self.plc else 0)
        if not 0 <= a <= 0xFFFF:
            raise ValueError("Address out of range")
        return a

    def build_pdu(self) -> bytes:  # pragma: no cover - abstract
        raise NotImplementedError

    def _send(self) -> None:
        try:
            pdu = self.build_pdu()
        except ValueError as exc:
            show_error(self, exc)
            return
        self.send.setEnabled(False)
        self.result.setText("Sending…")
        future = self.engine.write(self.slave.value(), pdu)
        future.add_done_callback(lambda f: post_to_gui(lambda: self._done(f)))

    def _done(self, future) -> None:
        self.send.setEnabled(True)
        exc = future.exception()
        if exc is None:
            values = future.result()
            self.on_success(values)
            self.result.setText("Response ok" if self.slave.value() else "Broadcast sent (no response)")
            if self.close_on_ok.isChecked():
                self.accept()
            return
        status = exc.status.text if isinstance(exc, ModbusError) else str(exc)
        self.result.setText(status)

    def on_success(self, values) -> None:
        pass


class WriteSingleCoilDialog(_WriteDialog):
    def __init__(self, engine, slave, address, plc, value: int = 1, parent=None):
        super().__init__("05: Write Single Coil", engine, slave, address, plc, parent)
        self.on = QRadioButton("On")
        self.off = QRadioButton("Off")
        (self.on if value else self.off).setChecked(True)
        self.fc15 = QCheckBox("Use 15: Write multiple coils")
        box = QHBoxLayout()
        box.addWidget(self.on)
        box.addWidget(self.off)
        w = QWidget()
        w.setLayout(box)
        self.form.addRow("Value:", w)
        self.form.addRow(self.fc15)

    def build_pdu(self) -> bytes:
        if self.fc15.isChecked():
            return protocol.write_multiple_coils(self.protocol_address(), [int(self.on.isChecked())])
        return protocol.write_single_coil(self.protocol_address(), self.on.isChecked())


class WriteRegisterDialog(_WriteDialog):
    """06/16 for one value; multi-register formats always use 16."""

    def __init__(self, engine, slave, address, plc, fmt: DisplayFormat, value=0, parent=None):
        super().__init__("Write Register", engine, slave, address, plc, parent)
        self.fmt = QComboBox()
        for f in DisplayFormat:
            self.fmt.addItem(f.label, f)
        self.fmt.setCurrentIndex(list(DisplayFormat).index(fmt))
        self.value = QLineEdit(format_value(value, fmt) if value is not None else "")
        self.fc16 = QCheckBox("Use 16: Write multiple registers")
        self.form.addRow("Format:", self.fmt)
        self.form.addRow("Value:", self.value)
        self.form.addRow(self.fc16)
        self.fmt.currentIndexChanged.connect(self._sync)
        self._sync()

    def _sync(self) -> None:
        wide = DisplayFormat(self.fmt.currentData()).width > 1
        self.fc16.setChecked(wide or self.fc16.isChecked())
        self.fc16.setEnabled(not wide)

    def build_pdu(self) -> bytes:
        fmt = DisplayFormat(self.fmt.currentData())
        words = encode(parse_value(self.value.text(), fmt), fmt)
        if len(words) == 1 and not self.fc16.isChecked():
            return protocol.write_single_register(self.protocol_address(), words[0])
        return protocol.write_multiple_registers(self.protocol_address(), words)


class _ValueListDialog(_WriteDialog):
    """Shared by FC15/FC16/FC23: editable value list with open/save."""

    def __init__(self, title, engine, slave, address, plc, quantity, initial: list[int], is_bits: bool, parent=None):
        super().__init__(title, engine, slave, address, plc, parent)
        self.is_bits = is_bits
        self.quantity = _spin(1, protocol.MAX_WRITE_BITS if is_bits else protocol.MAX_WRITE_REGISTERS, quantity)
        self.fmt = QComboBox()
        for f in DisplayFormat if not is_bits else ():
            if f.width == 1:
                self.fmt.addItem(f.label, f)
        self.list = QListWidget()
        self.list.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.list.itemDoubleClicked.connect(self._edit)
        self.form.addRow("Quantity:", self.quantity)
        if not is_bits:
            self.form.addRow("Format:", self.fmt)
            self.fmt.currentIndexChanged.connect(self._refresh)
        self._values = list(initial[:quantity]) + [0] * max(0, quantity - len(initial))
        self.quantity.valueChanged.connect(self._resize)
        btns = QHBoxLayout()
        for text, slot in (("&Edit", self._edit), ("&Open", self._open), ("S&ave", self._save)):
            b = QPushButton(text)
            b.clicked.connect(slot)
            btns.addWidget(b)
        self.layout_.insertWidget(1, self.list)
        self.layout_.insertLayout(2, btns)
        self._refresh()

    def _fmt(self) -> DisplayFormat:
        return DisplayFormat(self.fmt.currentData()) if not self.is_bits else DisplayFormat.UNSIGNED

    def _resize(self, n: int) -> None:
        self._values = (self._values + [0] * n)[:n]
        self._refresh()

    def _refresh(self) -> None:
        self.list.clear()
        base = self.address.value()
        for i, v in enumerate(self._values):
            self.list.addItem(f"{base + i:05d} = {v if self.is_bits else format_value(v, self._fmt())}")

    def _edit(self, *_):
        from PySide6.QtWidgets import QInputDialog

        rows = sorted({i.row() for i in self.list.selectedIndexes()}) or [0]
        first = self._values[rows[0]]
        text, ok = QInputDialog.getText(self, "Enter Value", "Value:",
                                        text=str(first) if self.is_bits else format_value(first, self._fmt()))
        if not ok:
            return
        try:
            v = parse_coil(text) if self.is_bits else encode(parse_value(text, self._fmt()), self._fmt())[0]
        except ValueError as exc:
            show_error(self, exc)
            return
        for r in rows:
            self._values[r] = v
        self._refresh()

    def _open(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "Open values", "", "Value list (*.txt);;All files (*)")
        if not path:
            return
        try:
            with open(path, encoding="utf-8") as fh:
                vals = [int(x, 0) for x in fh.read().split()]
            if not vals:
                raise ValueError("file contains no values")
        except (OSError, ValueError) as exc:
            show_error(self, exc)
            return
        self.quantity.setValue(len(vals))
        self._values = vals[:self.quantity.value()]
        self._refresh()

    def _save(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Save values", "values.txt", "Value list (*.txt)")
        if path:
            with open(path, "w", encoding="utf-8") as fh:
                fh.write("\n".join(str(v) for v in self._values) + "\n")


class WriteCoilsDialog(_ValueListDialog):
    def __init__(self, engine, slave, address, plc, quantity, initial, parent=None):
        super().__init__("15: Write Multiple Coils", engine, slave, address, plc, quantity, initial, True, parent)

    def build_pdu(self) -> bytes:
        return protocol.write_multiple_coils(self.protocol_address(), self._values)


class WriteRegistersDialog(_ValueListDialog):
    def __init__(self, engine, slave, address, plc, quantity, initial, parent=None):
        super().__init__("16: Write Multiple Registers", engine, slave, address, plc, quantity, initial, False, parent)

    def build_pdu(self) -> bytes:
        return protocol.write_multiple_registers(self.protocol_address(), self._values)


class ReadWriteRegistersDialog(_ValueListDialog):
    def __init__(self, engine, slave, address, plc, quantity, initial, parent=None):
        super().__init__("23: Read/Write Multiple Registers", engine, slave, address, plc, quantity, initial,
                         False, parent)
        self.form.labelForField(self.address).setText("Write Address:")
        self.read_address = _spin(0, 65536, self.address.value())
        self.read_quantity = _spin(1, protocol.MAX_RW_READ_REGISTERS, min(quantity, protocol.MAX_RW_READ_REGISTERS))
        self.form.addRow("Read Address:", self.read_address)
        self.form.addRow("Read Quantity:", self.read_quantity)
        self.read_result = QListWidget()
        self.layout_.insertWidget(self.layout_.count() - 1, self._group("Read Result", self.read_result))

    def build_pdu(self) -> bytes:
        ra = self.read_address.value() - (1 if self.plc else 0)
        return protocol.read_write_registers(ra, self.read_quantity.value(), self.protocol_address(), self._values)

    def on_success(self, values) -> None:
        self.read_result.clear()
        base = self.read_address.value()
        for i, v in enumerate(values or []):
            self.read_result.addItem(f"{base + i:05d} = {format_value(v, self._fmt())}")


class MaskWriteDialog(_WriteDialog):
    def __init__(self, engine, slave, address, plc, parent=None):
        super().__init__("22: Mask Write Register", engine, slave, address, plc, parent)
        self.and_mask = QLineEdit("FFFF")
        self.or_mask = QLineEdit("0000")
        self.form.addRow("AND Mask (hex):", self.and_mask)
        self.form.addRow("OR Mask (hex):", self.or_mask)
        self.form.addRow(QLabel("Result = (Current AND And_Mask) OR (Or_Mask AND (NOT And_Mask))"))

    def build_pdu(self) -> bytes:
        return protocol.mask_write_register(self.protocol_address(), parse_value(self.and_mask.text(), DisplayFormat.HEX),
                                            parse_value(self.or_mask.text(), DisplayFormat.HEX))


# --- Logging ------------------------------------------------------------------------------

class LogDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Log Setup")
        self.path = QLineEdit("logfile.txt")
        browse = QPushButton("…")
        browse.clicked.connect(self._browse)
        self.each = QCheckBox("Each Read")
        self.each.setChecked(True)
        self.rate = _spin(1, 86400, 1, " s")
        self.each.toggled.connect(lambda on: self.rate.setEnabled(not on))
        self.rate.setEnabled(False)
        self.delim = QComboBox()
        for d, name in ((Delimiter.FIXED, "Fixed Width"), (Delimiter.COMMA, "Comma"), (Delimiter.TAB, "Tab")):
            self.delim.addItem(name, d)
        self.errors = QCheckBox("Log Errors")
        self.errors.setChecked(True)
        self.date = QCheckBox("Log Date")
        self.changed = QCheckBox("Log if data changed only")
        row = QHBoxLayout()
        row.addWidget(self.path)
        row.addWidget(browse)
        form = QFormLayout(self)
        form.addRow("File:", row)
        form.addRow(self.each)
        form.addRow("Rate:", self.rate)
        form.addRow("Delimiter:", self.delim)
        form.addRow(self.errors)
        form.addRow(self.date)
        form.addRow(self.changed)
        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        form.addRow(buttons)

    def _browse(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Log file", self.path.text(), "Modbus log data (*.txt *.csv)")
        if path:
            self.path.setText(path)

    def settings(self) -> LogSettings:
        return LogSettings(path=self.path.text(), each_poll=self.each.isChecked(), rate_s=self.rate.value(),
                           delimiter=Delimiter(self.delim.currentData()), log_errors=self.errors.isChecked(),
                           log_date=self.date.isChecked(), changed_only=self.changed.isChecked())


# --- Test Center ------------------------------------------------------------------------------

class TestCenterDialog(QDialog):
    def __init__(self, engine, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Frame Console")
        self.setWindowFlag(Qt.WindowMaximizeButtonHint)
        self.engine = engine
        self.input = QComboBox()
        self.input.setEditable(True)
        self.input.lineEdit().setPlaceholderText('Enter hex numbers separated by ","  "." or space')
        self.check = QComboBox()
        for k, name in ((CheckKind.NONE, "No check"), (CheckKind.CRC, "Add CRC"), (CheckKind.LRC, "Add LRC + CRLF")):
            self.check.addItem(name, k)
        self.log = QListWidget()
        self.log.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.log.setFont(_mono())
        buttons = QHBoxLayout()
        for text, slot in (("&Send", self._send), ("A&dd to list", self._add), ("&Open list", self._open),
                           ("S&ave list", self._save), ("&Copy", self._copy), ("C&lear", self.log.clear)):
            b = QPushButton(text)
            b.clicked.connect(slot)
            buttons.addWidget(b)
        layout = QVBoxLayout(self)
        top = QHBoxLayout()
        top.addWidget(self.input, 1)
        top.addWidget(self.check)
        layout.addLayout(top)
        layout.addLayout(buttons)
        layout.addWidget(self.log)
        layout.addWidget(QLabel("Raw frames bypass framing: include the MBAP header for Modbus TCP, "
                                "or add CRC/LRC for RTU/ASCII."))
        self.resize(640, 400)

    def _frame(self) -> bytes:
        return add_check(parse_hex_string(self.input.currentText()), CheckKind(self.check.currentData()))

    def _send(self) -> None:
        try:
            data = self._frame()
        except ValueError as exc:
            show_error(self, exc)
            return
        self.log.addItem(f"Tx: {hex_bytes(data)}")
        fut = self.engine.raw(data)
        fut.add_done_callback(lambda f: post_to_gui(lambda: self._done(f)))

    def _done(self, fut) -> None:
        exc = fut.exception()
        if exc is not None:
            self.log.addItem(f"Error: {exc.status.text if isinstance(exc, ModbusError) else exc}")
        else:
            data = fut.result()
            self.log.addItem(f"Rx: {hex_bytes(data)}" if data else "Rx: (no response)")
        self.log.scrollToBottom()

    def _add(self) -> None:
        text = self.input.currentText().strip()
        if text and self.input.findText(text) < 0:
            self.input.addItem(text)

    def _entries(self) -> list[str]:
        return [self.input.itemText(i) for i in range(self.input.count())]

    def _open(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "Open frame list", "", "Frame list (*.txt);;All files (*)")
        if not path:
            return
        try:
            entries = load_test_list(path)
        except (OSError, ValueError) as exc:
            show_error(self, exc)
            return
        self.input.clear()
        self.input.addItems(entries)

    def _save(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Save frame list", "frames.txt", "Frame list (*.txt)")
        if path:
            save_test_list(path, self._entries())

    def _copy(self) -> None:
        QApplication.clipboard().setText("\n".join(i.text() for i in self.log.selectedItems()))


def _mono():
    from PySide6.QtGui import QFontDatabase

    return QFontDatabase.systemFont(QFontDatabase.FixedFont)
