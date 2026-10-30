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
      identity of the copyright holder. The GitHub publisher account is `Wayan123`, not a verified legal identity.
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

## Windows v0.5.1 release

- Release license: **GPL-3.0-or-later**. GitHub publisher account: **`Wayan123`** (account name, not an
  independently verified legal copyright holder). This release decision does not establish legal clearance.
- [x] **Record binary signing decision.** Authenticode signing is explicitly deferred for v0.5.1; no Windows
      code-signing certificate has been supplied. The release executable remains **unsigned**. Windows may
      display an unknown-publisher or SmartScreen warning, and organizational security policy may block it.
      A SHA-256 checksum verifies matching bytes, not publisher identity; signing would not itself guarantee
      SmartScreen reputation or acceptance.
- [x] **Record Windows release validation.** Both builds succeeded on 2026-10-30. Simulator-only checks:
      selftest 259, main GUI 81, devices GUI 42, toolbar GUI 21; Python tests 37, all passed.
      The commands and evidence scope are in [`docs/releases/v0.5.1.md`](docs/releases/v0.5.1.md).
      Per-run registry keys were removed; audit files remain in the local run directory. No hardware accessed.
- [x] **Refresh and document feature screenshots.** Added 18 app-rendered simulator illustrations
      (`DrawToBitmap`), visually checked with captions in README. The profile-directory row is masked.
      Native file/print dialogs and the alarm-history message box are not pictured.
- [x] **Compute and verify the final executable's SHA-256.**
      `90d65a7a1b8c1d7fb5afef8e384c31f8f6c90fc2a0cff5bb991d816433d85915`
      (359,424 bytes; version 0.5.1.0). Recompute if the executable changes.
- [ ] **Publish the GitHub Release and verify downloads.** Attach `MultiPollStudio.exe` and its SHA-256;
      do not commit the executable. Confirm the published asset and checksum match the final validated build
      before marking publication complete.
- [ ] **Determine the legal publisher identity and country** (person or PT AGS) and assess the applicable
      EU CRA role (roadmap brief §2.5). The GitHub account name alone does not settle this.
