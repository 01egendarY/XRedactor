# XRedactor

XRedactor is a local-first Windows notebook and visual canvas editor.

Current version: **1.0**. Release details are recorded in [CHANGELOG.md](CHANGELOG.md).

## Overview

- **Notebook:** a focused paper surface for formatted text and handwriting.
- **Plane:** connected canvases for notes, shapes, diagrams, and freehand drawing, with zoom and minimap navigation.
- **Local storage:** each document is saved as a `.xred` file in the `Notebooks` folder beside the application.

Repository documentation and code identifiers use English. The application interface currently uses Russian; Unicode text in test fixtures is intentional.

## Requirements

- Windows 10/11.
- .NET 10 SDK for building and running the source project.

The self-contained `win-x64` build does not require a separately installed .NET runtime. Keep the executable and its companion files together in a writable folder so the application can create `Notebooks`.

## Repository layout

- `src/XRedactor` - WPF/.NET source code.
- `docs/MASTER_PROMPT.md` - product and engineering specification.
- `tests/XRedactor.Tests` - storage and editor regression checks.
- `src/XRedactor/bin/Release/net10.0-windows/win-x64` - local self-contained Windows release, ignored by Git.
- `Notebooks` - created next to the running executable and ignored by Git.

## Build

```powershell
dotnet build XRedactor.slnx -c Release
dotnet run --project src/XRedactor -c Release
```

To create a self-contained Windows build:

```powershell
dotnet publish src/XRedactor/XRedactor.csproj -c Release -r win-x64 --self-contained true -o src/XRedactor/bin/Release/net10.0-windows/win-x64
```

Run checks with `dotnet run --project tests/XRedactor.Tests -c Release`. See [test documentation](tests/XRedactor.Tests/README.md) for coverage and limitations.

## Release convention

Use only `Version <number>` as the commit message, for example `Version 1.0`. Record changes in `CHANGELOG.md`, grouped by application version, with the newest version first. Updates within the current version belong to its existing section. Do not rewrite published history or move existing version tags for cosmetic changes.

Keep repository documentation, developer-facing comments, and release notes in English. Application localization and Unicode test data are independent of the documentation language. Generated builds and user documents are excluded from Git.
