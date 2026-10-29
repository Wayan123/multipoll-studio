# OpenPoll for continuous industrial use: feature research and roadmap

Date: 2026-10-29. Scope: which current features in Modbus master/test tools and industrial practice make OpenPoll
(Python `openpoll/` and Windows `openpoll-win/`) useful for long-running industrial work, and which of them fit a
single dependency-free WinForms EXE (no NuGet, .NET Framework 4.8 only). The EXE was ~120 KB before this work and
measures 182,272 bytes after wave 1 (build 2026-10-29 19:00, `openpoll-win/dist/OpenPoll.exe`).

Evidence files: `outputs/.drafts/openpoll-industrial-features-research-tools.md` (T1),
`outputs/.drafts/openpoll-industrial-features-research-standards.md` (T2) and
`outputs/.drafts/openpoll-industrial-features-research-integration.md` (T3). Numbered citations [n] resolve to the
Sources section, which also names the evidence file each source came from.

## Executive summary

- The reference commercial tool is still Witte Modbus Poll, now 13.2.1 (2026-05-10) [1][2]. Since the 5.0.1 build
  that OpenPoll was modelled on [4], its changelog adds a real-time chart, scaling, Value Names, a slave-ID range
  scan, a new log file at midnight and Modbus/TCP Security on port 802 [2]. The current manual also documents an
  ISO-8601 "T" separator and millisecond timestamps in the text log [3]. "Log on change" is not new: 5.0.1 already
  had it [4].
- Tool counts below mean "documented in N of 18 surveyed tools" (10 named tools plus 8 maintained open-source tools
  with 2024–2026 releases); "not documented" is not proof of absence [8].
- The three newest open-source tools (ModbusLens, Modbench, ModbusReader) converge on one bundle: trend charts,
  engineering-unit scaling, register maps or tag lists, unit-ID scan, register scan and a slave simulator. Two of
  them, ModbusLens and Modbench, also add alarms and write safeguards [5][6][7][8]. They are young: ModbusLens has 76 stars (repo created
  2026-04-25); Modbench (12 stars) and ModbusReader (4 stars) were both created 2026-10-13 [8].
- Features aimed at unattended operation are rare across the 18 surveyed tools: log rotation (2/18), per-point data
  quality (1/18, with partial signals in two more), MQTT (1/18), write safeguards (2/18) [8]. [INFERENCE] This is
  where OpenPoll can be distinctly useful; rarity among tools does not by itself show user demand.
- Telegraf, ThingsBoard Gateway and EMQX Neuron all define per-point name, address, type, byte/word order and linear
  scaling [9][10][11][12]. Neuron documents per-driver round-trip time and read-error counters [13]; Telegraf
  reports aggregate gather time and errors per input plugin (reviewer, checked live).
- Standards and security guidance point to read-only by default, explicit write confirmation and an append-only audit
  log for engineering tools on live plants. These are design inferences from NIST SP 800-82r3, MITRE ATT&CK for ICS
  mitigations and IEC 62443 requirement titles, not quoted requirements [14][15][16][17].
- Modbus/TCP Security (TLS, port 802, current spec v36 dated 2021-07-30) [18] is in three of the 18 surveyed
  masters: Modbus Poll 13, ModScan64 v8 and modbusctl [2][3][19][20]. Device adoption appears low, but that rests on practitioner
  statements and one vendor manual, not on a survey [21][22].

## 1. Where OpenPoll stands today

Before this work, both editions covered the Modbus Poll 5.0.1 baseline [4]: five connection types,
FC 01–06/15/16/22/23, multiple poll windows, ten display formats (seven base formats plus inverse variants), aliases,
traffic monitor, Test Center and text logging [23]. When T3 read the code, results were stamped with local time,
log lines had no milliseconds, UTC offset, header row or rotation [24].

