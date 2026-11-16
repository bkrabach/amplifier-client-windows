# Initial implementation decisions

Windows 11 is the initial target. .NET 10/WPF supplies Fluent settings controls and
transparent layered windows; Win32 nonactivation and work-area APIs implement Tempo.
This is an implementation choice, not a change to shared state/permission contracts.
Core protocol/presentation code is independent of Windows and testable without UI.

Use advertised version-1 observations: native attachment without browser state,
sessions, attention and incremental activity. Do not adopt `/api/companion`.
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
