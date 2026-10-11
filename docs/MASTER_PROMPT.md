# XRedactor Master Implementation Prompt

You are building **XRedactor**, a local-first Windows desktop application for visual notes. Treat this document as the authoritative product and engineering brief. Implement the product incrementally, but every increment must remain buildable, testable, and preserve existing user documents.

The version 1.0 baseline is described below. Features listed under Planned follow-up work are goals, not current release capabilities. Keep this specification, README, and changelog consistent with the implementation.

## Product intent

On every New Document action, ask the user to choose between two deliberately different experiences:

1. **Notebook** is a focused single-page document for a daily plan, checklist, journal entry, or short note. It contains exactly one paper/notebook surface. The user can type formatted text or draw freehand on it. Do not show shapes, tiles, minimap, or spatial-editor controls in this mode.
2. **Plane** is a spatial workspace for long-form notes, diagrams, handwriting, shapes, and multiple connected canvases. It supports zooming, panning, objects outside canvases, a minimap, and expansion by adjacent canvas tiles.

The distinction must stay visible in the UI and data model. Do not turn Notebook into a restricted Plane through a confusing set of hidden controls.

## Platform and repository

- Target Windows 10/11 using C#, WPF, and .NET 10.
- Avoid external dependencies unless they remove substantial complexity and are approved.
- Keep source under `src/XRedactor` and documentation under `docs`.
- Publish a self-contained `win-x64` build to `src/XRedactor/bin/Release/net10.0-windows/win-x64`, without a duplicate `publish` directory.
- Commit source and docs. Do not commit `bin`, `obj`, published binaries, or user documents.
- Use English for repository documentation, code identifiers, developer comments, and release notes. Keep the Russian application interface and Unicode test fixtures intact.
- Use version-only commit messages such as `Version 1.0`; describe changes in `CHANGELOG.md` under the corresponding application version.

## Branding and storage

- Working product name: **XRedactor**.
- Use one versioned JSON document per user file with the `.xred` extension.
- Create `Notebooks` beside the running executable and store documents there.
- Acknowledge that a read-only installation directory cannot support portable storage; show a clear error instead of silently writing elsewhere.
- Save through a temporary file followed by atomic replacement.
- Autosave after meaningful edits with debouncing, and save on explicit Save and graceful exit.
- Version the document schema from the first release and reject unsupported schema versions without modifying the original file.
- Store interface preferences in the user's local application data, separately from portable document files.

## Start screen

- Show existing documents with name, kind, and modification date.
- Provide Create, Open, Rename, and Delete actions; deletion requires confirmation.
- Renaming updates both the document title and its filename. Do not overwrite another document with the same name.
- Allow resizing the document sidebar and remember its width.
- On Create, first choose Notebook or Plane, then show only relevant options.
- Use clean Russian UI copy initially while keeping code identifiers in English.

## Notebook mode

- Offer templates: blank, grid, ruled, top spiral, and left spiral paper. Retain legacy serialized template values for compatibility.
- Display only navigation, document name, Save, text/draw mode, minimal text formatting, and pen controls.
- Text formatting: font size, color, bold, italic, and underline.
- Measure grid-paper font sizes in cells, with a default of 2; measure ruled-paper sizes in lines, with a default of 1. Use points for blank and spiral paper, with a default of 18. Provide editable size fields and increment/decrement buttons.
- Drawing: freehand pen color and thickness plus eraser.
- Typed text and ink coexist on the same paper without changing each other's colors.
- Keep the page readable, centered, and scrollable on small windows.
- Support undo/redo for text and ink.

## Plane mode

- Start with one configurable canvas tile.
- Configure the first tile's width and height when creating the document.
- Show an unobtrusive plus control on every free orthogonal edge of every tile.
- Adding a tile places an equal-sized tile in that grid cell and permanently locks tile dimensions for the document.
- Keep at least one tile. Use the saved confirmation preference when deleting a tile and warn when it intersects content.
- Allow text and shape objects outside tile bounds, but restrict ink input and visible strokes to the union of the canvas tiles.
- Wheel over the workspace zooms around the pointer. `Shift+Wheel` scrolls horizontally. Middle-drag or `Space+drag` pans.
- Support Select, Pen, Eraser, Text, Rectangle, Ellipse, large note sheets, and a dedicated canvas deletion tool. Do not show canvas coordinate labels or inline deletion buttons.
- Objects support movement, resizing, deletion, fill, outline, and independent text color. Keep stored z-order stable.
- Text inside objects supports size, color, bold, italic, and underline.
- Open shape creation and appearance controls in a horizontal context toolbar. Show paper, font family, and cell/line/point size controls when a note sheet is selected.
- Change object fill independently of contained text color.
- Add a collapsible minimap showing tile extents, object bounds, and the current viewport.
- Provide `Ctrl+0` for 100% zoom and `Ctrl+1` for fit-all.

## Editing behavior

- Implement `Ctrl+Z`, `Ctrl+Shift+Z`/`Ctrl+Y`, `Ctrl+S`, `Ctrl+B`/`Ctrl+I`/`Ctrl+U`, and `Delete` where applicable. Retain standard text copy/paste behavior in editable fields.
- Coalesce continuous typing, drawing, and dragging into sensible undo transactions.
- Use stable IDs for documents, tiles, and elements.
- Keep world coordinates independent of camera coordinates and DPI.
- Do not couple zoom to current content size.

## Storage and resilience

- Recover cleanly from malformed files: keep the original file untouched and report the problem.
- Validate document versions, names, tile dimensions, and collection sizes while loading.
- Add focused tests for serialization, coordinate conversion, tile-neighbor rules, and undo history.

## Planned follow-up work

- Add line and arrow objects, duplication, explicit layer-order controls, and text alignment controls.
- Add safe tile-size editing before the first neighboring tile is created.
- Add canvas background controls that do not affect object or text colors.
- Export selected canvases to PNG without destabilizing editing.
- Preserve unknown future fields when introducing document migrations.
- Preserve stroke identities across serialization and extend load-time validation for zoom values, coordinates, and colors.
- Extend manual live-interface test coverage.

## UX and visual direction

- Prefer a quiet editorial interface: warm paper surfaces, dark graphite navigation, restrained red accent, generous spacing.
- Avoid a generic enterprise dashboard appearance.
- Notebook mode should feel almost tool-free; Plane mode may be denser but controls must remain collapsible.
- Use icons only with tooltips and accessible names. Preserve keyboard access and visible focus states.
- Support white, black, gray, dark blue, purple, and system interface themes. Persist autosave timing, zoom speed, fit-on-open, and canvas deletion confirmation preferences.

## Definition of done

A release is complete when a user can create both document kinds, edit their intended content, undo mistakes, close and reopen without loss, and run the self-contained `.exe` without an installed .NET runtime. Plane users must be able to expand and delete tiles, zoom and pan, place text and shape objects inside and outside canvases, and draw only on canvases. Notebook users must never need to understand the plane model.

