"""Main MDI window and application menus."""
from __future__ import annotations

import json
from pathlib import Path

from PySide6.QtCore import QSettings, Qt
from PySide6.QtGui import QAction, QActionGroup, QKeySequence
from PySide6.QtWidgets import (
    QApplication, QDialog, QFileDialog, QHBoxLayout, QLabel, QListWidget, QMainWindow, QMdiArea, QMdiSubWindow,
    QMessageBox, QPushButton, QVBoxLayout, QCheckBox, QAbstractItemView,
)

from .. import __version__
from ..engine import PollEngine
from ..formats import DisplayFormat
from ..model import PollDocument
from ..protocol import FC, ModbusError, Status
from ..support import traffic_line
from ..transport import ConnectionSettings
from .bridge import install, post_to_gui
from .dialogs import ConnectionDialog, TestCenterDialog, show_error
from .pollwindow import PollWindow

DOC_FILTER = "MultiPoll Studio document (*.mpoll);;All files (*)"
WORKSPACE_FILTER = "MultiPoll Studio workspace (*.mpollw)"
TRAFFIC_LIMIT = 5000


class TrafficWindow(QDialog):
    """Communication Traffic: Stop/Continue, Stop on Error, Copy, Save, Clear."""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Communication Traffic")
        self.setWindowFlag(Qt.WindowMaximizeButtonHint)
        self.list = QListWidget()
        self.list.setSelectionMode(QAbstractItemView.ExtendedSelection)
        from .dialogs import _mono

        self.list.setFont(_mono())
        self.stop_btn = QPushButton("&Stop")
        self.stop_btn.setCheckable(True)
        self.stop_btn.toggled.connect(lambda on: self.stop_btn.setText("&Continue" if on else "&Stop"))
        self.stop_on_error = QCheckBox("S&top on Error")
        buttons = QHBoxLayout()
        buttons.addWidget(self.stop_btn)
        buttons.addWidget(self.stop_on_error)
        buttons.addStretch()
        for text, slot in (("&Copy", self._copy), ("S&ave", self._save), ("C&lear", self.list.clear),
                           ("E&xit", self.hide)):
            b = QPushButton(text)
            b.clicked.connect(slot)
            buttons.addWidget(b)
        layout = QVBoxLayout(self)
        layout.addWidget(self.list)
        layout.addLayout(buttons)
        self.resize(720, 360)
        self.seq = 0

    def add(self, direction: str, data: bytes) -> None:
        self.seq += 1
        if self.stop_btn.isChecked() or not self.isVisible():
            return
        self.list.addItem(traffic_line(self.seq, direction, data))
        while self.list.count() > TRAFFIC_LIMIT:
            self.list.takeItem(0)
        self.list.scrollToBottom()

    def error(self, text: str) -> None:
        if self.isVisible() and not self.stop_btn.isChecked():
            self.list.addItem(f"{self.seq % 1_000_000:06d} | ERR | {text}")
            if self.stop_on_error.isChecked():
                self.stop_btn.setChecked(True)

    def _copy(self) -> None:
        QApplication.clipboard().setText("\n".join(i.text() for i in self.list.selectedItems()))

    def _save(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Save", "commdata.txt", "Modbus comm. data (*.txt)")
        if path:
            Path(path).write_text("\n".join(self.list.item(i).text() for i in range(self.list.count())) + "\n",
                                  encoding="utf-8")


class ErrorCountersDialog(QDialog):
    def __init__(self, window: PollWindow, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Error Counters")
        st = window.engine.stats(window.poll_id)
        lst = QListWidget()
        lst.addItem(f"{st.tx}\tTransmitted requests")
        if not st.by_status:
            lst.addItem("NO ERRORS!")
        for status, count in sorted(st.by_status.items()):
            lst.addItem(f"{count}\t{status.text}")
        ok = QPushButton("OK")
        ok.clicked.connect(self.accept)
        layout = QVBoxLayout(self)
        layout.addWidget(lst)
        layout.addWidget(ok)


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("MultiPoll Studio")
        self.qsettings = QSettings("MultiPoll Studio", "MultiPoll Studio")
        self.conn_settings = self._load_conn_settings()
        self.traffic = TrafficWindow(self)
        self.engine = PollEngine(
            on_traffic=lambda d, b: post_to_gui(lambda: self.traffic.add(d, b)),
            on_connection=lambda up, text: post_to_gui(lambda: self._connection_changed(up, text)),
        )
        self.mdi = QMdiArea()
        self.mdi.setViewMode(QMdiArea.SubWindowView)
        self.mdi.subWindowActivated.connect(self._sync_actions)
        self.setCentralWidget(self.mdi)
        self.conn_label = QLabel("No connection")
        self.statusBar().addPermanentWidget(self.conn_label)
        self.statusBar().showMessage("For Help, press F1.")
        self._build_menus()
        self.resize(1000, 680)
        self.new_window()
        if self.qsettings.value("auto_connect", False, type=bool):
            self.quick_connect()

    # -- settings -------------------------------------------------------------------
    def _load_conn_settings(self) -> ConnectionSettings:
        raw = self.qsettings.value("connection", "")
        try:
            return ConnectionSettings.from_dict(json.loads(raw)) if raw else ConnectionSettings()
        except (ValueError, TypeError):
            return ConnectionSettings()

    def _save_conn_settings(self) -> None:
        self.qsettings.setValue("connection", json.dumps(self.conn_settings.to_dict()))

    # -- menus ------------------------------------------------------------------------
    def _act(self, menu, text, slot, shortcut=None, checkable=False, needs_window=False) -> QAction:
        a = QAction(text, self)
        if shortcut:
            a.setShortcut(QKeySequence(shortcut))
        a.setCheckable(checkable)
        a.triggered.connect(slot)
        menu.addAction(a)
        if needs_window:
            self._window_actions.append(a)
        return a

    def _build_menus(self) -> None:
        self._window_actions: list[QAction] = []
        mb = self.menuBar()
        w = lambda fn: (lambda *_: (win := self.active()) and fn(win))  # noqa: E731

        f = mb.addMenu("&File")
        self._act(f, "&New", self.new_window, "Ctrl+N")
        self._act(f, "&Open…", self.open_document, "Ctrl+O")
        self._act(f, "&Close", lambda: self.mdi.closeActiveSubWindow(), needs_window=True)
        self._act(f, "&Save", w(lambda x: x.save()), "Ctrl+S", needs_window=True)
        self._act(f, "Save &As…", w(self._save_as), needs_window=True)
        f.addSeparator()
        self._act(f, "Open &Workspace…", self.open_workspace)
        self._act(f, "Sa&ve Workspace…", self.save_workspace)
        f.addSeparator()
        self._act(f, "&Print…", w(self._print), "Ctrl+P", needs_window=True)
        f.addSeparator()
        self._act(f, "E&xit", self.close)

        e = mb.addMenu("&Edit")
        self._act(e, "&Copy", w(lambda x: x.copy_selection()), "Ctrl+C", needs_window=True)
        self._act(e, "&Paste", w(lambda x: x.paste_aliases()), "Ctrl+V", needs_window=True)
        self._act(e, "Select &All", w(lambda x: x.table.selectAll()), "Ctrl+A", needs_window=True)

        c = mb.addMenu("&Connection")
        self._act(c, "&Connect…", self.connect_dialog, "F3")
        self.disconnect_act = self._act(c, "&Disconnect", self.disconnect, "F4")
        c.addSeparator()
        auto = c.addMenu("Auto Connect")
        self.auto_act = self._act(auto, "&Enable at startup", self._toggle_auto, checkable=True)
        self.auto_act.setChecked(self.qsettings.value("auto_connect", False, type=bool))
        self._act(c, "&Quick Connect", self.quick_connect, "F5")

        s = mb.addMenu("&Poll")
        self._act(s, "Poll &Setup…", w(lambda x: x.open_definition()), "F8", needs_window=True)
        self._act(s, "Read/Write &Once", w(lambda x: x.read_once()), "F6", needs_window=True)
        self.enabled_act = self._act(s, "Polling &Enabled", w(lambda x: x.set_enabled(self.enabled_act.isChecked())),
                                     "Shift+F6", checkable=True, needs_window=True)
        s.addSeparator()
        self._act(s, "&Use as Default", w(self._use_as_default), needs_window=True)

        fn = mb.addMenu("&Write")
        for fc, text, key in ((FC.WRITE_SINGLE_COIL, "0&5: Write Single Coil…", "Alt+F5"),
                              (FC.WRITE_SINGLE_REGISTER, "0&6: Write Single Register…", "Alt+F6"),
                              (FC.WRITE_MULTIPLE_COILS, "15: Write &Coils…", "Alt+F7"),
                              (FC.WRITE_MULTIPLE_REGISTERS, "16: Write &Registers…", "Alt+F8"),
                              (FC.MASK_WRITE_REGISTER, "22: &Mask Write Register…", None),
                              (FC.READ_WRITE_MULTIPLE_REGISTERS, "2&3: Read/Write Registers…", None)):
            self._act(fn, text, w(lambda x, fc=fc: x.open_function(fc)), key, needs_window=True)

        d = mb.addMenu("&Display")
        group = QActionGroup(self)
        self.fmt_actions: dict[DisplayFormat, QAction] = {}
        keys = {DisplayFormat.SIGNED: "Alt+Shift+S", DisplayFormat.UNSIGNED: "Alt+Shift+U",
                DisplayFormat.HEX: "Alt+Shift+H", DisplayFormat.BINARY: "Alt+Shift+B"}
        for fmt in DisplayFormat:
            a = self._act(d, fmt.label, w(lambda x, fmt=fmt: x.set_display(fmt)), keys.get(fmt), checkable=True,
                          needs_window=True)
            group.addAction(a)
            self.fmt_actions[fmt] = a
        d.addSeparator()
        addr_group = QActionGroup(self)
        self.plc_act = self._act(d, "&PLC Addresses (Base 1)", w(lambda x: x.set_plc_addresses(True)),
                                 checkable=True, needs_window=True)
        self.proto_act = self._act(d, "P&rotocol Addresses (Base 0)", w(lambda x: x.set_plc_addresses(False)),
                                   checkable=True, needs_window=True)
        addr_group.addAction(self.plc_act)
        addr_group.addAction(self.proto_act)

        tools = mb.addMenu("&Tools")
        self._act(tools, "&Frame Console…", self.test_center, "Alt+T")
        self._act(tools, "&Communication Traffic…", self.show_traffic)
        tools.addSeparator()
        self._act(tools, "&Error Counters", w(lambda x: ErrorCountersDialog(x, self).exec()), "F11", needs_window=True)
        self._act(tools, "&Reset Counters", w(lambda x: x.reset_counters()), "F12", needs_window=True)
        tools.addSeparator()
        self._act(tools, "Start &Log…", w(lambda x: x.start_log()), "Alt+L", needs_window=True)
        self._act(tools, "Stop L&ogging", w(lambda x: x.stop_log()), "Alt+O", needs_window=True)

        v = mb.addMenu("&View")
        tb = self._act(v, "&Status Bar", lambda on: self.statusBar().setVisible(on), checkable=True)
        tb.setChecked(True)
        self._act(v, "&Always On Top", self._always_on_top, checkable=True)

        win = mb.addMenu("&Window")
        self._act(win, "&Cascade", self.mdi.cascadeSubWindows)
        self._act(win, "&Tile", self.mdi.tileSubWindows)

        h = mb.addMenu("&Help")
        self._act(h, "&About MultiPoll Studio…", self.about)
        self._sync_actions()

    # -- windows -------------------------------------------------------------------------
    def active(self) -> PollWindow | None:
        sub = self.mdi.activeSubWindow()
        return sub.widget() if sub else None

    def windows(self) -> list[PollWindow]:
        return [s.widget() for s in self.mdi.subWindowList()]

    def _add_window(self, win: PollWindow) -> QMdiSubWindow:
        sub = _PollSubWindow(win)
        self.mdi.addSubWindow(sub)
        sub.show()
        return sub

    def new_window(self) -> PollWindow:
        raw = self.qsettings.value("default_document", "")
        doc = None
        if raw:
            try:
                doc = PollDocument.from_dict(json.loads(raw))
                doc.aliases.clear()
            except (ValueError, TypeError, KeyError):
                doc = None
        win = PollWindow(self.engine, doc)
        self._add_window(win)
        return win

    def open_document(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "Open", "", DOC_FILTER)
        if path:
            self.open_path(path)

    def open_path(self, path: str) -> PollWindow | None:
        try:
            doc = PollWindow.load(path)
        except (OSError, ValueError, KeyError, TypeError) as exc:
            show_error(self, ValueError(f"{path}: {exc}"))
            return None
        win = PollWindow(self.engine, doc, path)
        self._add_window(win)
        return win

    def _save_as(self, win: PollWindow) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Save As", win.path or win.untitled_name + ".mpoll", DOC_FILTER)
        if path:
            win.save(path)

    def save_workspace(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "Save workspace", "workspace.mpollw", WORKSPACE_FILTER)
        if not path:
            return
        entries = []
        for sub in self.mdi.subWindowList():
            g = sub.geometry()
            entries.append({"document": sub.widget().doc.to_dict(),
                            "geometry": [g.x(), g.y(), g.width(), g.height()]})
        data = {"format": "multipoll-workspace", "version": 1, "connection": self.conn_settings.to_dict(),
                "windows": entries}
        Path(path).write_text(json.dumps(data, indent=2), encoding="utf-8")

    def open_workspace(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, "Open workspace", "", WORKSPACE_FILTER)
        if path:
            self.load_workspace(path)

    def load_workspace(self, path: str) -> None:
        try:
            data = json.loads(Path(path).read_text(encoding="utf-8"))
            if data.get("format") != "multipoll-workspace":
                raise ValueError("not a MultiPoll Studio workspace")
            docs = [(PollDocument.from_dict(e["document"]), e.get("geometry")) for e in data["windows"]]
            conn = ConnectionSettings.from_dict(data.get("connection", {}))
        except (OSError, ValueError, KeyError, TypeError) as exc:
            show_error(self, ValueError(f"{path}: {exc}"))
            return
        for sub in self.mdi.subWindowList():
            if not sub.widget().maybe_save():
                return
        self.mdi.closeAllSubWindows()
        self.conn_settings = conn
        for doc, geom in docs:
            sub = self._add_window(PollWindow(self.engine, doc))
            if geom:
                sub.setGeometry(*geom)

    def _use_as_default(self, win: PollWindow) -> None:
        self.qsettings.setValue("default_document", json.dumps(win.doc.to_dict()))
        self._save_conn_settings()

    def _print(self, win: PollWindow) -> None:
        from PySide6.QtPrintSupport import QPrintDialog, QPrinter

        printer = QPrinter()
        if QPrintDialog(printer, self).exec():
            win.render(printer)

    # -- connection -------------------------------------------------------------------------
    def connect_dialog(self) -> None:
        dlg = ConnectionDialog(self.conn_settings, self)
        if dlg.exec():
            self.conn_settings = dlg.settings()
            self._save_conn_settings()
            self.quick_connect()

    def quick_connect(self) -> None:
        try:
            self.engine.start(self.conn_settings)
        except ModbusError as exc:
            self._connection_changed(False, exc.detail or exc.status.text)
            show_error(self, exc)

    def disconnect(self) -> None:
        self.engine.stop()
        for win in self.windows():
            win.error_line.setText(Status.NO_CONNECTION.text)

    def _connection_changed(self, up: bool, text: str) -> None:
        self.conn_label.setText(text if up else f"No connection — {text}")
        if not up:
            self.traffic.error(text)
        self._sync_actions()

    def _toggle_auto(self, on: bool) -> None:
        self.qsettings.setValue("auto_connect", on)

    # -- misc ----------------------------------------------------------------------------------
    def test_center(self) -> None:
        if not self.engine.running:
            show_error(self, ModbusError(Status.NO_CONNECTION))
            return
        TestCenterDialog(self.engine, self).exec()

    def show_traffic(self) -> None:
        self.traffic.show()
        self.traffic.raise_()

    def _always_on_top(self, on: bool) -> None:
        self.setWindowFlag(Qt.WindowStaysOnTopHint, on)
        self.show()

    def about(self) -> None:
        QMessageBox.about(self, "About MultiPoll Studio",
                          f"<b>MultiPoll Studio {__version__}</b><br>Open-source Modbus master test tool.<br>"
                          "Licensed under the GNU GPL v3 or later.")

    def _sync_actions(self, *_) -> None:
        win = self.active()
        for a in self._window_actions:
            a.setEnabled(win is not None)
        self.disconnect_act.setEnabled(self.engine.running)
        if win is not None:
            self.enabled_act.setChecked(win.doc.definition.enabled)
            self.fmt_actions[win.doc.view.display].setChecked(True)
            (self.plc_act if win.doc.view.plc_addresses else self.proto_act).setChecked(True)

    def closeEvent(self, event) -> None:  # noqa: N802 - Qt API
        for win in self.windows():
            if not win.maybe_save():
                event.ignore()
                return
        self.engine.stop()
        for win in self.windows():
            win.shutdown()
        event.accept()


class _PollSubWindow(QMdiSubWindow):
    def __init__(self, win: PollWindow):
        super().__init__()
        self.setWidget(win)
        self.setAttribute(Qt.WA_DeleteOnClose)
        self.setWindowTitle(win.title())
        win.windowTitleChanged.connect(self.setWindowTitle)

    def closeEvent(self, event) -> None:  # noqa: N802 - Qt API
        win: PollWindow = self.widget()
        if not win.maybe_save():
            event.ignore()
            return
        win.shutdown()
        event.accept()


def run(argv: list[str]) -> int:
    app = QApplication(argv)
    app.setApplicationName("MultiPoll Studio")
    install()
    main = MainWindow()
    for arg in argv[1:]:
        if arg.endswith(".mpollw"):
            main.load_workspace(arg)
        else:
            main.open_path(arg)
    main.show()
    return app.exec()
