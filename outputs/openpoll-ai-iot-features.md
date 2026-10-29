# OpenPoll beyond wave 1: features for now and the next 2–5 years (AI, IIoT, devices, security)

Date: 2026-10-29. Scope: which features would make OpenPoll (Windows `openpoll-win` 0.2.0 and the Python
`openpoll` 0.1) more useful today and as AI, IIoT and regulation evolve. Builds on
`outputs/openpoll-industrial-features.md` (wave 1 done in openpoll-win 0.2.0) and does not repeat it.

Citations `[n]` resolve to the Sources table at the end. Each Sources row names the evidence file it came from:
A = `outputs/.drafts/openpoll-ai-iot-features-research-ai.md`, I = `…-research-iiot.md`, D = `…-research-devices.md`,
S = `…-research-security.md`, U = `…-research-demand.md`. **[INFERENCE]** marks reasoning that no source states.

## Executive summary

- **Users ask for basics first.** In 453 issues classified from seven Modbus tool trackers, the largest themes are
  timeouts/serial/connection reliability (78), install/packaging (52), byte/word order and data types (41),
  function-code variants (34), simulator/server (30), logging/export (24) and charts (22). AI, MQTT/IIoT and mobile
  had 0 user issues each [1][2][3][4][5][6][7]. Most simulator and reliability issues come from two libraries; at
  tool level, packaging and data types lead [1]. [INFERENCE] Issue trackers undercount demand for categories a tool
  does not have yet, but these numbers mean AI and IIoT features cannot be justified by user demand today.
- **[INFERENCE] Data types and byte order rank first.** OpenPoll swaps words but always reads the bytes inside a
  register big-endian, and has no string, BCD, bit-field or enum types [8]. In the 495-map `.mbmap` library, 4.5 % of
  multi-register values (11 % of maps) use CDAB word order, probably an under-count [9][10]; an integrator reference
  calls byte order the second most common integration error [123]; forum users find float byte order by trial and
  error [11][12].
- **A built-in slave/simulator ranks high for engineering reasons more than demand.** Only 7 of the 30 simulator
  issues come from GUI/CLI tools [1]. The case is that Witte Modbus Slave has simulated CRC errors, delays and dropped
  replies since 2006 [13], OpenPoll's own tests need an external pymodbus slave pinned below 3.9 [8], and CI testing
  depends on it.
- **Device maps give leverage.** `.mbmap` v2 has a spec, a JSON Schema and a CC0 library of 495 maps from 137
  vendors, but only 6 maps are verified on a device, the library repository was created on 13 Oct 2026, and it has
  one maintainer [9][14][15].
- **AI that helps is advisory and read-only.** One small open tool (ModbusTool) ships LLM register-map extraction from
  PDFs with code-side address conversion, human review and a live read-only check [16][17]; Modbench ships a read-only
  MCP server [19][20]. Vendor engineering agents (Siemens, Beckhoff, Rockwell, Inductive Automation) work on projects
  and configuration behind review [21][22][23][24]. CISA and eight partner agencies say LLMs "almost certainly should
  not be used to make safety decisions for OT environments" [25]. Injected instructions in log fields reached 83.4 %
  average attack success [26].
- **Simple statistics often match or beat deep models for anomaly flags on benchmarks.** TSB-AD (NeurIPS 2024):
  "simpler architectures and statistical methods often yield better performance" [27]. [INFERENCE] EWMA and robust
  z-score fit in plain C#/Python [28]; ONNX Runtime is a NuGet package with native builds [29], which would break the
  no-NuGet single EXE [8].
- **IIoT integration should be thin and outbound.** No standards-body definition of the Unified Namespace was found;
  in practice it is MQTT with ISA-95 topics, retained last values and report-by-exception [30][31][32]. An OPC UA
  server would add about 5.9 MB of .NET DLLs before dependencies [35]. Sparkplug 4.0 (draft adds JSON) is
  unreleased [36][37].
- **Before a public release: signing, a security policy and a CRA role statement.** Unsigned files start with zero
  SmartScreen reputation and Smart App Control blocks them [42]. EU CRA obligations bind a monetising manufacturer now
  and a steward from 11 Dec 2027 [38][39][40][41]. The CVEs found in Modbus Poll/Slave and libmodbus (2022–2026) come
  from project files, an over-long input field and unchecked lengths [43][44][45][46][47][48][49].

## 1. Demand evidence

| Theme | User issues (453) | Where it is concentrated | OpenPoll today [8] |
|---|---|---|---|
| Timeouts / serial / connection reliability | 78 | node-red (33 of 100 sampled), pymodbus (19) | Back-off, KPIs, timeout suggestion (wave 1) |
| Install / packaging / platform | 52 | mbpoll (27), OpenModScan (13) | Single EXE; Python not on PyPI |
| Byte/word order & data types | 41 | OpenModScan (11), mbpoll (8) | Word swap only |
| Function codes / protocol variants | 34 | mbpoll (11), pymodbus (8) | FC 01–06/15/16/22/23, FC43/14, serial broadcast |
| Simulator / server | 30 | pymodbus (19) | None |
| Logging / export / traffic | 24 | ModbusScope (14) | Logging v2 (wave 1) |
| Charts | 22 | ModbusScope (22, mostly maintainer-filed) | Trend (wave 1) |
| Addressing / offset confusion | 16 | OpenModScan (9) | PLC/protocol address toggle |
| Scripting / automation / CLI output | 14 | node-red (6), mbpoll (5) | Python CLI; Windows `--selftest` only |
| Discovery / scan | 7 | OpenModScan (4), ModbusMechanic (3) | Unit-ID scan (wave 1) |
| Security / safety | 6 | OpenModScan (3), pymodbus (2) | Write protection + audit (wave 1) |
| i18n | 8 | OpenModScan (8) | English only |
| Device maps / config files | 7 | ModbusScope (7, maintainer-heavy) | Tag CSV (wave 1) |
| Multi-device 4 · Remote/web 1 · Mobile 0 · AI 0 · MQTT/IIoT 0 | | | |

Tool-level split (GUI/CLI tools vs libraries): 23 of the 30 simulator/server issues and 52 of the 78 reliability
issues come from the pymodbus and node-red libraries, mostly bug reports; at tool level, install/packaging (43 of 52)
and data types (27 of 41) carry the most demand [1] (recomputed by the reviewer).

