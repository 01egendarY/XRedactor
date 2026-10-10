# Editor Regression Checks

Run from the repository root:

```powershell
dotnet run --project tests/XRedactor.Tests -c Release
```

The checks cover document serialization, atomic saving, Unicode, validation and filename changes. In-process WPF checks exercise font sizes, typing groups, undo across text and ink, shape deletion, ink clipping, zoom anchors and expansion of the plane.

UI previews are rendered beside the test executable in `bin/Release/net10.0-windows`. These checks do not replace manual testing of pointer input, keyboard interaction or Windows rendering in a live application window.

Application release notes are maintained in [CHANGELOG.md](../../CHANGELOG.md).
