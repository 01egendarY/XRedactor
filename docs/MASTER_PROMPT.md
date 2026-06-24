# XRedactor Master Implementation Prompt

You are building **XRedactor**, a local-first Windows desktop application for visual notes. Treat this document as the authoritative product and engineering brief. Implement the product incrementally, but every increment must remain buildable, testable, and preserve existing user documents.

## Product intent

On every New Document action, ask the user to choose between two deliberately different experiences:

1. **Notebook** is a focused single-page document for a daily plan, checklist, journal entry, or short note. It contains exactly one paper/notebook surface. The user can type formatted text or draw freehand on it. Do not show shapes, tiles, minimap, or spatial-editor controls in this mode.
2. **Plane** is a spatial workspace for long-form notes, diagrams, handwriting, shapes, and multiple connected canvases. It supports zooming, panning, objects outside canvases, a minimap, and expansion by adjacent canvas tiles.

The distinction must stay visible in the UI and data model. Do not turn Notebook into a restricted Plane through a confusing set of hidden controls.

## Platform and repository

- Target Windows 10/11 using C#, WPF, and .NET 10.
- Avoid external dependencies unless they remove substantial complexity and are approved.
- Keep source under `src/XRedactor` and documentation under `docs`.
- Publish a self-contained `win-x64` build to `publish/XRedactor`.
- Commit source and docs. Do not commit `bin`, `obj`, published binaries, or user documents.

## Branding and storage

- Working product name: **XRedactor**.
- Use one versioned JSON document per user file with the `.xred` extension.
- Create `Notebooks` beside the running executable and store documents there.
- Acknowledge that a read-only installation directory cannot support portable storage; show a clear error instead of silently writing elsewhere.
- Save through a temporary file followed by atomic replacement.
- Autosave after meaningful edits with debouncing, and save on explicit Save and graceful exit.
- Never lose unknown future fields when migrations are later introduced; version the schema from the first release.

## Start screen

- Show existing documents with name, kind, and modification date.
- Provide Create, Open, Rename, and Delete actions; deletion requires confirmation.
- On Create, first choose Notebook or Plane, then show only relevant options.
- Use clean Russian UI copy initially while keeping code identifiers in English.

## Notebook mode

- Offer templates: blank sheet, lined sheet, spiral on top, and spiral on bottom.
- Display only navigation, document name, Save, text/draw mode, minimal text formatting, and pen controls.
- Text formatting: font size, color, bold, italic, and underline.
- Drawing: freehand pen color and thickness plus eraser.
- Typed text and ink coexist on the same paper without changing each other's colors.
- Keep the page readable, centered, and scrollable on small windows.
- Support undo/redo for text and ink.

## Plane mode

- Start with one configurable canvas tile.
- Before a second tile exists, allow changing tile width and height.
- Show an unobtrusive plus control on every free orthogonal edge of every tile.
- Adding a tile places an equal-sized tile in that grid cell and permanently locks tile dimensions for the document.
- Allow tile deletion after confirmation when it contains or visually intersects content.
- Allow objects and ink outside tile bounds.
- Wheel over the workspace zooms around the pointer. `Shift+Wheel` scrolls horizontally. Middle-drag or `Space+drag` pans.
- Support Select, Pen, Eraser, Text, Rectangle, Ellipse, Line/Arrow, and large note-sheet blocks.
- Objects support move, resize, duplicate, delete, z-order, fill, border, and independent text color.
- Text inside objects supports size, color, bold, italic, underline, and basic alignment.
- Provide canvas and per-object fill without altering contained text.
- Add a collapsible minimap showing tile extents, object bounds, and the current viewport.
- Provide `Ctrl+0` for 100% zoom and `Ctrl+1` for fit-all.

## Editing behavior

- Implement `Ctrl+Z`, `Ctrl+Shift+Z`/`Ctrl+Y`, `Ctrl+S`, `Ctrl+C`, `Ctrl+V`, `Ctrl+D`, and `Delete` where applicable.
- Coalesce continuous typing, drawing, and dragging into sensible undo transactions.
- Use stable IDs for documents, tiles, elements, and strokes.
- Keep world coordinates independent of camera coordinates and DPI.
- Do not couple zoom to current content size.

## Export and resilience

- Export the selected canvas/tile region to PNG when practical without destabilizing editing.
- Recover cleanly from malformed files: keep the original file untouched and report the problem.
- Validate dimensions, zoom ranges, colors, and collection sizes while loading.
- Add focused tests for serialization, coordinate conversion, tile-neighbor rules, and undo history.

## UX and visual direction

- Prefer a quiet editorial interface: warm paper surfaces, dark graphite navigation, restrained red accent, generous spacing.
- Avoid a generic enterprise dashboard appearance.
- Notebook mode should feel almost tool-free; Plane mode may be denser but controls must remain collapsible.
- Use icons only with tooltips and accessible names. Preserve keyboard access and visible focus states.

## Definition of done

A release is complete when a user can create both document kinds, edit their intended content, undo mistakes, close and reopen without loss, and run the self-contained `.exe` without an installed .NET runtime. Plane users must be able to expand and delete tiles, zoom and pan, and place content both inside and outside canvases. Notebook users must never need to understand the plane model.