Sources for the counts: [1][2][3][4][5][6][7]. Classification is title-only by one coder, one theme per issue;
reactions are sparse (max 10), so comment counts are used as pain signals [1]. 55 of the 79 ModbusScope feature
issues were filed by its maintainer, so the charts and logging counts are partly roadmap [1][4]. Stack Overflow's top
100 `[modbus]` questions show some interest in Android (4), browser/JS (3) and MQTT/Home Assistant (2) [50]. The
modbench owner's 3 MCP issues are a roadmap, not user demand [51].

OpenModScan requests and reports from 2025–2026 that map onto an OpenPoll-class tool: sniffer, RTU over TCP, word-
order bugs, print to PDF, scan results to CSV, pause traffic, a slave simulator, confirmation before a device reboot,
winget packaging and an OpenSSF scorecard [2]. OpenPoll already has RTU over TCP [8].

## 2. Findings by area

### 2.1 Device data: types, maps, profiles
- `.mbmap` v2 has a spec, a JSON Schema and a test corpus with expected diagnostics [14][9]. The library holds 495 CC0
  maps from 137 vendors and about 145,000 registers; 6 maps are verified on a device, 267 official and 222 unofficial
  [9][15][10]. At least 206 of the unofficial maps were converted from two other open catalogues [10]. The library
  signs `index.json` with Ed25519 and tells apps to check each map's SHA-256 [52].
- Library statistics: 95.5 % of multi-register values are ABCD and 4.5 % CDAB, with no BADC/DCBA; 45 of 417 maps
  with multi-register values (about 11 %) use CDAB somewhere; bit fields cover about 3.3k registers and enums about
  7.7k [10]. This is one computed source and probably under-counts CDAB, because 222 maps are unverified conversions
  [9][10]. An integrator reference calls byte order "the second most common source of Modbus integration errors" [123]. Home Assistant, EMQX Neuron and Modbus Poll still offer all four
  orders [53][54][55].
- SunSpec publishes 112 Apache-2.0 model files [56]. The reference client looks for `SunS` at base addresses 40000, 0
  and 50000, then walks the model chain [57]; scale factors are separate `sunssf` points [58].
- The W3C WoT Modbus binding is an Editor's Draft [59]. IDTA 02017 v1.1 (April 2026) reuses its terms [60], and OPC
  10100-1 maps Modbus assets to OPC UA through WoT Thing Descriptions [61]. [INFERENCE] A Thing Description export is a
  standards-backed hand-off to OPC UA without an OPC UA stack.

### 2.2 Simulation and capture
- Witte Modbus Slave has simulated CRC errors, delays and skipped responses since 2006 [13]. Among the open GUI tools
  surveyed, Modbench, Modbux and OpenModSim have value generators and ModbusLens lists them as upcoming
  [19][62][63][64]. [INFERENCE] Most of these document little or no fault injection (Modbench answers exception 2 for
  unmapped addresses; OpenModSim's error simulation is shown only in a screenshot). Frame-level record/replay was found
  only in modbusctl (a CLI) and Modbus Mapper Pro (paid) [65][66].
- Wireshark decodes Modbus RTU and TCP, but its register format is one global preference with no per-register maps
  [67]. [INFERENCE] pcapng export with the Wireshark upper-PDU link type (252) is a low-cost bridge [68].
- Passive RS-485 sniffing has one explicit request, which the OpenModScan maintainer declined [69]. Witte's manual
  states that Windows cannot measure the 3.5-character idle time between frames [70], and on a passive tap the sender
  of each frame must be inferred from structure and timing [66].
- Other field protocols (BACnet, DNP3, IEC 104, M-Bus, CANopen): none of the surveyed open Modbus tools documents them,
  and issue searches in four repositories found 0 requests [71]. Vendors sell them as separate products [72][73].

### 2.3 AI
- LLM register-map extraction from PDF manuals ships in ModbusTool (GPL-3.0, 0 stars), with address conversion done by
  code, a source page per row, an editable review table and a read-only device check that marks rows verified,
  mismatch or unread [16][17]. ModbusCloud's shipping version imports tables without AI and checks them against the
  device; its local-AI version is announced [18]. The only metric found that covers Modbus manuals is one preprint
  (CLARITY): field-level F1 = 0.87 on 28 queries over a five-device benchmark, with abstention [74].
- Modbus MCP servers: five were found, with 2–25 stars; four expose write tools and one binds to `0.0.0.0` without
  authentication [19][75][76][77][78]. Modbench's MCP server "never writes to a device", and its GUI-control mode is
  off by default, loopback-only with a token file, and shows a bar where the user can pause or disconnect the
  assistant [19][20]. The current MCP revision is 2026-07-28, the third revision in about 13 months after 2025-06-18
  and 2025-11-25 [79][80].
- Vendor agents: Siemens Eigen Engineering Agent is generally available since 2026-04-20 [21]; Beckhoff TwinCAT
  CoAgent requires engineers to approve every AI result [22]; Rockwell's Design Studio Copilot "cannot modify
  safety-related PLC code" [23] and FactoryTalk Conductor is expected in Q1 2027 [81]; Inductive Automation's Ignition
  Catalyst has bring-your-own-model support including local Ollama [24]. Beckhoff, Rockwell Conductor and Ignition
  Catalyst offer local or customer-chosen models [22][81][24]. Performance figures in these sources are vendor claims.
- Safety guidance agrees across OWASP LLM01/LLM06 (least privilege enforced in code, human approval, complete
  mediation) [82][83], NIST AI 600-1 [84] and the CISA-led joint OT principles (human in the loop, a distinct AI
  identity in logs) [25]. MCPTox measured up to 72.8 % attack success for poisoned tool metadata [85]. LogInject
  measured 83.4 % average success for instructions in log fields and 8.4 % residual success after layered defences
  [26]. FC43 identification objects are device-supplied ASCII strings [86]. [INFERENCE] FC43 strings and register
  text in manuals or shared maps are third-party text and a likely injection channel; no published attack was found.

### 2.4 IIoT and platforms
- UNS in practice: plain MQTT, ISA-95 topic paths, retained last values and report-by-exception [31][32]. HiveMQ says
  Sparkplug is "not ideal as the central UNS" [32].
- The Sparkplug 4.0 draft (changed 2026-10-27) defines a JSON encoding (`spJv4.0`) and an optional topic prefix at an
  arbitrary root [36][37]. Sparkplug 3.0 needs Protobuf payloads and birth/death sequence state [87].
- OPC UA PubSub over MQTT has a JSON message form with defined topics and retained status [88]. [INFERENCE] Emitting it
  needs no OPC UA stack, only a payload formatter. No demand evidence was found.
- Time series: InfluxDB 3 and QuestDB both accept line protocol over HTTP [33][34]; QuestDB's docs now file ILP under
  "compatibility" protocols, so durability for that target is lower [34]. [INFERENCE] One writer covers
  InfluxDB 1/2/3, QuestDB and Telegraf, and a Prometheus `/metrics` text endpoint [89] is the cheapest way to see link
  KPIs in Grafana.
- Platforms: Mono's last major release was in July 2019 and Microsoft recommends migrating to .NET [90], so Linux/ARM
  means the Python edition. Web Serial works in Chrome, Firefox 151 desktop and Chrome on Android, but not in Safari
  [91][92][93].
- Size: a hand-written MQTT client in C# is estimated at +10–25 KB [INFERENCE; a complete MQTT 3.1.1 client in
  MicroPython is 233 lines [94]]. MQTTnet's last .NET Framework build is 343,096 bytes and needs NuGet [95]. OPC UA
  .NET Core + Server DLLs measure about 5.9 MB, all three packages about 8.3 MB, before dependencies [35].

