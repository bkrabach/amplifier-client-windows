# Windows client promises — v1

Implementation target derived from the approved desktop experience. The shared
DC/CX/CS contracts remain canonical in Unified; this document owns Windows choices.
Source implementation, physical acceptance and published delivery are distinct.

- **WP1: Native interaction.** Tray and optional nonactivating Tempo windows retain
  the previous app's focus during passive interaction. Settings/composer intentionally
  accept input. Accessible names, keyboard paths and reduced-motion behavior apply.
- **WP2: Presentation continuity.** Semantic state is independent of avatar/palette.
  Preserve expansion, scroll, grouping and maximum height across polling and hover.
  The notice viewport stays above its avatar and within the current work area.
  Dismissal is local presentation, never a host read receipt or approval.
- **WP3: Independent services.** Each registered origin owns its credential,
  host identity, status, polling and captured command destination. Default global
  attention ordering can optionally group services, sorting within each group.
- **WP4: Deliberate authority.** Browser comparison-code approval issues the existing
  restricted chat credential. Windows protects secrets for the current OS user.
  No cookies, bearer tokens or machine identifiers enter navigation or logs.
  Unsupported discovery fails visibly; no implicit full-control fallback.
- **WP5: Recovery.** Save reply intent before sending. Uncertain commands retain
  their original ID, payload and destination. No automatic resend on startup,
  reconnect, wake or a repeated activation. An unavailable service cannot receive
  another service's command. Forget does not silently discard uncertain messages.
- **WP6: Independent delivery.** Stable application identity, signed installer and
  verified update preserve settings and saved intent. No server installation or
  restart is a side effect. Unsigned development packages are labeled honestly.

Acceptance follows shared DJ1–DJ7 on Windows with exact client/host versions,
synthetic fixtures, real interaction and separately qualified release artifacts.
