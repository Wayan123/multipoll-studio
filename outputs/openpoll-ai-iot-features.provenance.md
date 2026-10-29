# Provenance: OpenPoll beyond wave 1 — AI, IIoT, devices, security

- **Date:** 2026-10-29
- **Rounds:** 1 research round (5 parallel researchers; 2 failed on API rate limits and were recovered: IIoTIntegration wrote its file from gathered evidence on request, DemandEvidence was re-run with a reduced budget as DemandEvidence2), 1 citation verification, 1 research review, 1 lead revision
- **Sources consulted:** A 51, I 60, D 59 (+ local), S 53 (+ local), U 15 source rows covering 453 classified issues + Stack Overflow top 100; plus 2 added by the lead (Chipkin byte-order reference, dotnet/runtime #63174)
- **Sources accepted:** 124 numbered sources in the final brief (Sources table); every citation resolves, every source is cited
- **Sources rejected or degraded:** EUR-Lex CRA pages (WAF block; text taken from StreamLex and cross-checked with Commission guidance/FAQ); SunSpec spec PDF (403); MDPI SWaT paper (403); two 2026 Swedish theses (timeout); PLCtalk and Reddit (403/blocked; snippets only); OWASP Agentic Top 10 PDF not opened; ModbusCloud 2.2 shipping status unconfirmed; Digital Omnibus briefing (403)
- **Verification:** PASS WITH NOTES — verifier corrected 17+ sentences (OPC UA size, CLARITY scope, vendor model claims, CRA dates, CVE sources); reviewer found 0 FATAL / 6 MAJOR / 14 MINOR. All MAJOR addressed in the revision: explicit ranking rule with D/S/F/C marks (V1); tool-level vs library demand split, simulator justified on engineering grounds (V2); zero-demand MCP and MQTT moved to Later with triggers (V3); wave-2 carry-overs restored as N7/X4 (V4); N4 cost Medium, CRA basis corrected, deterministic build dropped from Now (V5); publisher identity marked as inference, non-EU rules and signing-region limits disclosed (V6). MINOR addressed: V7–V10, V11 (counter-signal added), V12, V13 (Ed25519 resolved via dotnet/runtime #63174), V14, V15, V16, V17, V18, V20 (process notes moved here). Not addressed: V19 (CRA guidance paragraphs not re-read). No second review pass after revision.
- **Plan:** outputs/.plans/openpoll-ai-iot-features.md
- **Research files:** outputs/.drafts/openpoll-ai-iot-features-research-ai.md, …-research-iiot.md, …-research-devices.md, …-research-security.md, …-research-demand.md; draft …-draft.md; cited …-cited.md; review …-verification.md; revised …-revised.md

## Verifier notes and removed claims (moved from the cited draft)

## Removed or weakened unsupported claims

- "ModbusCloud" as a shipping AI register-map tool: its AI version is announced only; the shipping version is non-AI.
- "Always" (every shipping extraction tool uses deterministic address math, review and read-back): only one shipping
  LLM tool was found.
- "Modbus-specific" for the CLARITY F1 = 0.87 result: the preprint covers Modbus RTU, OPC UA, Profibus DP and CANopen.
- "All 2026 vendor products support local or bring-your-own models": the evidence supports this for Beckhoff, Rockwell
  Conductor and Ignition Catalyst only.
- "ModbusLens" as shipping rule-based link diagnosis: no source in the evidence files supports it.
- "≥ 8 MB" for an OPC UA server: the measured Core + Server DLLs are 5.9 MB; 8.3 MB includes the Client package.
- "No standards body defines the UNS": the evidence shows only that no definition was found.

## Verifier notes

Changes made to the draft (draft wording → cited wording):

1. Citation format: all `[X §n]` markers replaced by numbered `[n]` references that resolve to primary URLs or local
   artifacts in the Sources table. The draft header said `[A:n]` while the body used `[A §n]`; both are gone.
2. Exec summary, data types: "4.5 % of 32/64-bit values" → "4.5 % of multi-register values" (the evidence counts
   77,299 multi-register values). "Cannot swap bytes inside a register" → "always reads the bytes inside a register
   big-endian", the wording of the local code reading.
3. Exec summary, simulator: "30 issues ask for" → "30 classified issues concern" (the theme includes bug reports).
   Named Witte Modbus Slave 4.1.0 and "skipped responses" to match the changelog.
4. Exec summary and §2.1, `.mbmap`: "the format is two weeks old" → "the library repository was created on 13 Oct
   2026" (a `legacy/v1` format exists, so format age is unknown). "222 converted from other catalogues" → "222
   unofficial, of which at least 206 trace to two other open catalogues" (161 + 45 by `source` field).
5. Exec summary and §2.3, AI extraction: ModbusCloud's AI is announced, not shipped; "always" removed; CLARITY
   described as covering Modbus manuals among four protocols, with its 28-query gold set.
6. Exec summary, vendors: "work on projects, not live devices" marked [INFERENCE] (it is an absence-of-evidence claim
   in the AI evidence file). "Measured injection through log fields" now says "across three production LLMs".
7. Exec summary, anomaly detection: EWMA fit and ONNX/NuGet conflict marked [INFERENCE]; facts cited separately.
8. Exec summary and §2.4/§2.5, UNS: "No standards body defines" → "No standards-body definition was found".
   "One line-protocol writer covers…" marked [INFERENCE].
9. OPC UA size: "≥ 8 MB" → "about 5.9 MB (Core + Server), 8.3 MB with Client, before dependencies" (2,550,400 +
   3,331,200 bytes; + 2,439,296 bytes). Changed in the exec summary, §2.4, and Not recommended.
10. Sparkplug: "removes the Protobuf cost" → "would make Protobuf optional"; "arbitrary topic roots" → "an optional
   topic prefix at an arbitrary root"; date narrowed to the 2026-10-27 JSON commit.
11. CRA: "the rest from 11 Dec 2027" → "most other obligations" (Chapter IV applied from 11 June 2026). Steward
   definition now includes "intended for commercial activities"; "light-touch" quoted from the Commission page.
12. Smart App Control: "blocks them" → "blocks unsigned files unless they have a positive reputation" (Microsoft's
   wording; the feature "may supersede" SmartScreen).
13. CVE sentence: "project-file parsing and unchecked lengths" → "project files, an over-long input field and
   unchecked length fields" (CVE-2022-1068 is a registration field, not a file). CVE-2026-51539 now notes NVD status
   "Deferred" and sources the 16.6 s figure to libmodbus issue #843, not NVD.
14. §1 table: security row "OpenModScan, node-red" → "OpenModScan (3), pymodbus (2)" (pymodbus has 2, node-red 1).
   Added per-repo numbers where the draft gave names only. Added the caveat that ModbusScope counts are
   maintainer-heavy. "Python unpackaged" → "Python not on PyPI". "FC43" → "FC43/14"; "broadcast" → "serial broadcast".
15. §1 OpenModScan list: "requests" → "requests and reports" (word-order items are bug reports); added that OpenPoll
   already supports RTU over TCP.
16. §2.2: "most open GUI tools have … no fault injection" marked [INFERENCE] with the tools named; "exists only in"
   → "was found only in"; "no open Modbus tool supports them and 0 issue requests were found" → "none of the surveyed
   open tools documents them, and issue searches in four repositories found 0 requests". Added that the OpenModScan
   maintainer declined the sniffer request.
17. §2.3 MCP: "third shape change in about 13 months" → "third revision in about 13 months" (three revisions,
   2025-06-18 → 2025-11-25 → 2026-07-28, which is two changes). "Visible Pause/Disconnect" reworded to Modbench's
   own description.
18. §2.3 vendors: "all 2026 products support local or bring-your-own models" → limited to Beckhoff, Rockwell
   Conductor and Ignition Catalyst. Added Rockwell Design Studio Copilot's safety-code limit as the evidence for
   Rockwell's review stance.
19. §2.3 safety: "CISA/Five Eyes OT principles" → "CISA-led joint OT principles" (co-authors include BSI and
   NCSC-NL, which are not Five Eyes agencies). MCPTox "up to 72.8 %" kept (o1-mini, the highest).
20. §2.4 Mono: "effectively finished" → Mono's last major release date and Microsoft's migration advice.
21. §2.5 node-red-contrib-modbus: "does not add TLS" → "defers its TLS client to v6". Siemens line now notes mbaps
   conformance was not confirmed.
22. §2.5 OpenPoll code comparison marked [INFERENCE, from reading source; not tested].
23. Roadmap N6: removed ModbusLens (no evidence of rule-based diagnosis); added FC08 counter source. N5 "prerequisite
   for CI, Docker, Pi" marked [INFERENCE]. L6 citation moved from the IIoT file's §7 to the §4 evidence (Neuron,
   ThingsBoard, Ignition). X10 Home Assistant count updated to the live value (see checks).
24. Not recommended: PLCBench now states its measured result (75 of 240 episodes, 31.3 %); TimescaleDB reason added.
25. Caveats: "weeks to months old" → star counts (ModbusLens's age is not in the evidence). IEC 62443 caveat points to
   the security evidence file because no URL in it supports the paywall statement beyond the previews.
26. Added "Removed or weakened unsupported claims" and the Sources table.

Checks actually performed:

- Every `[X §n]` in the draft was compared with the cited section of the five evidence files; numbers in the
  exec summary, §1 table and §2 were matched to the evidence counts (U §3 table, D F3.1, I §2.3 table, S §5.1).
- Live URL spot-checks on 2026-10-29 (read in full or in the relevant part):
  - CISA AI-in-OT PDF [25]: resolves; Table 2 "Reliability" row contains the quoted sentence; nine authoring agencies.
  - TSB-AD NeurIPS page [27]: resolves; abstract contains the quoted sentence; DOI 10.52202/079017-3437.
  - mbmap-library README [9]: resolves; 6 verified / 267 official / 222 unofficial; 13 stars; CC0; converted-catalogue
    warning present.
  - Modbench README [19]: resolves; 12 stars; "It never writes to a device"; GUI control "off by default and local
    only (loopback port plus a token file)", pause/disconnect bar; "over 400 shared maps"; statistics with suggested
    timeout.
  - MCP spec 2026-07-28 tools page [79]: resolves; human-in-the-loop SHOULD text present; `_meta` fields required on
    every request (consistent with a changed protocol shape).
  - Sparkplug v4 draft topics chapter [36]: resolves; `[topic_prefix/]namespace/…`, `spCv4.0`/`spJv4.0`, group_id
    removed, STATE on `spCv4.0`.
  - Commission CRA reporting page [38]: resolves; 11 Sep 2026 manufacturers, 11 Dec 2027 stewards, 24 h/72 h/14 d.
  - Commission CRA open-source page [41]: resolves; "not monetised", stewards "light-touch", Art. 64(10) no fines.
  - Commission guidance PDF [40] and FAQ PDF [96]: both return HTTP 200 PDFs (1.19 MB, 66-page FAQ v1.4 with "Addition
    of FAQ 5.5" dated 04/09/2026 in its version table). The guidance paragraph text was not re-read.
  - Microsoft SmartScreen reputation [42]: resolves; EV no-bypass, unsigned zero reputation, Smart App Control quote.
  - Microsoft code signing options [99]: resolves; ~$9.99/month; organisations USA/Canada/EU/UK, individuals USA/Canada.
  - NVD CVE-2026-51539 [106]: resolves via NVD API; status Deferred; CVSS 7.5; references issue #843.
  - libmodbus issue #843 [107]: resolves; 12-byte request, about 16.593 s block, 2 s timeout, Windows 11.
  - Home Assistant analytics [117]: `current` block on 2026-10-29 shows modbus 18,692, mqtt 274,486, active
    installations 713,777 (the evidence file's 28 Oct snapshot said 18,655).
  - LogInject USENIX PDF [26]: HTTP 200 `application/pdf` (content not re-read).
- Paper metadata, two indexes each:
  - MCPTox: arXiv 2508.14925 (Wang et al., 2025-08-19, v2 2026-09-29) and Crossref DOI 10.1609/aaai.v40i42.40895
    (AAAI 2026, title "MCPTox: A Benchmark for Tool Poisoning on Real-World MCP Servers"; the arXiv title adds
    "Attack"). The abstract confirms 72.8 % (o1-mini) and refusals below 3 %. The preprint and the AAAI record are
    listed together in [85].
  - TSB-AD: NeurIPS proceedings page and Crossref (Liu, 2024) and OpenAlex W4415795917 (Liu, Paparrizos, 2024).
  - CLARITY: arXiv 2608.08618 and OpenAlex W7202189492 (Ganie, Ezzini, Marazi, 2026-08-09, preprint). Abstract
    confirms F1 = 0.87, 28 queries, five-device benchmark, four protocols.
  - PLCBench: arXiv 2608.26882 and OpenAlex W7204604947 (Zhou et al., 2026-08-27, preprint). Abstract confirms 75 of
    240 episodes (31.3 %).

Coverage statement: URLs not listed above were not re-fetched in this pass; for those, the Sources table relies on the
evidence files, whose authors report reading them on 2026-10-29. Sources the evidence files mark as snippet-only
(PLCtalk [11][12]) or unverified (ModbusCloud 2.2 [18], Ed25519 in .NET 4.8) stay labelled in the text. LogInject,
NIST AI 600-1, OWASP and the Commission guidance paragraphs were not re-read. LogInject's venue (USENIX Security 2026)
was not checked against a second index. Local artifacts [8][10][108] were partly re-checked (`openpoll-win/README.md`,
`CHANGELOG.md`, `openpoll/pyproject.toml`, and `src` grep for broadcast handling); the code-reading claims about
deadlines and loaders were not re-verified.