### 2.5 Security, regulation, supply chain
- CRA: manufacturer reporting since 11 Sep 2026; general application 11 Dec 2027; stewards report from 11 Dec 2027
  [38][39][96][97]. Charging for binaries, or giving binaries or security fixes only to payers or donors, makes the
  publisher a manufacturer [40]. EUR-Lex blocked automated access, so article text came from StreamLex and was
  cross-checked against Commission documents [39][98].
- Signing: EV certificates no longer bypass SmartScreen [42]. Azure Artifact Signing costs about $9.99/month and is
  available only to organisations in the EU, UK, US and Canada and to individuals in the US and Canada [99];
  [INFERENCE] a publisher elsewhere would need an OV certificate or SignPath Foundation. SignPath Foundation's "No hacking tools" clause
  [100] may raise questions about OpenPoll's unit-ID scan [INFERENCE].
- SBOM: CycloneDX 1.7 (ECMA-424 2nd ed., December 2025) and SPDX 3.0.1 are current [101][102].
- Modbus/TCP Security uptake is thin. Siemens S7-1500 (firmware V2.5 or later) documents secure Modbus TCP, but
  conformance to the Modbus.org spec was not confirmed [103]. CODESYS Modbus 4.6 adds no TLS [104], and
  node-red-contrib-modbus defers its TLS client to v6 [105].
- CVEs found for 2022–2026: Modbus Poll and Modbus Slave overflows come from crafted project files and an over-long registration field
  [43][44][45]; libmodbus overflows come from unchecked lengths [46][47][48][49]. CVE-2026-51539 (NVD status
  "Deferred") is a Windows receive-loop DoS in libmodbus 3.1.12 [106]; the bug report shows one 12-byte request
  blocking the server for about 16.6 s with a 2 s timeout [107]. [INFERENCE, from reading OpenPoll's source; not
  tested] OpenPoll already checks byte counts and MBAP lengths and uses one absolute deadline per receive. Its gaps
  are no size cap on document load, no fuzz tests and no deterministic build flags [8][108].
- Remote access guidance (CISA 2025 and 2026, NCSC-UK 2026): remove OT from the internet, use outbound-only or
  brokered connections, and use VPN with phishing-resistant MFA [109][110][111][112]. [INFERENCE] Any OpenPoll
  listener (API, MCP, simulator) should bind to 127.0.0.1 by default.

## 3. Roadmap

