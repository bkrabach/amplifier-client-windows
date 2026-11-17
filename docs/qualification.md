# Windows qualification

This is a development candidate, not a signed installable product. Track each
claim against the tested source/artifact; Mac qualification does not transfer.

## Reproducible local checks

.NET SDK is pinned in global.json. Python 3.13 and the exact Pillow version in
scripts/requirements.txt prepare lossless APNG frames.

```powershell
./scripts/build.ps1
& ./.artifacts/preview-win-x64/Amplifier.exe --self-test
& ./.artifacts/preview-win-x64/Amplifier.exe --preview
```

`--self-test` runs off-screen WPF rendering for all six shapes in both finishes
and a synthetic current-user DPAPI round-trip. It never connects to a host.
Results go into the preview's `native-checks` directory in LocalAppData.
`--preview` shows synthetic activity, disables connection enrollment and message
submission, and uses a separate mutex/settings directory. It does not run at login.
Quit from its tray menu. This is the safe visual-review entry point.

The ordinary executable uses a different settings directory and supports explicit
restricted chat pairing. It remains an unqualified development path. Neither mode
contains a browser/desktop task executor or host credentials in navigation.

## Implemented and source-tested

- HTTPS origin restrictions and scoped attach/observation discovery.
- Generic observation-only reads using existing restricted paired credentials;
  explicit identity and shared-dismissal discovery, with no companion-feed fallback.
- Sequential bounded pagination, empty continuation pages, count completion,
  coherent revisions, cursor recovery, authentication failure and response bounds.
- Per-service isolation, explicit global/grouped ordering and shared exact-state
  dismissal receipts. Appearance and layout preferences remain local.
- Saved replies before submission, immutable retry identity/target, uncertain
  delivery retention and no startup/reconnect replay.
- Hover grace/drag hold, anchored viewport limits and semantic animation playback.
- Native shell cross-build and original artwork provenance.

## Required before general release

- Normal independent source review and accepted source identity.
- Synthetic live service acceptance inside an owned DTU using the supported host
  release. HTTP fixtures prove client logic, not actual service integration.
- Windows interactive focus/drag/click/resize/scroll, mixed-DPI displays, screen
  edges, taskbar placement, sleep/resume, Narrator, keyboard and reduced motion.
- Native actionable notifications, duplicate-activation handling and warm browser
  handoff; no claim that opening a URL reuses an existing PWA window.
- Composer new-chat creation and durable editable drafts; current composer targets
  existing observed chats and saves send intent before delivery.
- Broader tool-action coverage beyond bounded host-provided display summaries.
- Stable application/notification identity, opt-in startup, signed installer and
  signed update delivery, including exact-artifact clean install/upgrade/uninstall.
- Windows signing/publisher identity and protected publication workflow. No Mac
  signing credential is suitable for or copied into this repository.

A successful native off-screen test does not qualify user focus, accessibility,
notifications, pairing, browser reuse or signed installation. ARM64 requires its
own actual runtime qualification even though a publish target is available.

## Notification parity follow-up

Core checks cover stable Mac-compatible attention tiers, pinned notices outside
active sessions, unread/working deduplication, size defaults, bounded preview
revalidation, raw-tool exclusion, and exact-origin PWA launch arguments. Native
Windows 11 synthetic-window checks exercise actual wheel delivery through gaps at all
three sizes, no outside capture, retained row anchor and foreground focus, and
visibility-mode changes preserving size/expansion. These checks use synthetic
windows and no service writes. The installed Edge PWA was launched twice through
the candidate: its window handle and total Edge window count stayed unchanged.
That establishes warm root-workspace reuse, not a full browser/profile or deep-link
acceptance matrix. Other origins without an installed PWA retain browser fallback.

The Mac context-menu equivalent is independently qualified and released by its
canonical repository; no Windows result is represented as Mac acceptance.

## Shared observations preview

The generic-only transport passed bidirectional dismissal, host restart and new-state
checks against an isolated real service using separate Mac and Windows credentials.
Existing credentials remain valid on Unified 0.103.0; unsupported hosts require an
update. These are transport checks, not signed installer qualification.