Wave 1 (section 3) was implemented in `openpoll-win` while this brief was being verified. Snapshot of the working
tree at 2026-10-29 19:00 (no git repository; order inferred from file times): UTC timestamps and logging v2 in
`Support.cs`/`Engine.cs`; RTT/success KPIs and reconnect back-off in `Engine.cs`; tags, scaling, alarms and
tag-map CSV in `Tags.cs`; write protection, confirmation and hash-chained audit log in `Engine.cs`/`Support.cs`;
unit-ID scan and FC43/14 in `Discovery.cs`; trend chart on `System.Windows.Forms.DataVisualization` in
`TrendWindow.cs` [24]. The Python edition has none of these yet (reviewer grep of `openpoll/src`).

## 2. Findings by theme

### 2.1 Long-run logging
- RFC 3339 states that "true interoperability is best achieved by using Coordinated Universal Time (UTC)", and that
  "the interoperability problems of unqualified local time are deemed unacceptable for the Internet" [25]. Sparkplug
  and OPC UA both carry UTC timestamps [26][27].
- Modbus Poll 13 offers "Log if data changed only", "Log Errors", flush, append, "New Log File at midnight", an
  ISO-8601 "T" separator and a "Log ms" option [3][28].
- Size-based rotation appears in ModbusReader [7]. ThingsBoard Gateway keeps a local SQLite buffer with per-file
  size rotation, a file-count cap and a retention period [29].
- OPC UA defines an absolute deadband ("Generate a Notification if (absolute value of (last cached value - current
  value) > AbsoluteDeadband)") [30]. ThingsBoard offers an "on change or report period" strategy
  (`ON_CHANGE_OR_REPORT_PERIOD`) [31].

### 2.2 Data quality and link KPIs
- ModbusScope 5.0.1 tracks quality per data point (good, degraded, invalid, no value) and exports it to CSV [32].
- EMQX Neuron exposes `last_rtt_ms`, `tag_reads_total` and `tag_read_errors_total` [13]. Modbench reports failures
  by cause and response times per function code, with a suggested timeout of "4 × the slowest answer" [6].

### 2.3 Tag maps and scaling
- Telegraf's per-field definition has `address`, `name`, `type`, `byte_order` and `scale` ("field_value * scale")
  [9]. ModbusLens uses two-point linear scaling (raw 0–4095 → 0–100 PSI) [5]. Modbench and ModbusReader add units
  and enumerations or state labels to their register maps [6][7]. ModbusLens (community device profiles) and
  Modbench ("over 400 shared maps") offer shared libraries [5][6].

### 2.4 Alarms
- OPC UA LimitAlarmType defines HighHigh/High/Low/LowLow with "HighHigh > High > Low > LowLow" and a deadband per
  limit. Its example: a value that exceeds a HighLimit of 20 with a HighDeadband of 1 "will remain in alarm until the
  Value drops below 19" [33].
- Among the 18 surveyed tools, only ModbusLens and Modbench document real alarms. Modbus Poll has conditional colours
  only [8].

### 2.5 Trend chart
- Modbus Poll can "plot up to 12 data series in a chart in real time", with PNG and CSV export [3]. Charts appear in
  5 of the 18 surveyed tools, 4 of them among the 8 newer ones [8].
- Microsoft Learn documents the `Chart` control in `System.Windows.Forms.DataVisualization.dll` for .NET Framework
  4.8 [34]. Observed: the assembly is in the Windows GAC on the development machine and OpenPoll's trend window
  uses it; the chart code adds to the EXE only its own wrapper class, not the charting library.

### 2.6 Device discovery
- Modbus has no discovery: "Every discovery operation is performed by brute-force polling" [35]. Unit-ID scan
  appears in 9 of the 18 surveyed tools [8].
- NIST SP 800-82r3 says OT network owners "should exercise extreme caution when permitting active scanning on an
  operational network" [14]. Nmap classifies its `modbus-discover` slave-ID script as "discovery, intrusive" [36].