**Ranking rule** (applies the plan's fixed criteria; the order is an [INFERENCE]):
1. *Gate:* an item enters **Now** only if it has tool-level demand (criterion 1) **or** it prevents harm or is a
   precondition for a safe public release (criterion 3). It enters **Next** if it has some demand, or if it is
   standards-backed and Very low/Low cost (criteria 5–6). Items with no demand and Medium or higher cost go to
   **Later** with a trigger.
2. *Within a tier:* order by demand, then value for unattended use (criterion 2), then fit (criterion 4).

Marks: D = tool-level demand (H ≥ 20 issues, M 5–19, L 1–4, 0 none), S = safety relevance, F = fit with the single
small EXE and the Python edition, C = cost (L/M/H). All marks trace to the citations in the row.

### Now (next release, 2026 Q4 – 2027 Q1)

| # | Feature | D | S | F | C | Reason (evidence) |
|---|---|---|---|---|---|---|
| N1 | Data types × 4 byte orders per tag (u/i16/32/64, f32/64, string, bit field, BCD, Unix time) and an "interpret as" panel | H (27 tool-level) | – | H | L | 41 issues [1]; CDAB under-counted at 4.5 % / 11 % of maps [10]; "second most common" error source [123]; forum trial and error [11][12] |
| N2 | Python edition parity for wave 1 + packaging (pipx, Docker, Pi; winget for the EXE) | H (43 tool-level) | – | H | M | Install/packaging 52 issues [1]; Mono is not a path [90]; winget request [2] |
| N3 | File-input hardening: size cap, schema checks, fuzz tests for `.opoll`/`.opollw`/tag CSV; hostile-slave tests | L | H | H | L | Project-file and length CVEs [43]–[49]; OpenPoll gaps [8][108] |
| N4 | Release prerequisites: public repository and CI, SECURITY.md, CRA role statement, SBOM (CycloneDX 1.7 / SPDX 3.0.1), signed checksums, Authenticode signing | L | H | H | M ($0–300/yr) | SmartScreen and Smart App Control [42]; signing options depend on the publisher's country [99][100]; CRA steward duties from 11 Dec 2027 at the latest [38][39]; OpenSSF Scorecard checks [113]; scorecard request [2]. No repo or CI exists today [108] |
| N5 | Built-in slave/simulator (TCP + RTU) with generators and fault injection (delay, no reply, exceptions, bad CRC/LRC), loopback by default | M (7 tool-level) | M | H | M | Reference product since 2006 [13]; replaces OpenPoll's external test slave [8]; enables CI (X2); loopback per [109][110] |
| N6 | Headless run of a `.opoll` with JSON lines and exit codes (Python first, then `OpenPoll.exe --headless`) | M (14) | – | H | L | Scripting/CLI 14 issues [1]; [INFERENCE] prerequisite for CI, Docker and Pi |
| N7 | Carried over from wave 2: inter-request pacing per slave and skip-after-K-failures; register/address scan (read-only) | M | M | H | L | Poll-pacing request [U §5]; discovery/scan 7 issues incl. OpenModScan #4 (13 comments) [1][2] |

[INFERENCE] Seven items with three Medium costs are more than one release; N1, N3 and N6 fit one release, N2, N4, N5
and N7 a second. Rule-based link diagnosis moved to Next: reliability issues are mostly tool bugs [1], and diagnosis
addresses only the operator side of that pain.

### Next (2027)

| # | Feature | D | S | F | C | Reason (evidence) |
|---|---|---|---|---|---|---|
| X1 | `.mbmap` v2 import (types, orders, scaling, enums, bit fields, write limits); later a library browser | L (7, maintainer-heavy) | M (write limits) | M | M | 495 CC0 maps [9][15]; spec write limits [14]; needs N1. Verifying the signed index needs Ed25519, which the .NET BCL lacks (dotnet/runtime #63174 is still a proposal [124]), so the EXE would need a hand-written verifier or rely on SHA-256 pins only |
| X2 | Test/assert mode for CI (expected ranges, JUnit/JSON report) with the built-in slave | M (14 shared with N6) | – | H | L–M | ModbusLens `ASSERT` DSL [64]; free hosted runners for public repos [114]; socat PTY [115]; needs N4, N5, N6 |
| X3 | Rule-based link diagnosis ("explain this fault"), optional FC08 counters | [INFERENCE] indirect (reliability 78, mostly tool bugs) | – | H | L | Ships in Modbench and ModbusCloud [19][18]; FC08 counters [86] |
| X4 | Per-value quality code in grid, chart and log (carried over from wave 2; stale marking and chart gaps exist) | 0 | M | H | L | ModbusScope per-point quality [previous brief]; completes wave-1 work |
| X5 | Prometheus `/metrics` on 127.0.0.1 (KPIs, alarms, poll age) | 0 | – | H | Very low | Standard text format [89]; Neuron exposes the same [116] |
| X6 | pcapng export of the traffic log | L | – | H | L | Wireshark lacks per-register maps [67]; sniffer interest [69][U §5] |
| X7 | Tag-map export: WoT Thing Description, Home Assistant YAML, Neuron/Telegraf | 0 | – | H | L (after N1) | OPC 10100-1 via TD [61]; HA modbus integration on about 18,700 installations [117] |
| X8 | SunSpec auto-detect | 0 | – | H | L–M | 112 Apache-2.0 models and a reference algorithm [56][57]; standards-backed |
| X9 | Statistical anomaly flags (EWMA, robust z-score, flatline, rate of change), advisory only | 0 | M | H | L–M | TSB-AD [27]; EWMA [28]; [INFERENCE] reuses alarm deadband logic |

### Later (2028–2031, each with a trigger)

| # | Feature | Trigger | Reason (evidence) |
|---|---|---|---|
| L1 | Read-only MCP server (stdio), read-only enforced in code, device text delimited and marked untrusted, each call audited with an AI identity | User requests after a public release, and an MCP spec without breaking revisions | 0 user demand; only owner-filed roadmap issues [51]; three revisions in ~13 months [79][80]; design per Modbench [19][20], OWASP [82][83], CISA [25] |
| L2 | MQTT publish-only for a UNS (ISA-95 topic template, retained JSON per tag, status + Last Will, report-by-exception) | User requests; X4 done | 0 user issues [1]; UNS practice [31][32]; hand-written client est. +10–25 KB [INFERENCE] |
| L3 | Line-protocol push (InfluxDB/Telegraf; QuestDB via its compatibility path) with spool on failure | User requests | [33][34] |
| L4 | LLM-assisted register-map import (local/bring-your-own model, schema-validated, address math in code, review table, read-back check, no write permission) | After X1; local model option | Shipping pattern [16][17]; F1 0.87 on a five-device benchmark only [74]; injection risk [82][26] |
| L5 | Assistant-driven GUI session (loopback + token, visible activity, Pause/Disconnect, read FCs only) | After L1 proves useful | Modbench design [19][20] |
| L6 | Frame record/replay as a slave | After N5 | Only modbusctl and one paid tool [65][66] |
| L7 | Passive RS-485 sniffer (receive-only) | Demand confirmed; second adapter | One explicit request [69]; Stack Overflow "Modbus sniffer tool required" has 9,667 views [50]; Windows timing limit [70] |
| L8 | Modbus/TCP Security client (port 802, mutual TLS) | Devices appear | Thin adoption [103][104][105][118] |
| L9 | Sparkplug v4 JSON / OPC UA PubSub JSON on the MQTT publisher | v4 release candidate; L2 done | [36][37][88] |
| L10 | Local read-only REST/WebSocket API | User requests | Gateways have it [116][119][120]; loopback + token, never writes [110] |
| L11 | Web Serial browser edition / Android OTG app | Demand confirmed | Some Stack Overflow interest [50]; Web Serial on Chrome Android [93]; third codebase |

### Not recommended
- AI-proposed or AI-executed writes to field devices: excessive-agency and injection risk [83][26], PLCBench shows LLM
  agents sustaining physical objectives on real PLCs in 31.3 % of 240 episodes [121], and CISA advises against AI
  safety decisions in OT [25].
- Bundling ONNX Runtime or a model in the EXE [29][8]; default cloud AI [25]; HTTP MCP on non-loopback addresses [78].
- OPC UA server in the EXE (about 5.9 MB of DLLs plus dependencies) [35]; direct TimescaleDB driver, because inserts
  need a PostgreSQL client and CSV `\copy` already works [122].
- Other field protocols (BACnet, DNP3, IEC 104, M-Bus, CANopen), on fit and cost: 0 requests in four repos [71] and
  vendors sell separate products [72][73]. Counter-signals exist: the ModbusScope maintainer is exploring other
  protocols, and GitHub topics pair `modbus` with `bacnet` (58 repos) and `dnp3` (43) [D §4] (developer tagging, not
  user demand).

## 4. Caveats

- Demand counts are title-only, one coder, seven repos; the node-red sample is 84 % from 2017–2018; pymodbus and
  ModbusScope are label-filtered; bugs count as pain signals; QModMaster and GitHub Discussions were not covered;
  forum quotes are search snippets because PLCtalk returned 403 and Reddit was blocked [1][11][12]. They undercount
  demand for features tools do not yet have.
- The newest tools have few stars (Modbench 12, mbmap-library 13, ModbusLens 76); their own docs are the main evidence
  for maps, MCP and simulators [19][9][64].
- AI accuracy figures are small-sample (five devices) or vendor claims [74][21][22].
- The CRA analysis is not legal advice; EUR-Lex text was read via a secondary rendering [39][98]. The monetisation
  test rests on Commission guidance paragraphs the verifier did not re-read [40].
- [INFERENCE from a workspace path] OpenPoll may be published by a company (PT AGS, apparently Indonesian). Rules for
  publishers outside the EU (importer, authorised representative) were not researched; signing options depend on the
  publisher's country [99].
- IEC 62443 normative text is paywalled; only requirement titles were checked.
- Size and effort figures are estimates unless a byte count is quoted.

## 5. Open questions

- Who publishes OpenPoll (person or company, which country), and will anything be monetised? This decides the CRA role
  and the signing options [40][99].
- Is demand for AI/MQTT real among OpenPoll's own users? A first public release with an issue tracker would answer it.
- Will Sparkplug 4.0 ship with JSON as drafted [36]?
- Does `.mbmap` gain a second maintainer and more verified maps [9]?

## Sources

| # | Title | URL or artifact | Evidence file |
|---|---|---|---|
| 1 | Demand classification of 453 issues (counts per theme per repo, sampling limits) | `outputs/.drafts/openpoll-ai-iot-features-research-demand.md` §1, §3, §6 | U |
| 2 | sanny32/OpenModScan issues (76/76) | https://api.github.com/search/issues?q=repo:sanny32/OpenModScan+is:issue&per_page=100&sort=reactions | U |
| 3 | epsilonrt/mbpoll issues (74/74) | https://api.github.com/search/issues?q=repo:epsilonrt/mbpoll+is:issue&per_page=100&sort=reactions | U |
| 4 | ModbusScope `feature` issues (79) | https://api.github.com/search/issues?q=repo:ModbusScope/ModbusScope+is:issue+label:feature&per_page=100&sort=created | U |
| 5 | BiancoRoyal/node-red-contrib-modbus issues (100 of 420) | https://api.github.com/search/issues?q=repo:BiancoRoyal/node-red-contrib-modbus+is:issue&per_page=100&sort=reactions | U |
| 6 | pymodbus `Enhancements` issues (99) | https://api.github.com/search/issues?q=repo:pymodbus-dev/pymodbus+is:issue+label:Enhancements&per_page=100&sort=reactions | U |
| 7 | SciFiDryer/ModbusMechanic issues (24) | https://api.github.com/search/issues?q=repo:SciFiDryer/ModbusMechanic+is:issue&per_page=100&sort=reactions | U |
| 8 | OpenPoll repository (local, read only) | `openpoll-win/README.md`; `openpoll-win/CHANGELOG.md`; `openpoll-win/build.sh`; `openpoll-win/src/Core/{Formats,Protocol,Framing,Transport,Discovery,Support,Engine}.cs`; `openpoll/src/openpoll/{formats,app}.py`; `openpoll/pyproject.toml`; `openpoll-win/tools/demo_slave.py`; `AGENTS.md` | D, I, S, A |
| 9 | nobrega8/mbmap-library README | https://github.com/nobrega8/mbmap-library | D |
| 10 | Researcher's analysis of a shallow clone of mbmap-library (type, order and feature counts), 2026-10-29 | `outputs/.drafts/openpoll-ai-iot-features-research-devices.md` F1.1, F3.1 (source S4) | D |
| 11 | PLCtalk, "IEEE754 conversion - 32 bit - Modbus TCP" (search snippet) | https://www.plctalk.net/forums/threads/ieee754-conversion-32-bit-modbus-tcp.138524 | U |
| 12 | PLCtalk, "Modbus data not accurate" (search snippet) | https://www.plctalk.net/forums/threads/modbus-data-not-accurate.103628 | U |
| 13 | Witte Software, Modbus Slave changelog | https://www.modbustools.com/ModSlaveChangeLog.txt | D |
| 14 | `.mbmap` v2 specification | https://raw.githubusercontent.com/nobrega8/mbmap-library/main/SPEC.md | D |
| 15 | mbmap `index.json` and GitHub repository API | https://raw.githubusercontent.com/nobrega8/mbmap-library/main/index.json ; https://api.github.com/repos/nobrega8/mbmap-library | D |
| 16 | black-a85/ModbusTool README | https://github.com/black-a85/ModbusTool | A |
| 17 | ModbusTool `ai/validation.py` | https://raw.githubusercontent.com/black-a85/ModbusTool/main/modbus_tool/ai/validation.py | A |
| 18 | ModbusCloud Diagnostics product page (2.1.3; 2.2 announced) | https://www.modbuscloud.com/diagnostics | A |
| 19 | nobrega8/modbench README | https://github.com/nobrega8/modbench | A, I, D |
| 20 | Modbench manual, ch. 12 "Command line and AI assistants" | https://raw.githubusercontent.com/nobrega8/modbench/main/manual/chapters/12-command-line.tex | A |
| 21 | Siemens press, "Siemens launches the Eigen Engineering Agent", 2026-04-20 | https://assets.new.siemens.com/siemens/assets/api/uuid:7c20b450-d7b7-4e8b-9a02-cb4bde2c520c/HQCOPR202604187387EN.pdf | A |
| 22 | Beckhoff press, TwinCAT CoAgent, 2026-06-23 | https://www.beckhoff.com/en-us/company/press/open-model-independent-ai-assistance-for-automation-programming-and-controls-engineering-2026-06.html | A |
| 23 | Rockwell, FactoryTalk Design Studio Copilot (online help) | https://www.rockwellautomation.com/en-in/docs/factorytalk-design-studio/current/contents-ditamap/getting-started/factorytalk-design-studio-copilot.html | A |
| 24 | Inductive Automation, "ICC 2026 Recap", 2026-09-30 | https://inductiveautomation.com/blog/icc-2026-recap-three-days-of-innovation-unleashed | A |
| 25 | CISA et al., "Principles for the Secure Integration of Artificial Intelligence in Operational Technology", 2025-12-03 | https://www.cisa.gov/sites/default/files/2026-01/joint-guidance-principles-for-the-secure-integration-of-artificial-intelligence-in-operational-technology-508cV2.pdf | A |
| 26 | Karanjai et al., "Context Contamination in LLM Analysis of Network Security Logs" (LogInject), USENIX Security 2026 | https://www.usenix.org/system/files/usenixsecurity26-karanjai.pdf | A |
| 27 | Liu, Paparrizos, "The Elephant in the Room: Towards A Reliable Time-Series Anomaly Detection Benchmark" (TSB-AD), NeurIPS 2024, DOI 10.52202/079017-3437 | https://proceedings.neurips.cc/paper_files/paper/2024/hash/c3f3c690b7a99fba16d0efd35cb83b2c-Abstract-Datasets_and_Benchmarks_Track.html | A |
| 28 | NIST/SEMATECH e-Handbook, §6.3.2.4 EWMA Control Charts | https://www.itl.nist.gov/div898/handbook/pmc/section3/pmc324.htm | A |
| 29 | ONNX Runtime C# getting started; NuGet Microsoft.ML.OnnxRuntime | https://onnxruntime.ai/docs/get-started/with-csharp.html ; https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime | A |
| 30 | Inductive Automation, "UNS: Unified Namespace" | https://inductiveautomation.com/resources/article/uns-unified-namespace | I |
| 31 | HiveMQ, "Implementing UNS With MQTT Sparkplug", 2026-02-22 | https://www.hivemq.com/blog/implementing-unified-namespace-uns-mqtt-sparkplug | I |
| 32 | HiveMQ, "Beyond MQTT: Fit and Limitations of Other Technologies in a UNS", 2026-08-06 | https://www.hivemq.com/blog/beyond-mqtt-fit-and-limitations-other-technologies-in-uns/ | I |
| 33 | InfluxDB 3 Core, line protocol reference | https://docs.influxdata.com/influxdb3/core/reference/line-protocol/ | I |
| 34 | QuestDB, ILP overview | https://questdb.com/docs/connect/compatibility/ilp/overview/ | I |
| 35 | NuGet OPCFoundation.NetStandard.Opc.Ua.{Core,Client,Server} 2.0.1 (net48 DLL sizes measured) | https://api.nuget.org/v3-flatcontainer/opcfoundation.netstandard.opc.ua.core/2.0.1/opcfoundation.netstandard.opc.ua.core.2.0.1.nupkg | I |
| 36 | Sparkplug 4.0 draft, ch. 4 Topics (`develop`) | https://raw.githubusercontent.com/eclipse-sparkplug/sparkplug/develop/specification/src/main/asciidoc/chapters/Sparkplug_4_Topics.adoc | I |
| 37 | Sparkplug `develop` history of the Topics chapter ("[#231] Define the spJv4.0 JSON payload encoding", 2026-10-27) | https://github.com/eclipse-sparkplug/sparkplug/commits/develop/specification/src/main/asciidoc/chapters/Sparkplug_4_Topics.adoc | I |
| 38 | European Commission, "Cyber Resilience Act – Reporting obligations" (updated 11 Sep 2026) | https://digital-strategy.ec.europa.eu/en/policies/cra-reporting | S |
| 39 | CRA Art. 71 and Art. 64 via StreamLex | https://streamlex.eu/articles/cra-en-art-71/ ; https://streamlex.eu/articles/cra-en-art-64/ | S |
| 40 | Commission guidance C(2026) 5252 Annex, 27 Jul 2026 | https://ec.europa.eu/newsroom/dae/redirection/document/131456 | S |
| 41 | European Commission, "Cyber Resilience Act – Open source" (updated 15 Oct 2026) | https://digital-strategy.ec.europa.eu/en/policies/cra-open-source | S |
| 42 | Microsoft Learn, "SmartScreen reputation for Windows app developers" (updated 2026-08-17) | https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation | S |
| 43 | CVE-2022-1068 (Modbus Slave ≤ 7.4.2 registration field) | https://www.cve.org/CVERecord?id=CVE-2022-1068 | S |
| 44 | NVD CVE-2022-4856 (Modbus Slave ≤ 7.5.1 `.mbs` file) | https://nvd.nist.gov/vuln/detail/CVE-2022-4856 | S |
| 45 | NVD CVE-2022-4857 (Modbus Poll ≤ 9.10.0 `.mbp` file) | https://nvd.nist.gov/vuln/detail/CVE-2022-4857 | S |
| 46 | CVE-2022-0367 (libmodbus `modbus_reply`) | https://www.cve.org/CVERecord?id=CVE-2022-0367 | S |
| 47 | NVD CVE-2023-26793 (libmodbus `read_io_status`) | https://nvd.nist.gov/vuln/detail/CVE-2023-26793 | S |
| 48 | NVD CVE-2024-34244 (libmodbus `modbus_write_bits`) | https://nvd.nist.gov/vuln/detail/CVE-2024-34244 | S |
| 49 | NVD CVE-2024-10918 (libmodbus unexpected request length) | https://nvd.nist.gov/vuln/detail/CVE-2024-10918 | S |
| 50 | Stack Overflow `[modbus]`, top 100 by score | https://api.stackexchange.com/2.3/questions?order=desc&sort=votes&tagged=modbus&site=stackoverflow&pagesize=100 | U |
| 51 | nobrega8/modbench issues (57, owner-filed) | https://api.github.com/search/issues?q=repo:nobrega8/modbench+is:issue&per_page=100&sort=reactions | U |
| 52 | mbmap CONTRIBUTING.md (Ed25519-signed index, per-map sha256) | https://raw.githubusercontent.com/nobrega8/mbmap-library/main/CONTRIBUTING.md | D |
| 53 | Home Assistant Modbus integration docs | https://www.home-assistant.io/integrations/modbus/ | D |
| 54 | EMQX Neuron Modbus TCP docs | https://docs.emqx.com/en/neuronex/latest/configuration/south-devices/modbus-tcp/modbus-tcp.html | D |
| 55 | Witte Software, Modbus Poll user manual | https://www.modbustools.com/mbpoll-user-manual.html | D |
| 56 | sunspec/models repository | https://github.com/sunspec/models | D |
| 57 | pySunSpec2 `modbus/client.py` | https://raw.githubusercontent.com/sunspec/pysunspec2/master/sunspec2/modbus/client.py | D |
| 58 | SunSpec model 103 JSON | https://raw.githubusercontent.com/sunspec/models/master/json/model_103.json | D |
| 59 | W3C WoT Modbus Binding (Editor's Draft) | https://w3c.github.io/wot-binding-templates/bindings/protocols/modbus/index.html | D, I |
| 60 | IDTA 02017 Asset Interfaces Description v1.1 | https://industrialdigitaltwin.org/en/wp-content/uploads/sites/2/2026/04/IDTA-02017-1-1_Submodel_Asset-Interfaces-Description.pdf | D |
| 61 | OPC 10100-1 WoT Connectivity Part 1, 1.02 | https://reference.opcfoundation.org/specs/OPC-10100-1/1 | I |
| 62 | ploxc/modbux README | https://github.com/ploxc/modbux | D |
| 63 | sanny32/OpenModSim README | https://github.com/sanny32/OpenModSim | D |
| 64 | CraftParking/ModbusLens README | https://github.com/CraftParking/ModbusLens | D, I |
| 65 | otfabric/modbusctl README | https://raw.githubusercontent.com/otfabric/modbusctl/main/README.md | D |
| 66 | Modbus Mapper Pro user manual | https://docs.quantumbitsolutions.com/products/mapper/user-manual/ | D |
| 67 | Wireshark `packet-mbtcp.c` | https://gitlab.com/wireshark/wireshark/-/raw/master/epan/dissectors/packet-mbtcp.c | D |
| 68 | tcpdump.org link-layer header types | https://www.tcpdump.org/linktypes.html | D |
| 69 | OpenModScan issue #70 "Sniffer functionality" | https://github.com/sanny32/OpenModScan/issues/70 | D, U |
| 70 | Witte Software, Modbus Slave user manual | https://www.modbustools.com/mbslave-user-manual.html | D |
| 71 | GitHub issue search API for BACnet/DNP3/OPC in four Modbus tool repos, 2026-10-29 | https://api.github.com/search/issues?q=bacnet+repo:ploxc/modbux (same pattern per repo and term) | D |
| 72 | Voltrus home page (separate protocol explorers) | https://voltrus.id/ | D |
| 73 | ModbusManager scanner page | https://modbusmanager.com/modbus-scanner.html | D |
| 74 | Ganie, Ezzini, Marazi, "RAG-Based Auto-Configuration for Industrial Fieldbus Devices" (CLARITY), arXiv 2608.08618v1, 2026 (preprint) | https://arxiv.org/abs/2608.08618v1 | A |
| 75 | ezhuk/modbus-mcp README | https://github.com/ezhuk/modbus-mcp | A |
| 76 | alejoseb/ModbusMCP README | https://github.com/alejoseb/ModbusMCP | A |
| 77 | kukapay/modbus-mcp README | https://github.com/kukapay/modbus-mcp | A |
| 78 | KerberosClaw/kc_modbus_mcp README | https://github.com/KerberosClaw/kc_modbus_mcp | A |
| 79 | MCP specification 2026-07-28, Tools; Versioning | https://modelcontextprotocol.io/specification/2026-07-28/server/tools ; https://modelcontextprotocol.io/specification/versioning | A |
| 80 | Inductive Automation forum, "MCP Module Early Access" (MCP 2025-06-18), 2026-02-13 | https://forum.inductiveautomation.com/t/mcp-module-early-access/113966 | A |
| 81 | Rockwell press, "Rockwell Automation Introduces FactoryTalk Conductor", 2026-10-21 | https://www.rockwellautomation.com/en-us/company/news/press-releases/Rockwell-Automation-Introduces-FactoryTalk-Conductor-an-AI-Native-Orchestration-Platform-for-Industrial-Automation.html | A |
| 82 | OWASP, LLM01:2025 Prompt Injection | https://genai.owasp.org/llmrisk/llm01-prompt-injection/ | A |
| 83 | OWASP, LLM06:2025 Excessive Agency | https://genai.owasp.org/llmrisk/llm062025-excessive-agency/ | A |
| 84 | NIST AI 600-1, Generative AI Profile, 2024-07-26 | https://nvlpubs.nist.gov/nistpubs/ai/NIST.AI.600-1.pdf | A |
| 85 | Wang et al., "MCPTox: A Benchmark for Tool Poisoning Attack on Real-World MCP Servers", arXiv 2508.14925 (2025); AAAI 2026, DOI 10.1609/aaai.v40i42.40895 | https://arxiv.org/abs/2508.14925 ; https://doi.org/10.1609/aaai.v40i42.40895 | A |
| 86 | Modbus Organization, MODBUS Application Protocol Specification V1.1b3 | https://www.modbus.org/file/secure/modbusprotocolspecification.pdf | A |
| 87 | Eclipse Sparkplug 3.0, ch. 4 Topics (master) | https://raw.githubusercontent.com/eclipse-sparkplug/sparkplug/master/specification/src/main/asciidoc/chapters/Sparkplug_4_Topics.adoc | I |
| 88 | OPC 10000-14 Part 14 PubSub 1.05.06 | https://reference.opcfoundation.org/specs/OPC-10000-14/ | I |
| 89 | Prometheus, Exposition formats | https://prometheus.io/docs/instrumenting/exposition_formats/ | I |
| 90 | Mono download page (stable) | https://www.mono-project.com/download/stable | I |
| 91 | MDN Web Serial API and browser-compat-data | https://developer.mozilla.org/en-US/docs/Web/API/Web_Serial_API ; https://github.com/mdn/browser-compat-data/blob/main/api/Serial.json | I |
| 92 | Mozilla Hacks, "Announcing Web Serial Support in Firefox", 2026-05-21 | https://hacks.mozilla.org/2026/05/web-serial-support-in-firefox | I |
| 93 | Chrome 148 release notes | https://developer.chrome.com/release-notes/148 | I |
| 94 | micropython-lib `umqtt.simple` | https://github.com/micropython/micropython-lib/blob/master/micropython/umqtt.simple/umqtt/simple.py | I |
| 95 | dotnet/MQTTnet README and NuGet 4.3.7/5.2.0 | https://github.com/dotnet/MQTTnet | I |
| 96 | Commission FAQs on the CRA, v1.4 (04/09/2026), FAQ 5.5 | https://ec.europa.eu/newsroom/dae/redirection/document/122331 | S |
| 97 | ENISA, "The CRA Single Reporting Platform is launched", 11 Sep 2026 | https://www.enisa.europa.eu/news/the-cra-single-reporting-platform-is-launched | S |
| 98 | CRA Art. 24 and Annexes I and III via StreamLex | https://streamlex.eu/articles/cra-en-art-24/ ; https://streamlex.eu/annexes/cra-en-annex-i/ ; https://streamlex.eu/annexes/cra-en-annex-iii/ | S |
| 99 | Microsoft Learn, "Code signing options for Windows app developers" (updated 2026-08-29) | https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options | S |
| 100 | SignPath Foundation terms | https://signpath.org/terms | S |
| 101 | Ecma International, ECMA-424 | https://ecma-international.org/publications-and-standards/standards/ecma-424/ | S |
| 102 | ENISA Technical Advisory for SBOM v1.10, Oct 2026 | https://www.enisa.europa.eu/sites/default/files/2026-10/ENISA%20Technical%20Advisory%20-%20Software%20Bill%20of%20Materials%20-%20v1.10.pdf | S |
| 103 | Siemens, S7-1500 Communication Function Manual 11/2025 | https://support.industry.siemens.com/cs/attachments/59192925/s71500_communication_function_manual_en-US_en-US.pdf | S |
| 104 | CODESYS Modbus 4.6.0.0 release notes (12 May 2026) | https://api-www.codesys.com/fileadmin/user_upload/CODESYS_Group/Ecosystem/Up-to-Date/Releases-Lifecycle/Release-Updates/Release-Notes/Release-Notes-CODESYS-Modbus-4600.html | S |
| 105 | BiancoRoyal/node-red-contrib-modbus README and security page | https://github.com/BiancoRoyal/node-red-contrib-modbus ; https://github.com/BiancoRoyal/node-red-contrib-modbus/security | S |
| 106 | NVD CVE-2026-51539 | https://nvd.nist.gov/vuln/detail/CVE-2026-51539 | S |
| 107 | libmodbus issue #843, "Slowloris-Style DoS in Modbus TCP Receive Path on Windows", 2026-04-13 | https://github.com/stephane/libmodbus/issues/843 | S |
| 108 | OpenPoll source comparison (code reading, untested) | `outputs/.drafts/openpoll-ai-iot-features-research-security.md` §3.3, §5.3 (source L1) | S |
| 109 | CISA et al., "Primary Mitigations to Reduce Cyber Threats to Operational Technology", 6 May 2025 | https://www.cisa.gov/resources-tools/resources/primary-mitigations-reduce-cyber-threats-operational-technology | S |
| 110 | NCSC-UK, Secure connectivity principles for OT, Principle 2, 14 Jan 2026 | https://www.ncsc.gov.uk/collection/operational-technology/secure-connectivity/principle-2 | S |
| 111 | CISA et al., "Adapting Zero Trust Principles to Operational Technology", 29 Apr 2026 | https://www.cisa.gov/sites/default/files/2026-04/joint-guide-adapting-zero-trust-principles-to-operational-technology_508c.pdf | S |
| 112 | CISA alert on water-sector PLC targeting, 30 Jul 2026 | https://www.cisa.gov/news-events/alerts/2026/07/30/cisa-urges-water-and-wastewater-systems-sector-protect-ot-against-activity-targeting-plcs | S |
| 113 | OpenSSF Scorecard checks | https://github.com/ossf/scorecard/blob/main/docs/checks.md | S |
| 114 | GitHub-hosted runners reference | https://docs.github.com/en/actions/reference/runners/github-hosted-runners | I |
| 115 | socat(1) man page | https://man7.org/linux/man-pages/man1/socat.1.html | I |
| 116 | EMQX NeuronEX OpenAPI 3.6.0 | https://docs.emqx.com/en/emqx-neuron/latest/api/api-docs.html | I |
| 117 | Home Assistant analytics data (modbus 18,692 on 2026-10-29; 18,655 on 2026-10-28) | https://analytics.home-assistant.io/data.json | D |
| 118 | Trout Software, "Secure Modbus vs Modbus TCP", 17 Sep 2026 | https://www.trout.software/blog/the-difference-between-secure-modbus-and-modbus-tcp | S |
| 119 | ThingsBoard IoT Gateway, REST connector | https://thingsboard.io/docs/iot-gateway/config/rest/ | I |
| 120 | Ignition 8.3, API documentation | https://docs.inductiveautomation.com/docs/8.3/platform/gateway/openapi.md | I |
| 121 | Zhou et al., "PLCBench: Can Autonomous LLM Agents Turn PLC Access into Sustained Physical Impact?", arXiv 2608.26882v1, 2026 (preprint) | https://arxiv.org/abs/2608.26882v1 | A |
| 122 | Tiger Data (TimescaleDB), Insert data | https://www.tigerdata.com/docs/build/data-management/write-data/insert | I |
| 123 | Chipkin, Modbus data types and byte order reference (2026-03-12) | https://docs.chipkin.com/articles/modbus-data-types-byte-order-reference/ | D |
| 124 | dotnet/runtime issue #63174, "[API Proposal]: Add support for Ed25519" | https://github.com/dotnet/runtime/issues/63174 | Lead (web search 2026-10-29) |
