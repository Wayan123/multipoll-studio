# Changelog — MultiPoll Studio for Windows

## Unreleased — independent interface

- Rename generated polling windows to `Poll N`; reorganize menus around Poll, Write and Monitor, and label
  `Poll Setup` and `Frame Console`. Poll status displays the unit, function and interval separately from
  request/error counts, with a tooltip containing the full status.
- Show data type and word order explicitly in display presets. Stored `.mpoll` format keys and decoding
  semantics remain unchanged.
- Format traffic as sequence, direction and bytes. Frame Console list files now require the header
  `MultiPoll Studio Frame List`; replace the first line of older lists after backing them up.
- Update Python and Windows UI terminology together, without changing Modbus frames or write safeguards.
- Refresh simulator screenshots using application-window rendering rather than capturing the desktop.

## 0.5.0 — 2026-10-30

- Rename to **MultiPoll Studio**: `MultiPollStudio.exe`, `HKCU\Software\MultiPollStudio`,
  `%LOCALAPPDATA%\MultiPollStudio`, `.mpoll` / `.mpollw`, `multipoll-document` / `multipoll-workspace` /
  `multipoll-profile`. Python package/CLI renamed to `multipoll_studio` / `multipoll-studio`.
  Old files/settings remain untouched; no automatic import or legacy format aliases.
- Add a native toolbar: document commands, active-target connect/disconnect, Definition, Read Once, periodic
  polling toggle, FC05/06/15/16/22/23, Traffic, Test Center and help. Own vector-drawn raster icons, tooltips,
  target ID/connection label, state-aware buttons, narrow-window overflow and View → Toolbar visibility.
- Toolbar connection commands affect only the active bus; windows on other connections continue polling.
  Function buttons open the existing dialogs; write protection, confirmation and audit are retained.
- Test Center uses the active window's engine instead of always using the main connection.
  Write-dialog titles show the target ID and transport.
- Context help (Shift+F1): click a toolbar command to explain it without executing it; Escape cancels.
- Add a GUI scenario against the five-device simulator for cross-ID/connection routing and write safeguards.

## 0.4.0 — 2026-10-29

Testing and commissioning helpers for RS-485 sensors.

- **Device profiles** (Device → Add Device, Ctrl+D): ten built-in register maps (Volison ADM-4204C/V, BGT-SMPS soil
  8-in-1, Peacefair PZEM-014/016, Eastron SDM120 new and legacy, XY-MD02, JXCT JXBS-3001-TR and -NPK, RS-…-N01 soil
  transmitter). A profile opens one window per register block with names, scaling, units, data types and write
  limits. Own profiles: JSON files in `%LOCALAPPDATA%\OpenPoll\profiles` (strict schema, unknown keys rejected).
  Each profile lists its manual; facts not confirmed in a manufacturer-hosted manual are marked unverified.
- **Device Settings** (Ctrl+E), like a vendor configuration tool: reads the current slave ID, baud, parity and
  other settings and sets them with one click, without typing a register or function code. After a change the
  windows of that device follow the new ID, the connection follows a new baud/parity, and the value is read back
  to verify it. Profile actions: factory reset (ADM), reset energy (PZEM FC 0x42). *Find forgotten ID* uses the
  device's general address (BGT 0xFE, PZEM 0xF8). Every change goes through write protection, confirmation and the
  audit log.
- **Find Device** (Connection → Find Device, Ctrl+F): read-only search over baud rates × parity (× 8N2) × IDs,
  common defaults first; quick (IDs 1, 2, 3, 247, 254) or full range; time estimate; identifies a found device by
  its profile's model register; "Use" applies baud, parity and ID. The port is released from its connection while
  the search runs.
- **Diagnosis and one-click fixes**: a failed poll shows *Why? / Fix…* on the window's error line (also Device →
  Diagnose, Ctrl+G). It lists likely causes and manual checks for every status (port busy or missing, timeout, CRC,
  other slave answered, illegal function/address/value, busy, gateway, …) and offers safe fixes that never write to
  the device: find device, raise the timeout to the suggested value, switch FC03↔FC04, probe which addresses exist
  and shrink the window, poll the ID that answered, turn on Remove Echo, try the address one lower, poll slower,
  reconnect. Connect failures no longer pop up a box; they appear on the error line with the same link.
  Help → Troubleshooting Guide (F1) prints the whole table.
