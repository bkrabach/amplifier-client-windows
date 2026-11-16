# Amplifier for Windows

Native Windows tray and optional Tempo companion for one or more Amplifier services.
This is an **early development preview**, not a signed end-user release. The initial
implementation is under review; Windows interaction and installation acceptance
are tracked separately from compilation.

The Windows client follows the shared desktop experience: connection approval,
service-scoped attention, a small composer, and six animated crystal avatars. The
existing web client remains the full workspace. Chat pairing does not authorize
browser or desktop execution.

## Project home

Development temporarily lives at `bkrabach/amplifier-client-windows`, pending the
owner's Microsoft repository registration process. The intended destination is
`microsoft/amplifier-client-windows`. The repository includes the ecosystem's
standard contribution and legal text below; **Microsoft CLA automation and
organization teams are not configured in this personal repository**.

## Design and delivery

- [Product vision](docs/VISION.md)
- [Windows contract](contracts/windows-client.v1.md)
- [Architecture and ownership](docs/architecture.md)
- [Development](CONTRIBUTING.md) and [build/qualification](docs/qualification.md)
- [Repository audit and transfer checklist](docs/repository-audit.md)

No server installation, service restart, browser-profile access, or native task
executor is part of this client. Keep real connection credentials, host addresses,
private screenshots, and machine-specific evidence out of this public repository.

## Contributing

> [!NOTE]
> This project is not currently accepting external contributions, but we're actively working toward opening this up. We value community input and look forward to collaborating in the future. For now, feel free to fork and experiment!

Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit [Contributor License Agreements](https://cla.opensource.microsoft.com).

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
