# XRedactor

XRedactor is a local-first Windows notebook and visual canvas editor.

Current version: **1.0**. Release details are recorded in [CHANGELOG.md](CHANGELOG.md).

## Repository layout

- `src/XRedactor` - WPF/.NET source code.
- `docs/MASTER_PROMPT.md` - product and engineering specification.
- `src/XRedactor/bin/Release/net10.0-windows/win-x64` - local self-contained Windows release, ignored by Git.
- `Notebooks` - created next to the running executable and ignored by Git.

## Build

```powershell
dotnet build XRedactor.slnx
dotnet publish src/XRedactor/XRedactor.csproj -c Release -r win-x64 --self-contained true -o src/XRedactor/bin/Release/net10.0-windows/win-x64
```

Run checks with `dotnet run --project tests/XRedactor.Tests -c Release`. See [test documentation](tests/XRedactor.Tests/README.md) for coverage and limitations.

## Release convention

Use only `Version <number>` as the commit message, for example `Version 1.0`. Record changes in `CHANGELOG.md`, grouped by application version, with the newest version first. Updates within the current version belong to its existing section. Do not rewrite published history or move existing version tags for cosmetic changes.
