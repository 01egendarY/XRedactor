# XRedactor

XRedactor is a local-first Windows notebook and visual canvas editor.

## Repository layout

- `src/XRedactor` - WPF/.NET source code.
- `docs/MASTER_PROMPT.md` - product and engineering specification.
- `publish/XRedactor` - local release build (intentionally ignored by Git).
- `Notebooks` - created next to the running executable and ignored by Git.

## Build

```powershell
dotnet build XRedactor.slnx
dotnet publish src/XRedactor/XRedactor.csproj -c Release -r win-x64 --self-contained true -o publish/XRedactor
```
