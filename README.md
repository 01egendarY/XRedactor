# XRedactor

XRedactor is a local-first Windows notebook and visual canvas editor.

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

Notebook font sizes use cells for grid paper, lines for ruled paper, and points for blank/spiral paper. The editable size field accepts custom values; plus/minus buttons adjust it quickly. Plane note sheets expose their paper and font options when selected; shape tools open in a horizontal context panel.

Settings include system/light/dark themes, autosave timing, fit-on-open, zoom speed, canvas deletion confirmation, and a resizable document sidebar. Documents can be renamed from the home screen, including their `.xred` filenames. Ink is restricted to canvas tiles. Keyboard shortcuts include Ctrl+B/I/U, Ctrl+Z/Y, Ctrl+S, and Ctrl+0/1.

Run regression checks with `dotnet run --project tests/XRedactor.Tests -c Release`. These cover storage and in-process WPF editor behavior, including mixed-action undo, typing groups, font sizes, ink clipping, zoom anchors, and expansion in both directions. They also render UI previews beside the test executable.