- FC43/14 Read Device Identification is the only standard identity query. FC17 output is device-specific [37][38].
  Chipkin notes FC43/FC17 "are optional and rarely supported by field devices" [35], so identification is
  best-effort.
  A timeout does not prove absence, because a server that detects a parity or CRC error returns no response [37].

### 2.7 Serial timing
- RTU t1.5/t3.5 must be strictly respected up to 19200 baud. Above that the spec recommends fixed values of 750 µs
  and 1.750 ms [39]. The typical response timeout is "from 1s to several second at 9600 bps" [39].
- [INFERENCE, arithmetic] A full 125-register FC03 response is 255 bytes of 11 bits each [39], so it takes about
  2.34 s at 1200 baud and 292 ms at 9600 baud. OpenPoll's default response timeout is 1000 ms, so it fails for
  full-size reads at 1200 and 2400 baud. Caveat: the spec's 100–200 ms "turnaround delay" refers to broadcasts [39];
  OpenPoll's timeout suggestion uses 200 ms as an assumed device processing allowance, which is an [INFERENCE].

### 2.8 Safe writes on live plants
- MITRE M0800 lists "HMI read-only vs. read-write modes" (under T0886 Remote Services) and says to "enable write
  protection for parameters" (under T0836 Modify Parameter) [15]. Both address controllers and HMIs; applying them
  to a test master is an [INFERENCE].
  NIST SP 800-82r3 AC-6 describes privilege models where "lower privileges include read access, and higher
  privileges include write access" [14].
- A Modbus device does not range-check values for the application: ILLEGAL DATA VALUE "does NOT mean that a data
  item submitted for storage in a register has a value outside the expectation of the application program" [37].
- IEC 62443-3-3 requirement titles include "SR 2.8 – Auditable events" [16]. The IEC 62443-4-2 requirement CR 3.9
  "Protection of audit information" covers protecting audit logs from modification and deletion [17]. The
  normative 62443 text is paywalled; only titles and the ISASecure summary were read.
- The PIPEDREAM tool described in CISA AA22-103A can "Send custom Modbus commands" over Modbus TCP 502 [40].
  FrostyGoop "sent Modbus commands to ENCO controllers, causing inaccurate measurements and system malfunctions"
  [41]. T2 cautions that write safeguards in a master tool protect against operator error and add accountability;
  they are not security controls.

### 2.9 Bridging
- Neuron, ThingsBoard Gateway and Telegraf publish Modbus data northbound over MQTT [42][43][29]. Sparkplug 3.0 is
  ISO/IEC 20237:2023 [46]. It requires Protobuf payloads, an NDEATH will message (death certificate) and rebirth
  handling [45][26].
- Counterpoint: only 1 of 18 surveyed tools has MQTT, and T1 found "no evidence that users of GUI masters expect
  MQTT" [8].
- [INFERENCE] For a single dependency-free EXE, plain MQTT 3.1.1 publish of JSON (similar to Neuron's
  `{timestamp, values, errors}` payload [44]) is feasible. Sparkplug B and an OPC UA server are better left to
  gateways.

### 2.10 Modbus/TCP Security
- Spec v36 (2021-07-30) requires mutual TLS, X.509v3 certificates and TLS 1.2 or better, and uses port 802 [18]. IANA
  lists `mbap-s` on 802 tcp/udp, registered 2014-06-10 [47]. The role comes from the client certificate, and a
  request that is not authorized returns exception 01 [18].
- Libraries support it (pymodbus, digitalpetri modbus) [48][49]. T2 found no primary vendor document naming a
  supporting device, and practitioners report little field adoption [21][22].

## 3. Ranked feature roadmap for OpenPoll

Criteria, in order: (1) value for unattended, long-running operation; (2) reduction of operator-error risk on live
plants; (3) prevalence among the surveyed tools and gateways; (4) cost in the single-EXE budget. The order is an
[INFERENCE] from these criteria; no source measures operator demand. Unit-ID scan ranks below write safeguards
despite being more common (9/18 vs 2/18) because it is a commissioning aid, while write safeguards and logging
affect every long run, and because NIST advises caution with active scanning [14].

