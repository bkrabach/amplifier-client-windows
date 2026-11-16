# Initial implementation decisions

Windows 11 is the initial target. .NET 10/WPF supplies Fluent settings controls and
transparent layered windows; Win32 nonactivation and work-area APIs implement Tempo.
This is an implementation choice, not a change to shared state/permission contracts.
Core protocol/presentation code is independent of Windows and testable without UI.

Use advertised version-1 observations: native attachment without browser state,
sessions, attention and incremental activity. The coordinated cutover removes the
companion adapter and its permission-denial fallback. Unsupported hosts report
that they need a compatible service; clients never broaden credentials to recover.
Shared exact-state dismissal and safe preview projection are coordinated with the
canonical service contract. This branch is not deployed compatibility evidence.
One bounded observation traversal per service publishes complete results only;
pagination, deadlines, protocol errors and authentication failures fail closed.
TLS validation stays enabled, redirects and cookies disabled, and advertised URLs
must remain on the registered origin. Device proof remains only in memory.

Connection scope is registration + durable host identity + chat. Pin the durable
host identity after initial authorized attachment; reject replacement rather than
retargeting work. Native identity is per installation, with a separate paired-client
binding for each credential. Local presentation state is not another unread store.

The Windows shell owns tray, passive companion, actionable settings/composer,
Windows user-protected credentials and installation lifecycle. The existing web
client owns full workspace rendering. Browser handoff cannot claim warm PWA reuse
until tested with the chosen Windows installation/profile; do not inspect or copy
private browser profile data to manufacture that proof.

The initial public home is bkrabach/amplifier-client-windows, pending the owner's
Microsoft registration and transfer. Signing identity,
installer/update channel, real Windows hardware and physical acceptance are explicit
release prerequisites. Linux compilation is not Windows interaction qualification.

### Notification interaction and navigation correction

Notification sizes are Compact (default), Comfortable and Large (the initial
pilot size); avatar size is independent. Direct context-menu visibility choices
share Settings state and preserve expansion and height. Mac's attention tiers
and stable host ordering are mirrored: action required, other attention, then
ongoing work. A chat with unread attention and active work occupies one row.
Pinned notice metadata remains sufficient even outside the active-session page.

A near-transparent viewport hit plane spans gaps and faded edges. While visible,
a Windows wheel hook handles only vertical wheel events over this exact,
unobscured window's viewport, returning all other input untouched. It is removed
when hidden/closed; no input is recorded. The independent header and passive
focus behavior remain. Refresh restores the visible row's anchor.

Optional bounded conversation detail reads enrich visible notices; membership and
priority still come solely from observations. Completion text waits for a later
complete matching fingerprint with no active turn. Tool display uses projected
labels/hints only, not raw inputs/results. A detail failure cannot broaden scope,
mark read, replay work or discard the authoritative observation.

Windows installed Edge PWA registrations provide the start origin, app ID and
profile directory. An unambiguous same-origin registration launches through Edge
with the same Chromium app/shortcut URL arguments used by the Mac adapter. No
browser profile database, cookies or credentials are read. Other/ambiguous origins
retain ordinary browser navigation. Launch-handler support in the web app remains
necessary for warm in-place navigation; discovery alone does not prove reuse.

### Shared display receipts

Each active/preparing session and attention row supplies a host-issued dismissal
identity/fingerprint. `SharedDismissals` persists an exact pending intent before
hiding it locally. The client retries only that idempotent display receipt after
connection recovery; it never retargets a newer state or replays a chat command.
A complete fresh observation retires accepted/obsolete intent. Other devices see
the accepted host receipt through the same generic collections. Read/approval
state and counts keep their canonical meanings. Corrupt local state is retained
for recovery. Appearance, geometry, hover/visibility, grouping and sound remain
local; host pins are the shared opt-in for agent-created chats.

Candidate shared wire owner: [presentation contract](https://github.com/microsoft/amplifier-unified/blob/e7bc197dd/docs/clients/presentation.md) at `e7bc197dd`, based on accepted `46f0fe683b6bf87914fcda170e0ef3f6bd26f67f`. Candidate qualification is separate from service release/adoption.
