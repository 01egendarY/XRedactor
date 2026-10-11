# Changelog

Changes are grouped by application version, with the newest version first. Updates within the current version are added to its existing section.

## 1.0

### Added

- Two document modes: a single-page notebook for text and handwriting, and a spatial plane with connected canvases, shapes, and note sheets.
- Individual `.xred` document files in the `Notebooks` folder beside the executable, with automatic and manual saving.
- Blank, grid, and ruled paper, plus notebooks with top or left spiral binding.
- Font sizes measured in cells for grid paper, lines for ruled paper, and points for blank and spiral paper. Editable size fields and increment/decrement buttons.
- Text formatting, pen color and thickness controls, and a point eraser.
- A horizontal shape toolbar with fill and outline controls, plus paper, font family, and font size controls for note sheets.
- Adjacent canvas expansion, object movement and resizing, and a dedicated canvas deletion tool.
- A collapsible minimap showing canvases, objects, ink, and the viewport, with click and drag navigation.
- Document and filename renaming, and a resizable document sidebar.
- Five interface themes and a system theme, configurable autosave, zoom speed, fit-on-open, and canvas deletion confirmation.
- Keyboard shortcuts for saving, undo/redo, text formatting, and zoom.
- A paper-and-red-stroke application icon for executables, the application window, and the taskbar.
- Automated storage and in-process WPF editor regression checks.

### Changed

- Standardized repository documentation in English while retaining the Russian application interface.
- Moved version-specific release notes to this changelog and retained version-only commit messages.
- Updated the implementation specification to distinguish current behavior from planned features.

### Fixed

- A startup crash caused by interface theme resources.
- Unintended viewport movement when changing text formatting.
- Undo ordering across text edits, ink strokes, and object changes, with typing grouped by word boundaries and idle pauses.
- Undo for selected-text font size changes, and caret and viewport preservation when restoring editor state.
- Neighboring canvas creation and placement of add buttons outside canvas boundaries.
- Drawing outside canvases, unnecessary coordinate labels, and inline canvas deletion buttons.
- Viewport calculations during zoom and fixed workspace boundaries during plane expansion.
- Minimap navigation and viewport display.
- Placeholder behavior in empty text objects and unwanted initial text in note sheets.

### Validation

Regression checks cover document round-trips, Unicode, renaming, font sizes, typing groups, mixed-action undo, ink clipping, zoom anchors, and plane expansion. A complete manual mouse-and-keyboard pass in a live application window remains a separate validation step.