"Write" (rank 2) means FC05/06/15/16/22/23, the FC08 sub-functions that change device state (01 restart, 04 force
listen-only, 0A clear counters, 14) and any serial broadcast (unit 0) [37] (T2 F3.4, §4c).

| Rank | Feature | Reason (evidence) | Status 2026-10-29 19:00 (openpoll-win) |
|---|---|---|---|
| 1 | Logging v2: UTC or local-with-offset ISO-8601 with ms, header row, status and RTT columns, error rows kept, daily/size rotation with retention, absolute deadband + heartbeat | Every long run produces a log; RFC 3339 [25], Modbus Poll 13 [3], ThingsBoard [29][31], OPC UA deadband [30] | Done, tested |
| 2 | Write safeguards: write-protection switch (default off, persisted), confirmation dialog with old→new value, hash-chained append-only audit log; Test Center raw frames allow-listed while protected | MITRE M0800 [15], NIST AC-6 [14], IEC 62443 audit titles [16][17]; device does not range-check [37] | Done, tested (incl. FC08/04 blocked) |
| 3 | Link KPIs: success % over last 100 polls, last/avg/max RTT; serial timeout suggestion | Neuron [13], Modbench [6], serial timing [39] | Done, tested |
| 4 | Tags: name, scale/offset, unit, decimals, write limits; strict CSV import/export with formula-injection guard | Telegraf/ThingsBoard/Neuron [9][10][11][12]; CVE in a file importer (T2 F4.10) argues for strict parsing | Done, tested. Not yet: per-tag data type, byte/word order, value names |
| 5 | Alarms HH/H/L/LL with per-limit deadband, cell colouring, events in log and history | OPC UA LimitAlarmType [33]; ModbusLens/Modbench [5][6] | Done, tested. Not yet: on/off delay (OPC 10000-9 §5.8.2, T3 F3) |
| 6 | Trend chart: up to 12 values, time window, gaps on failed polls, CSV/PNG export | Modbus Poll [3], 5/18 tools [8] | Done, tested |
| 7 | Read-only unit-ID scan with warning and cancel; FC43/14 identification (best-effort) | 9/18 tools [8], NIST caution [14], Nmap [36], Chipkin [35] | Done, tested against demo slave |
| 8 | Reconnect back-off 2 s → 30 s with ±10 % jitter | Node-RED [50], ModbusLens [5]; benefit is [INFERENCE] | Done |
| 9 | Register/address scan (read-only) | 9/18 tools [8]; Modbus Poll address scan [2] | Wave 2 |
| 10 | Per-tag data type, byte/word order and value names (enumerations) | Gateways [9][10][11]; Modbus Poll Value Names [2]; Modbench/ModbusReader [6][7] | Wave 2 |
| 11 | Per-value quality shown in grid and chart | ModbusScope [32]; T3 pattern 3 | Partly done: stale values greyed, chart gaps; no per-cell quality code |
| 12 | Inter-request pacing per slave and skip-after-K-failures | All three gateways (T3 F10), Neuron degradation (T3 F7) | Wave 2 (global delay between polls exists) |
| 13 | Tag map export for gateways (Telegraf/Neuron format) | T3 pattern 11a | Wave 2 |
| 14 | MQTT 3.1.1 publish-only JSON | Neuron payload [44]; only 1/18 tools [8] | Wave 2, low priority |
| 15 | Modbus/TCP Security client (SslStream, client certificate) | Spec [18]; low device adoption [21][22] | Wave 2, low demand |
| 16 | Python edition parity for wave 1 | Scope covers both editions; none of wave 1 in `openpoll/src` | Wave 2 |
| 17 | Sparkplug B / OPC UA server | Protobuf, state machine [26][45] | Out of scope; use a gateway |

