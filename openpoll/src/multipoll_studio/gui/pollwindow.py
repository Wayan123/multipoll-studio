"""MDI poll window: status line, alias/value grid, editing, per-window logging."""
from __future__ import annotations

import json
from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtGui import QBrush, QColor
from PySide6.QtWidgets import (
    QAbstractItemView, QApplication, QColorDialog, QFileDialog, QFontDialog, QHeaderView, QLabel,
    QMenu, QMessageBox, QTableWidget, QTableWidgetItem, QVBoxLayout, QWidget,
)

from .. import protocol
from ..engine import PollEngine, PollResult
from ..formats import DisplayFormat, decode, encode, parse_coil, parse_value
from ..model import PollDefinition, PollDocument, address_label
from ..protocol import FC, ModbusError, Status
from ..support import DataLogger, LogSettings, render_values
from .bridge import post_to_gui
from .dialogs import (
    DefinitionDialog, LogDialog, MaskWriteDialog, ReadWriteRegistersDialog, WriteCoilsDialog, WriteRegisterDialog,
    WriteRegistersDialog, WriteSingleCoilDialog, show_error,
)

ERROR_BG = QColor(255, 220, 220)
OK_BG = QColor(220, 255, 220)


class PollWindow(QWidget):
    """One poll definition shown as a grid of (alias, value) column pairs."""

    counter = 0

    def __init__(self, engine: PollEngine, document: PollDocument | None = None, path: str | None = None):
        super().__init__()
        PollWindow.counter += 1
        self.engine = engine
        self.doc = document or PollDocument()
        self.path = path
        self.untitled_name = f"Poll {PollWindow.counter}"
        self.values: list[int] | None = None
        self.last_status = Status.NOT_INITIALIZED
        self.logger: DataLogger | None = None
        self._editing = False
        self.dirty = False

        self.status = QLabel()
        self.status.setTextInteractionFlags(Qt.TextSelectableByMouse)
        self.error_line = QLabel("No connection")
        self.error_line.setStyleSheet("color: #c00000; font-weight: bold")
        self.table = QTableWidget()
        self.table.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.table.setEditTriggers(QAbstractItemView.DoubleClicked | QAbstractItemView.EditKeyPressed
                                   | QAbstractItemView.AnyKeyPressed)
        self.table.horizontalHeader().setSectionResizeMode(QHeaderView.Interactive)
        self.table.itemChanged.connect(self._item_changed)
        self.table.setContextMenuPolicy(Qt.CustomContextMenu)
        self.table.customContextMenuRequested.connect(self._context_menu)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(2, 2, 2, 2)
        layout.addWidget(self.status)
        layout.addWidget(self.error_line)
        layout.addWidget(self.table)

        self.poll_id = engine.add_poll(self.doc.definition, self._on_result)
        self.rebuild()
        self.resize(560, 360)

    # -- identity ------------------------------------------------------------------
    def title(self) -> str:
        name = Path(self.path).name if self.path else self.untitled_name
        return name + (" *" if self.dirty else "")

    def mark_dirty(self, dirty: bool = True) -> None:
        self.dirty = dirty
        self.setWindowTitle(self.title())

    # -- grid -------------------------------------------------------------------------
    def rebuild(self) -> None:
        d, v = self.doc.definition, self.doc.view
        rows = v.rows
        pairs = (d.quantity + rows - 1) // rows
        self._editing = True
        self.table.clear()
        self.table.setRowCount(rows)
        self.table.setColumnCount(pairs * 2)
        for p in range(pairs):
            start = p * rows
            self.table.setHorizontalHeaderItem(2 * p, QTableWidgetItem("Alias"))
            self.table.setHorizontalHeaderItem(2 * p + 1, QTableWidgetItem(
                address_label(d.function, d.address + start, v.plc_addresses)))
            self.table.setColumnHidden(2 * p, v.hide_alias)
        self.table.setVerticalHeaderLabels([str(r) for r in range(rows)])
        for offset in range(d.quantity):
            alias = QTableWidgetItem(self.doc.aliases.get(offset, ""))
            self.table.setItem(*self._cell(offset, alias=True), alias)
            cell = QTableWidgetItem("")
            cell.setTextAlignment(Qt.AlignRight | Qt.AlignVCenter)
            if not d.writable:
                cell.setFlags(cell.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(*self._cell(offset), cell)
        for r in range(rows):
            for c in range(self.table.columnCount()):
                if self.table.item(r, c) is None:
                    blank = QTableWidgetItem("")
                    blank.setFlags(Qt.NoItemFlags)
                    self.table.setItem(r, c, blank)
        self._editing = False
        self._update_status()
        self._paint_values()
        self.setWindowTitle(self.title())

    def _cell(self, offset: int, alias: bool = False) -> tuple[int, int]:
        rows = self.doc.view.rows
        return offset % rows, (offset // rows) * 2 + (0 if alias else 1)

    def _offset_at(self, row: int, col: int) -> int | None:
        if col % 2 == 0:
            return None
        offset = (col // 2) * self.doc.view.rows + row
        return offset if offset < self.doc.definition.quantity else None

    def _paint_values(self) -> None:
        if self.values is None:
            return
        d, v = self.doc.definition, self.doc.view
        result = PollResult(self.poll_id, d, Status.OK, self.values, "", 0)
        self._editing = True
        for offset, text in render_values(result, v):
            item = self.table.item(*self._cell(offset))
            if item is None:
                continue
            if v.address_in_cell:
                text = f"{address_label(d.function, d.address + offset, v.plc_addresses)} = {text}"
            item.setText(text)
        self._editing = False

    def _update_status(self) -> None:
        d = self.doc.definition
        st = self.engine.stats(self.poll_id)
        disabled = "" if d.enabled else "  (DISABLED)"
        self.status.setText(f"Requests {st.tx} | Errors {st.errors} | Unit {d.slave_id} | "
                            f"FC {d.function:02d} | Interval {d.scan_rate_ms} ms{disabled}")

    # -- results ------------------------------------------------------------------------
    def _on_result(self, result: PollResult) -> None:
        post_to_gui(lambda: self._apply_result(result))

    def _apply_result(self, result: PollResult) -> None:
        if result.definition is not self.doc.definition:
            return  # stale result from a superseded definition
        self.last_status = result.status
        if result.status is Status.OK:
            self.values = result.values
            self.error_line.setText("")
            self._paint_values()
        else:
            self.error_line.setText(result.status.text + (f" ({result.detail})" if result.detail else ""))
        if self.logger is not None:
            try:
                self.logger.log(result)
            except OSError as exc:
                self.stop_log()
                show_error(self, exc)
        self._update_status()

    # -- definition ------------------------------------------------------------------------
    def apply_definition(self, definition: PollDefinition, view: dict) -> None:
        old = self.doc.definition
        reshape = (definition.function, definition.address, definition.quantity) != \
                  (old.function, old.address, old.quantity)
        self.doc.definition = definition
        for k, val in view.items():
            setattr(self.doc.view, k, val)
        if reshape:
            self.values = None
            self.doc.view.cell_formats.clear()
        self.engine.update_poll(self.poll_id, definition)
        self.rebuild()
        self.mark_dirty()

    def open_definition(self) -> None:
        DefinitionDialog(self.doc.definition, self.doc.view, self.apply_definition,
                         lambda: self.engine.read_once(self.poll_id), self).exec()

    def set_enabled(self, on: bool) -> None:
        d = self.doc.definition
        new = PollDefinition(**{**d.__dict__, "enabled": on})
        self.doc.definition = new
        self.engine.update_poll(self.poll_id, new)
        self._update_status()
        self.mark_dirty()

    def read_once(self) -> None:
        self.engine.read_once(self.poll_id)

    def set_display(self, fmt: DisplayFormat) -> None:
        """Apply to the selected value cells, or to the whole window when nothing is selected."""
        offsets = self.selected_offsets()
        if offsets and len(offsets) < self.doc.definition.quantity:
            for o in offsets:
                self.doc.view.cell_formats[o] = fmt
        else:
            self.doc.view.display = fmt
            self.doc.view.cell_formats.clear()
        self.rebuild()
        self.mark_dirty()

    def set_plc_addresses(self, on: bool) -> None:
        self.doc.view.plc_addresses = on
        self.rebuild()
        self.mark_dirty()

    def reset_counters(self) -> None:
        self.engine.reset_stats(self.poll_id)
        self._update_status()

    # -- editing -------------------------------------------------------------------------------
    def selected_offsets(self) -> list[int]:
        out = {self._offset_at(i.row(), i.column()) for i in self.table.selectedIndexes()}
        return sorted(o for o in out if o is not None)

    def _item_changed(self, item: QTableWidgetItem) -> None:
        if self._editing:
            return
        if item.column() % 2 == 0:
            offset = (item.column() // 2) * self.doc.view.rows + item.row()
            self.doc.aliases[offset] = item.text()
            self.mark_dirty()
            return
        offset = self._offset_at(item.row(), item.column())
        if offset is None:
            return
        text = item.text().split("=", 1)[-1] if self.doc.view.address_in_cell else item.text()
        self._paint_values()  # restore displayed value until the slave confirms
        try:
            self._write_cell(offset, text)
        except ValueError as exc:
            show_error(self, exc)

    def _write_cell(self, offset: int, text: str) -> None:
        d = self.doc.definition
        address = d.address + offset
        if d.function == FC.READ_COILS:
            pdu = protocol.write_single_coil(address, bool(parse_coil(text)))
        elif d.function == FC.READ_HOLDING_REGISTERS:
            fmt = self.doc.view.format_at(offset)
            words = encode(parse_value(text, fmt), fmt)
            pdu = (protocol.write_single_register(address, words[0]) if len(words) == 1
                   else protocol.write_multiple_registers(address, words))
        else:
            raise ValueError("Discrete inputs and input registers are read-only")
        fut = self.engine.write(d.slave_id, pdu)
        fut.add_done_callback(lambda f: post_to_gui(lambda: self._write_done(f)))

    def _write_done(self, fut) -> None:
        exc = fut.exception()
        if exc is not None:
            show_error(self, exc)
        else:
            self.engine.read_once(self.poll_id)

    def keyPressEvent(self, event) -> None:  # noqa: N802 - Qt API
        # Space toggles coils, as in the original.
        if event.key() == Qt.Key_Space and self.doc.definition.function == FC.READ_COILS and self.values:
            for o in self.selected_offsets():
                self._write_cell(o, "0" if self.values[o] else "1")
            return
        super().keyPressEvent(event)

    def copy_selection(self) -> None:
        idx = self.table.selectedIndexes()
        if not idx:
            return
        rows = sorted({i.row() for i in idx})
        cols = sorted({i.column() for i in idx})
        lines = []
        for r in rows:
            cells = []
            for c in cols:
                item = self.table.item(r, c)
                cells.append(item.text() if item and self.table.selectionModel().isSelected(
                    self.table.model().index(r, c)) else "")
            lines.append("\t".join(cells))
        QApplication.clipboard().setText("\n".join(lines))

    def paste_aliases(self) -> None:
        """Paste tab/newline text into alias cells only (value cells cannot be pasted)."""
        text = QApplication.clipboard().text()
        cur = self.table.currentItem()
        if not text or cur is None or cur.column() % 2 != 0:
            return
        for dr, line in enumerate(text.splitlines()):
            item = self.table.item(cur.row() + dr, cur.column())
            if item is not None and item.flags() & Qt.ItemIsEditable:
                item.setText(line.split("\t")[0])

    def _context_menu(self, pos) -> None:
        menu = QMenu(self)
        menu.addAction("Poll &Setup…", self.open_definition)
        menu.addSeparator()
        menu.addAction("&Copy", self.copy_selection)
        menu.addAction("&Paste (aliases)", self.paste_aliases)
        menu.addAction("Select &All", self.table.selectAll)
        menu.addSeparator()
        fmt_menu = menu.addMenu("Display")
        for f in DisplayFormat:
            fmt_menu.addAction(f.label, lambda f=f: self.set_display(f))
        menu.addAction("&Foreground Color…", lambda: self._color(True))
        menu.addAction("&Background Color…", lambda: self._color(False))
        menu.addAction("F&ont…", self._font)
        menu.exec(self.table.viewport().mapToGlobal(pos))

    def _color(self, foreground: bool) -> None:
        c = QColorDialog.getColor(parent=self)
        if not c.isValid():
            return
        for i in self.table.selectedItems():
            (i.setForeground if foreground else i.setBackground)(QBrush(c))

    def _font(self) -> None:
        ok, font = QFontDialog.getFont(self.table.font(), self)
        if ok:
            self.table.setFont(font)

    # -- function dialogs ----------------------------------------------------------------------------
    def _first_selected(self) -> int:
        sel = self.selected_offsets()
        return sel[0] if sel else 0

    def _initial_value(self, offset: int):
        return self.values[offset] if self.values and offset < len(self.values) else 0

    def open_function(self, fc: int) -> None:
        if not self.engine.running:
            show_error(self, ModbusError(Status.NO_CONNECTION))
            return
        d, v = self.doc.definition, self.doc.view
        off = self._first_selected()
        addr = d.address + off
        plc = v.plc_addresses
        slave = d.slave_id
        if fc == FC.WRITE_SINGLE_COIL:
            dlg = WriteSingleCoilDialog(self.engine, slave, addr, plc, self._initial_value(off), self)
        elif fc == FC.WRITE_SINGLE_REGISTER:
            fmt = v.format_at(off)
            words = (self.values or [])[off:off + fmt.width]
            value = decode(words, fmt) if len(words) == fmt.width else None
            dlg = WriteRegisterDialog(self.engine, slave, addr, plc, fmt, value, self)
        elif fc == FC.WRITE_MULTIPLE_COILS:
            dlg = WriteCoilsDialog(self.engine, slave, d.address, plc, min(d.quantity, protocol.MAX_WRITE_BITS),
                                   self.values or [], self)
        elif fc == FC.WRITE_MULTIPLE_REGISTERS:
            dlg = WriteRegistersDialog(self.engine, slave, d.address, plc,
                                       min(d.quantity, protocol.MAX_WRITE_REGISTERS), self.values or [], self)
        elif fc == FC.MASK_WRITE_REGISTER:
            dlg = MaskWriteDialog(self.engine, slave, addr, plc, self)
        elif fc == FC.READ_WRITE_MULTIPLE_REGISTERS:
            dlg = ReadWriteRegistersDialog(self.engine, slave, d.address, plc,
                                           min(d.quantity, protocol.MAX_RW_WRITE_REGISTERS), self.values or [], self)
        else:
            return
        dlg.finished.connect(lambda _: self.engine.read_once(self.poll_id))
        dlg.exec()

    # -- logging ----------------------------------------------------------------------------------
    def start_log(self) -> None:
        dlg = LogDialog(self)
        if not dlg.exec():
            return
        self.stop_log()
        try:
            self.logger = DataLogger(dlg.settings(), self.doc.view)
        except OSError as exc:
            show_error(self, exc)

    def start_log_with(self, settings: LogSettings) -> None:
        self.stop_log()
        self.logger = DataLogger(settings, self.doc.view)

    def stop_log(self) -> None:
        if self.logger is not None:
            self.logger.close()
            self.logger = None

    # -- persistence ---------------------------------------------------------------------------------
    def save(self, path: str | None = None) -> bool:
        path = path or self.path
        if not path:
            path, _ = QFileDialog.getSaveFileName(self, "Save As", self.untitled_name + ".mpoll",
                                                  "MultiPoll Studio document (*.mpoll)")
            if not path:
                return False
        try:
            Path(path).write_text(json.dumps(self.doc.to_dict(), indent=2), encoding="utf-8")
        except OSError as exc:
            show_error(self, exc)
            return False
        self.path = path
        self.mark_dirty(False)
        return True

    @staticmethod
    def load(path: str) -> PollDocument:
        return PollDocument.from_dict(json.loads(Path(path).read_text(encoding="utf-8")))

    def maybe_save(self) -> bool:
        if not self.dirty:
            return True
        ans = QMessageBox.question(self, "MultiPoll Studio", f"Save changes to {self.title().rstrip(' *')}?",
                                   QMessageBox.Save | QMessageBox.Discard | QMessageBox.Cancel)
        if ans == QMessageBox.Cancel:
            return False
        return ans == QMessageBox.Discard or self.save()

    def shutdown(self) -> None:
        self.stop_log()
        self.engine.remove_poll(self.poll_id)
