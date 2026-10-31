# Repository Guidelines

## Project Structure & Module Organization

ClipFlow is a Windows clipboard-history application built with C#, .NET 8, WinUI 3, and Windows App SDK.

- `ClipFlow/ClipFlow/`: application entry points, `Views/`, `ViewModels/`, platform `Services/`, Debug-only `Diagnostics/`, and branded `Assets/`.
- `ClipFlow/ClipFlow.Core/`: models, SQLite persistence, content identity, and text paging.
- `ClipFlow/ClipFlow.Tests/`: executable core acceptance checks and clipboard fixtures.
- `scripts/`: PowerShell build, test, packaging, and asset tools.
- `docs/`: validation notes and Store release instructions; `licenses/`: dependency notices.

## Build, Test, and Development Commands

Run from the repository root on Windows with .NET SDK 8 or newer; CI uses .NET 10. Initial restore requires network access.

- `./scripts/Build.ps1 -Run`: build and launch the unpackaged Debug x64 app.
- `./scripts/Build.ps1 -Configuration Release`: build the Release app.
- `./scripts/Test.ps1`: run core acceptance checks.
- `./scripts/Test.ps1 -Integration`: additionally build and exercise the UI on an interactive Windows desktop; this replaces clipboard contents.
- `./scripts/Test-ReleaseReadiness.ps1`: run release source checks used by CI.

Store packaging requires Visual Studio MSBuild and WinUI/MSIX tooling; follow `docs/STORE-RELEASE.md`.

## Coding Style & Naming Conventions

Match nearby code: four-space C# indentation, file-scoped namespaces, PascalCase types/members, camelCase locals/parameters, and `_camelCase` private fields. Keep nullable annotations enabled and follow existing XAML formatting. No dedicated formatter or linter configuration is checked in.

Preserve MVVM boundaries, native Fluent controls, theme resources, and keyboard accessibility. Keep expensive I/O off the UI thread.

## Testing Guidelines

Tests use a custom `Check(condition, "descriptive behavior")` harness in `ClipFlow.Tests/Program.cs`, rather than a `dotnet test` adapter. Add behavior-focused checks there; no numeric coverage threshold is configured. Use GUID-isolated data directories and preserve migration, favorite, and settings behavior.

Run integration checks for clipboard, image, tray, theme, or view changes. Startup changes also require `./scripts/Test-PackagedStartup.ps1`, Windows Developer Mode, and MSBuild.

## Commit & Pull Request Guidelines

History currently contains only `chore: initialize ClipFlow project`; use similarly concise, prefixed subjects. Keep PRs focused. Discuss major changes in an Issue first. Include the problem, resulting behavior, linked Issue, Windows/test environment, results, UI screenshots when relevant, and known limitations. See `CONTRIBUTING.md`.

## Security & Configuration

Preserve local-only storage. Discuss new networking, telemetry, or permissions first. Never commit clipboard data, databases, screenshots containing private content, credentials, certificates, personal Store associations, absolute developer paths, or build outputs.

## Agent Instructions

For library, SDK, API, CLI, or cloud documentation, use Context7: resolve the library ID first, then query one concept at a time.