## 4. Caveats and disagreements

- Feature claims for the newest open-source tools come from their own READMEs; none were run [5][6][7].
- The safe-write design is inferred, not a quoted standard requirement [14][15][16][17]. No source says write
  protection must be on by default; OpenPoll defaults it off and remembers the operator's choice.
- These safeguards protect against operator error and add accountability. They are not security controls: anyone
  with network access to a Modbus device can write to it with another tool [40][41].
- Serial defaults: the serial-line spec says "The default parity mode must be even parity" and "19.2 is the required
  default" baud rate, and "the use of no parity requires 2 stop bits" [39]. Chipkin lists 9600 8N1 as "Most common in
  field devices" [35]. OpenPoll defaults to 9600 8N1 (field practice, not the spec) and keeps the user's settings.
- Old modbus.org `docs/` PDF links return HTTP 404; current files live under `modbus.org/file/secure/` [51].
- The size budget changed: the EXE grew from ~120 KB to 182,272 bytes with wave 1, still without NuGet or extra DLLs.

## 5. Open questions

- Real-world adoption of Modbus/TCP Security in field devices beyond practitioner reports [21][22].
- Adapter-specific USB serial latency that should feed the timeout suggestion. T2 did not research it.
- Whether operators want alarm on/off delays in a test master, or only in SCADA.
- Sparkplug 4.0: the Sparkplug specification page still lists 3.0 as the latest version on 2026-10-29 [52].

## Sources