- **Several connections at once**: Connection → Window Connection gives a window its own COM port or TCP host.
  Windows on the same port share one connection; different ports poll in parallel. Saved in documents and
  workspaces (`"connection"` key); Disconnect, write protection and the audit log cover all connections.
- Address fixes keep names, tags and formats on the same registers when a window's range moves.
- RTU framing accepts FC 0x42 replies and replies from another address for recovery queries.
- `tools/device_sim.py`: standard-library simulator of five devices on one bus (RTU over TCP and Modbus TCP) for
  testing settings writes without hardware; `OpenPollUiTest.exe devices <host> <port> <dir>` drives it.
- EXE size: 204 KB → 344 KB (new dialogs and 10 embedded profiles).

## 0.3.0 — 2026-10-29

Roadmap item N1 from `../outputs/openpoll-ai-iot-features.md` (data types and byte order).

- Data types per window or cell: signed/unsigned/hex/binary 16-bit, BCD16/32, signed/unsigned/float 32-bit, Unix
  time 32-bit, signed/unsigned/float 64-bit (exact text beyond 2^53), ASCII string of 1–64 registers, bit field.
  Every multi-register type in byte order ABCD, CDAB, BADC or DCBA; 16-bit types as AB or BA. Display → *Other
  Format…* and the cell context menu.
- *Interpret As* (Ctrl+I): the selected registers decoded as every type × byte order at once, following the selection
  and live values; *Use for cell* applies a row.
- Grid writes for every type: strings use FC16 with NUL padding (printable ASCII only), bit fields merge into the last
  read register value and refuse to write when that value is stale, integers accept `0x…` raw patterns, Unix time
  accepts ISO-8601. A value that does not fit before the window end is shown as hex and cannot be written.
- Tag map CSV has an optional `format` column; documents store new formats as keys such as `f32_badc`, `str8`,
  `bits4.3`. The ten Modbus Poll keys are unchanged, so 0.2 files still open. Files that use the new keys cannot be
  opened by the Python edition yet.
- The 06 dialog offers the window's custom format next to the ten presets.
- EXE size: 182 KB → 204 KB.

## 0.2.0 — 2026-10-29

Features chosen from the research brief `../outputs/openpoll-industrial-features.md` (wave 1).

- Logging: ISO-8601 timestamps with milliseconds (local with UTC offset, or UTC `Z`), header row with tag names and
  units, status and response-time columns, failed polls kept as rows, on-change logging with deadband and heartbeat,
  daily or size-based rotation with retention. Log format changed: the first columns are now
  `timestamp,status,rtt_ms`; "Log Date" is replaced by the timestamp setting.
- Poll results are timestamped in UTC; response time (RTT) is measured per poll.
- Link KPIs in the status line: success % over the last 100 polls, RTT last/avg/max.
- Tags per address: name, scale/offset, unit, decimals, write limits; scaled display and entry in the grid;
  tag map CSV import/export; tags saved in `.opoll` documents (`"tags"` key).
- Alarms HH/H/L/LL with per-limit deadband, cell colours, alarm count, events in the log, alarm history.
- Trend window (up to 12 values, gaps on failed polls, CSV/PNG export).
- Write safeguards: write protection switch, optional confirmation with old→new value (on by default), append-only
  hash-chained audit log in `%LOCALAPPDATA%\OpenPoll\audit.jsonl`. Test Center raw frames are allow-listed while
  write protection is on (FC08 restart/listen-only and serial broadcasts are refused).
- Read-only unit-ID scan and FC43/14 Read Device Identification.
- Reconnect back-off 2 s → 30 s with ±10 % jitter.
- Serial response-timeout suggestion from baud rate and frame size.
- Fixes: negative numbers are no longer escaped as text in CSV logs; FC43 RTU responses are framed by line silence.
- EXE size: ~120 KB → 182 KB. Still one file, no NuGet; the chart uses `System.Windows.Forms.DataVisualization`
  from .NET Framework.

## 0.1.0 — 2026-10-29

First release: Modbus Poll 5.0.1-level master (serial RTU/ASCII, TCP, UDP, RTU/ASCII over TCP/UDP; FC 01–06, 15, 16,
22, 23; MDI windows; display formats; traffic; Test Center; logging; documents and workspaces).
