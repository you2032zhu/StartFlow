# StartFlow · 启序

[简体中文](README.md) · [User and build guide (Chinese)](docs/用户与制作教程.md) · [Validation record](docs/验证报告.md)

[Download](https://github.com/you2032zhu/StartFlow/releases/latest) · [Issues](https://github.com/you2032zhu/StartFlow/issues) · [Builds](https://github.com/you2032zhu/StartFlow/actions/workflows/build.yml)

A local Windows 11 startup task manager built with C#, .NET 10 LTS, WPF and MVVM. Choose a plan after sign-in and explicitly click Run. StartFlow launches desktop and Store apps, websites, files, folders, shortcuts, scripts and commands, using sequential delays or a shared timeline.

No account, telemetry, ads, cloud database, automatic update checks or page-load probing. Windows startup is off by default. Manual opening shows the manager. One instance is allowed per user, with tray integration and a one-shot next-sign-in skip.

## Distribution

Both x64 installer and portable ZIP are self-contained. Users need no .NET installation. Installer data lives in `%LOCALAPPDATA%\StartFlow`; portable data lives in `Data` next to the executable. Extract and move the entire portable folder. Configure sign-in startup in Settings; repair its path after moving the portable copy.

Download the installer or portable ZIP from [GitHub Releases](https://github.com/you2032zhu/StartFlow/releases). Each release includes the corresponding complete source ZIP and SHA256SUMS. Locally built outputs are in `artifacts`.

## Build and test

Windows 11 x64, .NET SDK **10.0.402**, Inno Setup **7.1.0 x64**. No application-level third-party NuGet packages.

```powershell
dotnet build StartFlow.sln -c Release -m:1
powershell -NoProfile -File scripts/Build.ps1 -WindowsIntegration
powershell -NoProfile -File scripts/Publish.ps1 -InnoCompiler "C:\tools\Inno Setup\ISCC.exe"
```

The dependency-free executable test harness uses a controllable TimeProvider and a fake launcher. It must be run through Build.ps1 or `dotnet run --project tests/StartFlow.Tests -c Release -- --windows-integration`; `dotnet test` is not the test entry point. Windows tests use temporary targets and uniquely named startup registry values, then clean them up.

Core logic, Windows integration, WPF presentation, tests and packaging have separate projects/directories. UI strings are centralized in `Resources/strings.json`. See `docs/ARCHITECTURE.md` for invariants and tradeoffs.

## Honest limits

Browser settings control windows/tabs; StartFlow cannot guarantee a shared window across browsers. Submitted requests do not prove page loading, login or application readiness. Window states are hints. Process access restrictions and parameterized targets can make running detection inconclusive, which is reported. Scripts respect PowerShell execution policy; UAC is never bypassed. Stop never kills user-launched processes. A pending native UAC request must return before its in-flight launch can finish stopping.

No signing certificate is supplied and no claim of SmartScreen trust is made. See the validation record for the distinction between automated checks, actual Windows checks and remaining manual scenarios.

## License

**GPL-3.0-only**; see the complete [LICENSE](LICENSE). Commercial use is allowed. Distribution of covered modified versions or object code requires complying with the GPL, including providing corresponding source to recipients as required. See [third-party notices](THIRD-PARTY-NOTICES.md). Repository maintainer: [you2032zhu](https://github.com/you2032zhu).