| [n] | Title | URL or artifact | Evidence file (ID) |
|---|---|---|---|
| 1 | Witte Software, Modbus Poll download page | https://www.modbustools.com/download.html | T1 (S1) |
| 2 | Witte Software, Modbus Poll changelog | https://www.modbustools.com/ModPollChangeLog.txt | T1 (S2) |
| 3 | Witte Software, Modbus Poll User Manual, dated 2026-05-06 | https://www.modbustools.com/mbpoll-user-manual.html | T1 (S4), T3 (30) |
| 4 | Modbus Poll 5.0.1 (build 450) reverse-engineering report (local) | `re/REPORT.md` | Not in T1–T3; added by verifier |
| 5 | ModbusLens README | https://github.com/CraftParking/ModbusLens | T1 (S35) |
| 6 | Modbench README | https://github.com/nobrega8/modbench | T1 (S37) |
| 7 | ModbusReader README | https://github.com/Mercurrial/ModbusReader | T1 (S39) |
| 8 | T1 per-tool table and feature-frequency matrix (§1–§3), compiled from T1 sources S1–S46 | `outputs/.drafts/openpoll-industrial-features-research-tools.md` §1–§3 | T1 (research note) |
| 9 | InfluxData, Telegraf Modbus input plugin README | https://github.com/influxdata/telegraf/blob/master/plugins/inputs/modbus/README.md | T3 (1) |
| 10 | ThingsBoard IoT Gateway, Modbus connector | https://thingsboard.io/docs/iot-gateway/config/modbus/ | T3 (7) |
| 11 | EMQX Neuron docs, Modbus RTU | https://docs.emqx.com/en/emqx-neuron/latest/configuration/south-devices/modbus-rtu/modbus-rtu.html | T3 (11) |
| 12 | EMQX Neuron docs, Groups and Tags | https://docs.emqx.com/en/emqx-neuron/latest/configuration/groups-tags/groups-tags.html | T3 (12) |
| 13 | EMQX Neuron docs, Create a Southbound Driver | https://docs.emqx.com/en/emqx-neuron/latest/configuration/south-devices/south-devices.html | T3 (13) |
| 14 | NIST SP 800-82 Rev. 3, Guide to OT Security, Sept 2023 | https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-82r3.pdf | T2 (S27), T3 (33) |
| 15 | MITRE ATT&CK for ICS, M0800 Authorization Enforcement | https://attack.mitre.org/mitigations/M0800/ | T2 (S24) |
| 16 | IEC 62443-3-3:2013 preview (iTeh, TOC pages) | https://cdn.standards.iteh.ai/samples/19488/7c0b753be32e46fc986c23c32efbdbe8/IEC-62443-3-3-2013.pdf | T2 (S22) |
| 17 | ISASecure, "The Case for ISA/IEC 62443 Security Level 2 as a Minimum for COTS Components", Feb 2024 | https://www.isasecure.org/hubfs/The-Case-for-ISA-IEC-62443-Security-Level-2-as-a-Minimum-FINAL.pdf?hsLang=en | T2 (S23) |
| 18 | Modbus.org, MODBUS/TCP Security Protocol Specification, MB-TCP-Security-v36_2021-07-30 | https://www.modbus.org/file/secure/modbussecurityprotocol.pdf | T2 (S1) |
| 19 | WinTECH ModScan64 v8 demo archive, `How_it_Works.rtf` | https://www.win-tech.com/demos/modscan64-8.zip | T1 (S21) |
| 20 | otfabric/modbusctl README | https://github.com/otfabric/modbusctl | T1 (S41) |
| 21 | Inductive Automation forum, "Modbus TCP Security over TLS" (topic 37738) | https://forum.inductiveautomation.com/t/modbus-tcp-security-over-tls/37738 | T2 (S8) |
| 22 | Schneider Electric, Modicon M262 Programming Guide EIO0000003651.12 (distributor copy) | https://kesko-onninen-pim-resources-production.s3-eu-west-1.amazonaws.com/pimdocuments/25251746.pdf | T2 (S9) |
| 23 | OpenPoll for Windows README (local) | `openpoll-win/README.md` | T3 (38) |
| 24 | OpenPoll-win source (local): `src/Core/Engine.cs`, `src/Core/Support.cs` | `openpoll-win/src/Core/Engine.cs`, `openpoll-win/src/Core/Support.cs` | T3 (38); re-read by verifier |
| 25 | RFC 3339, Date and Time on the Internet: Timestamps | https://www.rfc-editor.org/rfc/rfc3339 | T3 (34) |
| 26 | Eclipse Sparkplug 3.0 specification source, ch. 6 Payloads | https://github.com/eclipse-sparkplug/sparkplug/blob/master/specification/src/main/asciidoc/chapters/Sparkplug_6_Payloads.adoc | T3 (20) |
| 27 | OPC 10000-4 §7.11 DataValue | https://reference.opcfoundation.org/specs/OPC-10000-4/7.11 | T3 (27) |
| 28 | Witte Software, Modbus Poll v12 XML sample | https://www.modbustools.com/pollxml.html | T1 (S46) |
| 29 | ThingsBoard IoT Gateway, General Configuration (storage) | https://thingsboard.io/docs/iot-gateway/config/general/ | T3 (9) |
| 30 | OPC 10000-4 §7.22.2 DataChangeFilter | https://reference.opcfoundation.org/specs/OPC-10000-4/7.22.2 | T3 (23) |
| 31 | ThingsBoard IoT Gateway, Report Strategy | https://thingsboard.io/docs/iot-gateway/features/report-strategy/ | T3 (8) |
| 32 | ModbusScope 5.0.1 release notes | https://github.com/ModbusScope/ModbusScope/releases/tag/5.0.1 | T1 (S30) |
| 33 | OPC 10000-9 §5.8.18 LimitAlarmType | https://reference.opcfoundation.org/specs/OPC-10000-9/5.8.18 | T3 (28) |
| 34 | Microsoft Learn, Chart class (System.Windows.Forms.DataVisualization.Charting), .NET Framework 4.8 | https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.datavisualization.charting.chart?view=netframework-4.8 | Not in T1–T3; added by verifier |
| 35 | Chipkin, Modbus Device Discovery & Network Scanning Guide | https://docs.chipkin.com/articles/modbus-device-discovery-and-network-scanning-guide/ | T3 (31) |
| 36 | Nmap NSE, `modbus-discover` | https://nmap.org/nsedoc/scripts/modbus-discover.html | T3 (32) |
| 37 | Modbus.org, MODBUS Application Protocol Specification V1.1b3 | https://www.modbus.org/file/secure/modbusprotocolspecification.pdf | T2 (S17) |
| 38 | libmodbus reference, `modbus_report_slave_id` | https://libmodbus.org/reference/modbus_report_slave_id/ | T2 (S18) |
| 39 | Modbus.org, MODBUS over Serial Line Specification and Implementation Guide V1.02 | https://www.modbus.org/file/secure/modbusoverserial.pdf | T2 (S13) |
| 40 | CISA AA22-103A, APT Cyber Tools Targeting ICS/SCADA Devices (rev. 2022-05-25) | https://www.cisa.gov/news-events/cybersecurity-advisories/aa22-103a | T2 (S28) |
| 41 | Dragos, "How to Protect Against FrostyGoop", 2024-07-23 | https://www.dragos.com/blog/protect-against-frostygoop-ics-malware-targeting-operational-technology | T2 (S30) |
| 42 | Telegraf MQTT output plugin README | https://github.com/influxdata/telegraf/blob/master/plugins/outputs/mqtt/README.md | T3 (4) |
| 43 | EMQX Neuron docs, Northbound Applications | https://docs.emqx.com/en/emqx-neuron/latest/configuration/north-apps/catalog.html | T3 (15) |
| 44 | EMQX Neuron docs, MQTT upstream/downstream data format | https://docs.emqx.com/en/emqx-neuron/latest/configuration/north-apps/mqtt/api.html | T3 (17) |
| 45 | Eclipse Sparkplug 3.0 specification source, ch. 5 Operational Behavior | https://github.com/eclipse-sparkplug/sparkplug/blob/master/specification/src/main/asciidoc/chapters/Sparkplug_5_Operational_Behavior.adoc | T3 (18) |
| 46 | ISO/IEC 20237:2023, Information technology — Sparkplug® version 3.0 | https://www.iso.org/standard/86204.html | T3 (21) |
| 47 | IANA Service Name and Transport Protocol Port Number Registry (CSV) | https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.csv | T2 (S4) |
| 48 | pymodbus documentation, Client | https://pymodbus.readthedocs.io/en/stable/source/client.html | T2 (S5) |
| 49 | digitalpetri/modbus README | https://github.com/digitalpetri/modbus | T2 (S6) |
| 50 | node-red-contrib-modbus, Modbus-Client node help | https://github.com/BiancoRoyal/node-red-contrib-modbus/blob/master/src/modbus-client.html | T3 (6) |
| 51 | Modbus.org, Specifications page | https://www.modbus.org/modbus-specifications | T2 (S2) |
| 52 | Eclipse Foundation, The Sparkplug Specification (version list) | https://sparkplug.eclipse.org/specification | Not in T1–T3; added by verifier |

## Review notes

Verifier (citations, 17 corrections) and research reviewer (0 FATAL, 7 MAJOR, 13 MINOR) passes are in
`outputs/.drafts/openpoll-industrial-features-cited.md` and `outputs/.drafts/openpoll-industrial-features-verification.md`.
This revision addresses V1 (code snapshot dated, status column), V2 (measured size), V3 (criteria stated, order
marked inference), V4 (missing features added as ranks 9–13), V5 (write-class defined; FC08 gap fixed in code and
tested), V6 (counting caveat, repo ages), V7 (shared libraries), V8, V10, V11, V12, V15, V16, V17, V18, V19 (strict
CSV parsing noted), V20 (on/off delay listed as not done). V9 resolved by stating the actual default. V13: second
back-off source added. V14: Python gap stated in §1 and rank 16.
