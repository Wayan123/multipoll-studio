# Release checklist (GitHub)

Facts behind each item: `outputs/github-release-legal.md` (sources quoted there). Not legal advice.

## Source publication status

- [x] **Choose a product name.** MultiPoll Studio was selected on 2026-10-30; the executable, registry key,
      local-data directory, Python package/CLI and file identifiers have been renamed.
- [ ] **Complete name/trademark clearance before public release.** Choosing a different name is not legal clearance.
      A separate [multipoll](https://github.com/EfimAlekseevich/multipoll) project also describes Modbus polling;
      this is a same-domain near-match, not a determination of trademark rights or likelihood of confusion.
- [x] **Release authorization.** The project owner confirmed on 2026-10-30 that they are authorized to release
      this code publicly under GPL-3.0-or-later. This confirmation does not independently establish the legal
      identity of the copyright holder; publisher identity remains to be documented.
- [x] **Check tracked third-party artifacts.** The initial commit does not contain the proprietary binaries, help
      files or vendor PDFs excluded by `.gitignore`. This does not establish copyright clearance for text or assets.
- [x] **Scan published history for secrets.** `gitleaks detect --source . --redact` scanned the initial commit on
      2026-10-30 and reported no leaks; scans cannot guarantee that no secrets are present.
- [x] **Enable private vulnerability reporting.** GitHub's repository API returned `enabled: true` on 2026-10-30.
- [x] **Verify GitHub commit signature.** GitHub reports `verified: true`, `reason: valid` for the initial commit.

## Already in place

- License GPL-3.0-or-later in `LICENSE`; `NOTICE.md` with the MODBUS® trademark line, non-affiliation with Witte
  Software, device-profile provenance and the no-warranty note.
- `re/REPORT.md` records a static study of the commercial application. This is not a formal clean-room process or
  a legal clearance. Review source, documentation, assets and complete Git history before publication; exclude
  proprietary copies and consult qualified counsel where needed. The conclusions of cases from other jurisdictions
  do not by themselves establish an Indonesian or worldwide legal result.
- Device profiles contain register facts with their sources; unverified facts are flagged in the file and the UI.
- `SECURITY.md`, `CONTRIBUTING.md` (DCO sign-off), `README.md`.

## Before a binary release

- GitHub publisher account: `Wayan123` (account name, not an independently verified legal copyright holder).
- Current Windows EXE is unsigned; no Windows code-signing certificate has been supplied.
- [ ] Decide binary signing and document platform requirements. Unsigned EXEs can be blocked by Windows security
      policy; signing is not a guarantee of SmartScreen reputation or acceptance.
- [ ] Attach `MultiPollStudio.exe` and its SHA-256 to a GitHub Release; do not commit it.
- [ ] Decide the publisher identity (person or PT AGS, country) — it decides the EU CRA role (roadmap brief §2.5).
