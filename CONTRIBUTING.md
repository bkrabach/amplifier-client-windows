# Development

Read [AGENTS.md](AGENTS.md), the vision and Windows contract first. Changes go
through pull requests and independent review. Shared protocol promises belong in
Amplifier Unified; platform implementation belongs here.

The current source branch adds a .NET 10 WPF application, a platform-neutral core,
and lossless packaging of the approved Mac avatar artwork. Build instructions and
the exact qualification checklist are in [docs/qualification.md](docs/qualification.md). A successful
cross-build does not establish Windows focus, DPI, notification or install behavior.

Use synthetic test data and isolated test services. Do not restart a live server
for development. Never commit personal paths, service tokens, pairing secrets,
certificates, private logs or recordings. Retain attribution for reused artwork.

Microsoft's canonical contribution policy is in [README.md](README.md#contributing).
CLA bot enrollment is a transfer prerequisite, not a currently configured check.
