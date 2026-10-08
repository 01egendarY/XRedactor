using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using XRedactor.Models;

namespace XRedactor;

public partial class MainWindow
{
    private double WorldOrigin = 6000;
    private string _planeTool = "Select";
    private CanvasElementData? _selectedElement;
    private FrameworkElement? _selectedElementView;
    private bool _isDraggingElement;
    private bool _elementDragSnapshotCaptured;
    private Point _elementDragStart;
    private Point _elementOriginalPosition;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartHorizontal;
    private double _panStartVertical;
    private bool _minimapExpanded = true;
    private bool _isSyncingPlaneControls;
    private double _minimapMinX;
    private double _minimapMinY;
    private double _minimapScale = 1;
    private Guid? _activeTextEditSnapshotElementId;

    private void ConfigurePlane(PlaneData plane, bool fitOnOpen)
    {
        WorldOrigin = plane.WorldOrigin;
        _selectedElement = null;
        _selectedElementView = null;
        _activeTextEditSnapshotElementId = null;
        _elementDragSnapshotCaptured = false;
        _inkSnapshotCaptured = false;
        SelectComboBoxItemByContent(PlaneFontSizeBox, "18");
        PlaneScale.ScaleX = plane.Zoom;
        PlaneScale.ScaleY = plane.Zoom;
        ZoomText.Text = $"{plane.Zoom:P0}";
        PlaneInkCanvas.Strokes = ToStrokeCollection(plane.Strokes);
        UpdatePlaneDrawingAttributes();
        SetPlaneTool("Select");
        RenderTiles();
        RenderElements();
        RenderMinimap();
        UpdatePlaneFormattingButtons();

        if (!fitOnOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                PlaneScrollViewer.ScrollToHorizontalOffset(plane.HorizontalOffset);
                PlaneScrollViewer.ScrollToVerticalOffset(plane.VerticalOffset);
                RenderMinimap();
            }, DispatcherPriority.Loaded);
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            FitAllTiles(false);
        }, DispatcherPriority.Loaded);
    }

    private void CapturePlane(PlaneData plane)
    {
        plane.Zoom = PlaneScale.ScaleX;
        plane.HorizontalOffset = PlaneScrollViewer.HorizontalOffset;
        plane.VerticalOffset = PlaneScrollViewer.VerticalOffset;
        plane.Strokes = FromStrokeCollection(PlaneInkCanvas.Strokes);
    }

    private void PlaneTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var tool = button.Tag?.ToString() ?? "Select";
        if (tool is "Text" or "Rectangle" or "Ellipse" or "NoteSheet")
        {
            AddElement(tool);
            SetPlaneTool("Select");
        }
        else
        {
            SetPlaneTool(tool);
        }
    }

    private void SetPlaneTool(string tool)
    {
        _planeTool = tool;
        var inkMode = tool is "Ink" or "Erase";
        PlaneInkCanvas.IsHitTestVisible = inkMode;
        PlaneInkCanvas.EditingMode = tool switch
        {
            "Ink" => InkCanvasEditingMode.Ink,
            "Erase" => InkCanvasEditingMode.EraseByPoint,
            _ => InkCanvasEditingMode.None
        };
        ElementLayer.IsHitTestVisible = !inkMode;
        TileLayer.IsHitTestVisible = !inkMode;
        PlaneWorkspace.Cursor = tool == "Ink" ? Cursors.Pen : tool == "Erase" ? Cursors.Cross : Cursors.Arrow;
        UpdatePlaneDrawingAttributes();
        UpdatePlaneToolButtons();
    }

    private void RenderTiles()
    {
        TileLayer.Children.Clear();
        var plane = _currentDocument?.Plane;
        if (plane is null)
        {
            return;
        }

        var occupied = plane.Tiles.Select(tile => (tile.Column, tile.Row)).ToHashSet();
        var offered = new HashSet<(int, int)>();
        UpdatePlaneExtents(plane);
        foreach (var tile in plane.Tiles)
        {
            var left = WorldOrigin + tile.Column * plane.TileWidth;
            var top = WorldOrigin + tile.Row * plane.TileHeight;
            var border = new Border
            {
                Width = plane.TileWidth,
                Height = plane.TileHeight,
                Background = BrushFromHex(tile.Background),
                BorderBrush = BrushFromHex("#FFB7AFA4"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            Canvas.SetLeft(border, left);
            Canvas.SetTop(border, top);
            TileLayer.Children.Add(border);

            border.MouseLeftButtonDown += (_, e) =>
            {
                if (_planeTool == "DeleteCanvas")
                {
                    DeleteTile(tile);
                    e.Handled = true;
                }
            };

            AddNeighborButtonIfFree(tile.Column - 1, tile.Row, left - 46, top + plane.TileHeight / 2 - 17, occupied, offered);
            AddNeighborButtonIfFree(tile.Column + 1, tile.Row, left + plane.TileWidth + 12, top + plane.TileHeight / 2 - 17, occupied, offered);
            AddNeighborButtonIfFree(tile.Column, tile.Row - 1, left + plane.TileWidth / 2 - 17, top - 46, occupied, offered);
            AddNeighborButtonIfFree(tile.Column, tile.Row + 1, left + plane.TileWidth / 2 - 17, top + plane.TileHeight + 12, occupied, offered);
        }
        UpdateInkClip();
    }

    private void AddNeighborButtonIfFree(
        int column,
        int row,
        double left,
        double top,
        HashSet<(int, int)> occupied,
        HashSet<(int, int)> offered)
    {
        if (occupied.Contains((column, row)) || !offered.Add((column, row)))
        {
            return;
        }

        var button = CreateTileActionButton("+", "Добавить соседний холст");
        button.Background = (Brush)Application.Current.Resources["Accent"];
        button.Click += (_, _) => AddTile(column, row);
        Canvas.SetLeft(button, left);
        Canvas.SetTop(button, top);
        TileLayer.Children.Add(button);
    }

    private static Button CreateTileActionButton(string content, string tooltip)
    {
        return new Button
        {
            Content = content,
            ToolTip = tooltip,
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
    }

    private void AddTile(int column, int row)
    {
        var plane = _currentDocument?.Plane;
        if (plane is null || plane.Tiles.Any(tile => tile.Column == column && tile.Row == row))
        {
            return;
        }

        PushUndoSnapshot();
        plane.Tiles.Add(new CanvasTileData { Column = column, Row = row });
        RenderTiles();
        RenderMinimap();
        FitAllTiles(false);
        MarkDirty();
    }

    private void DeleteTile(CanvasTileData tile)
    {
        var plane = _currentDocument?.Plane;
        if (plane is null || plane.Tiles.Count <= 1)
        {
            return;
        }

        var intersectsContent =
            plane.Elements.Any(element => ElementIntersectsTile(element, tile, plane)) ||
            PlaneInkCanvas.Strokes.Any(stroke => StrokeIntersectsTile(stroke, tile, plane));
        var question = intersectsContent
            ? "На этом холсте есть объекты. Удалить только холст и оставить объекты на плоскости?"
            : "Удалить этот холст?";
        if (_confirmCanvasDelete && MessageBox.Show(question, "Удаление холста", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        PushUndoSnapshot();
        plane.Tiles.Remove(tile);
        RenderTiles();
        ClipCollectedInk();
        RenderMinimap();
        MarkDirty();
    }

    private static bool ElementIntersectsTile(CanvasElementData element, CanvasTileData tile, PlaneData plane)
    {
        var tileRect = new Rect(tile.Column * plane.TileWidth, tile.Row * plane.TileHeight, plane.TileWidth, plane.TileHeight);
        return tileRect.IntersectsWith(new Rect(element.X, element.Y, element.Width, element.Height));
    }

    private bool StrokeIntersectsTile(Stroke stroke, CanvasTileData tile, PlaneData plane)
    {
        var tileRect = new Rect(
            WorldOrigin + tile.Column * plane.TileWidth,
            WorldOrigin + tile.Row * plane.TileHeight,
            plane.TileWidth,
            plane.TileHeight);
        return tileRect.IntersectsWith(stroke.GetBounds());
    }

    private void AddElement(string tool)
    {
        var plane = _currentDocument?.Plane;
        if (plane is null)
        {
            return;
        }

        PushUndoSnapshot();
        var centerX = (PlaneScrollViewer.HorizontalOffset + PlaneScrollViewer.ViewportWidth / 2) / PlaneScale.ScaleX - WorldOrigin;
        var centerY = (PlaneScrollViewer.VerticalOffset + PlaneScrollViewer.ViewportHeight / 2) / PlaneScale.ScaleY - WorldOrigin;
        var kind = Enum.Parse<ElementKind>(tool);
        var element = new CanvasElementData
        {
            Kind = kind,
            X = centerX - (kind == ElementKind.NoteSheet ? 210 : 130),
            Y = centerY - (kind == ElementKind.NoteSheet ? 260 : kind == ElementKind.Text ? 45 : 85),
            Width = kind == ElementKind.NoteSheet ? 420 : 260,
            Height = kind == ElementKind.NoteSheet ? 520 : kind == ElementKind.Text ? 90 : 170,
            Fill = kind switch
            {
                ElementKind.Text => "#00FFFFFF",
                ElementKind.NoteSheet => "#FFFFF9EF",
                _ => GetSelectedComboTag(ShapeFillBox, GetSelectedComboTag(PlaneFillColorBox, "#FFFFF2B8"))
            },
            Stroke = kind == ElementKind.Text ? "#0034313A" : GetSelectedComboTag(ShapeOutlineColorBox, "#FF34313A"),
            StrokeThickness = kind == ElementKind.NoteSheet ? 2 : ShapeOutlineBox.IsChecked == true && double.TryParse(ShapeOutlineWidthBox.Text, out var outlineWidth) && double.IsFinite(outlineWidth) ? Math.Clamp(outlineWidth, 0, 40) : 0,
            Text = "",
            TextColor = GetSelectedComboTag(PlaneColorBox, "#FF29262D"),
            FontSize = kind == ElementKind.NoteSheet ? FontSizing.GetPixelFontSize(NotebookTemplate.Lined, 2) : FontSizing.GetPixelFontSize(NotebookTemplate.Blank, GetSelectedComboNumber(PlaneFontSizeBox, 18)),
            ZIndex = plane.Elements.Count
        };
        plane.Elements.Add(element);
        RenderElements();
        SelectElement(element);
        RenderMinimap();
        MarkDirty();
    }

    private void RenderElements()
    {
        ElementLayer.Children.Clear();
        _selectedElementView = null;
        var plane = _currentDocument?.Plane;
        if (plane is null)
        {
            return;
        }

        foreach (var element in plane.Elements.OrderBy(item => item.ZIndex))
        {
            var frame = CreateElementView(element);
            Canvas.SetLeft(frame, WorldOrigin + element.X);
            Canvas.SetTop(frame, WorldOrigin + element.Y);
            Panel.SetZIndex(frame, element.ZIndex);
            ElementLayer.Children.Add(frame);
            if (_selectedElement?.Id == element.Id)
            {
                _selectedElementView = frame;
                SetElementSelectionVisual(frame, true);
            }
        }
    }

    private FrameworkElement CreateElementView(CanvasElementData element)
    {
        var frame = new Border
        {
            Width = element.Width,
            Height = element.Height,
            Tag = element,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(5),
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll
        };
        var content = new Grid();

        Shape shape = element.Kind == ElementKind.Ellipse ? new Ellipse() : new Rectangle { RadiusX = 5, RadiusY = 5 };
        shape.Fill = BrushFromHex(element.Fill);
        shape.Stroke = BrushFromHex(element.Stroke);
        shape.StrokeThickness = element.StrokeThickness;
        if (element.Kind == ElementKind.NoteSheet)
        {
            shape.Fill = element.Template == NotebookTemplate.Lined ? CreateGridPaperBrush(element.Fill) : element.Template == NotebookTemplate.Ruled ? CreateRuledPaperBrush(element.Fill) : BrushFromHex(element.Fill);
        }
        content.Children.Add(shape);
        if (element.Kind == ElementKind.NoteSheet && element.Template is NotebookTemplate.SpiralTop or NotebookTemplate.SpiralLeft or NotebookTemplate.SpiralBottom)
            content.Children.Add(CreateNoteSpiral(element));

        var editor = new TextBox
        {
            Text = element.Text,
            Tag = element,
            Background = Brushes.Transparent,
            Foreground = BrushFromHex(element.TextColor),
            BorderThickness = new Thickness(0),
            Padding = GetElementTextPadding(element),
            FontSize = element.FontSize,
            FontFamily = new FontFamily(element.FontFamily),
            FontWeight = element.IsBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = element.IsItalic ? FontStyles.Italic : FontStyles.Normal,
            TextDecorations = element.IsUnderlined ? TextDecorations.Underline : null,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            Cursor = Cursors.Arrow
        };
        editor.TextChanged += ElementText_TextChanged;
        editor.PreviewKeyDown += ElementEditor_PreviewKeyDown;
        editor.PreviewTextInput += ElementEditor_PreviewTextInput;
        editor.LostKeyboardFocus += (_, _) =>
        {
            if (IsInsideControlPanel(Keyboard.FocusedElement as DependencyObject)) return;
            editor.IsReadOnly = true;
            editor.Cursor = Cursors.Arrow;
            _activeTextEditSnapshotElementId = null;
            UpdateElementPlaceholder(editor);
        };
        DataObject.AddPastingHandler(editor, ElementEditor_Pasting);
        content.Children.Add(editor);

        var placeholder = new TextBlock
        {
            Tag = "Placeholder",
            Text = GetElementPlaceholder(element),
            Foreground = BrushFromHex("#88726D73"),
            Padding = GetElementTextPadding(element),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
            Visibility = string.IsNullOrWhiteSpace(element.Text) ? Visibility.Visible : Visibility.Collapsed
        };
        content.Children.Add(placeholder);

        var resize = new Thumb
        {
            Tag = element,
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Background = (Brush)Application.Current.Resources["Accent"],
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Opacity = 0.85,
            Visibility = _selectedElement?.Id == element.Id ? Visibility.Visible : Visibility.Collapsed
        };
        resize.DragStarted += ElementResize_DragStarted;
        resize.DragDelta += ElementResize_DragDelta;
        resize.DragCompleted += ElementResize_DragCompleted;
        content.Children.Add(resize);

        frame.Child = content;
        frame.PreviewMouseLeftButtonDown += ElementView_MouseLeftButtonDown;
        frame.MouseMove += ElementView_MouseMove;
        frame.MouseLeftButtonUp += ElementView_MouseLeftButtonUp;
        return frame;
    }

    private void ElementView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement frame || frame.Tag is not CanvasElementData element)
        {
            return;
        }

        if (IsInsideResizeThumb(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (IsInsideEditableTextBox(e.OriginalSource as DependencyObject))
        {
            return;
        }

        SelectElement(element, frame);
        if (e.ClickCount == 2 && FindTextEditor(frame) is { } editor)
        {
            BeginElementTextEdit(editor, element);
            e.Handled = true;
            return;
        }

        _isDraggingElement = true;
        _elementDragSnapshotCaptured = false;
        _elementDragStart = e.GetPosition(ElementLayer);
        _elementOriginalPosition = new Point(element.X, element.Y);
        frame.CaptureMouse();
        e.Handled = true;
    }

    private void BeginElementTextEdit(TextBox editor, CanvasElementData element)
    {
        editor.IsReadOnly = false;
        editor.Cursor = Cursors.IBeam;
        editor.Focus();
        Keyboard.Focus(editor);
        editor.CaretIndex = editor.Text.Length;
        UpdateElementPlaceholder(editor);
    }

    private void ElementView_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingElement || e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement frame || frame.Tag is not CanvasElementData element)
        {
            return;
        }

        var current = e.GetPosition(ElementLayer);
        var deltaX = current.X - _elementDragStart.X;
        var deltaY = current.Y - _elementDragStart.Y;
        if (!_elementDragSnapshotCaptured)
        {
            if (Math.Abs(deltaX) < 2 && Math.Abs(deltaY) < 2)
            {
                return;
            }

            PushUndoSnapshot();
            _elementDragSnapshotCaptured = true;
        }

        element.X = _elementOriginalPosition.X + deltaX;
        element.Y = _elementOriginalPosition.Y + deltaY;
        Canvas.SetLeft(frame, WorldOrigin + element.X);
        Canvas.SetTop(frame, WorldOrigin + element.Y);
        MarkDirty();
    }

    private void ElementView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingElement || sender is not FrameworkElement frame)
        {
            return;
        }

        _isDraggingElement = false;
        _elementDragSnapshotCaptured = false;
        frame.ReleaseMouseCapture();
        RenderMinimap();
        e.Handled = true;
    }

    private static TextBox? FindTextEditor(FrameworkElement frame)
    {
        if (frame is Border { Child: Grid grid })
        {
            return grid.Children.OfType<TextBox>().FirstOrDefault();
        }
        return null;
    }

    private void ElementText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || sender is not TextBox { Tag: CanvasElementData element } editor)
        {
            return;
        }

        element.Text = editor.Text;
        UpdateElementPlaceholder(editor);
        MarkDirty();
    }

    private void ElementEditor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox { Tag: CanvasElementData element } editor && !editor.IsReadOnly)
        {
            EnsureElementTextEditSnapshot(element);
        }
    }

    private void ElementEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: CanvasElementData element, IsReadOnly: false })
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
            _activeTextEditSnapshotElementId = null;
        var changesText =
            e.Key is Key.Back or Key.Delete or Key.Enter or Key.Space or Key.Tab ||
            control && e.Key is Key.V or Key.X;

        if (changesText)
        {
            if (e.Key is Key.Back or Key.Delete or Key.Enter or Key.Space or Key.Tab || control && e.Key is Key.V or Key.X)
                _activeTextEditSnapshotElementId = null;
            EnsureElementTextEditSnapshot(element);
        }
    }

    private void ElementEditor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is TextBox { Tag: CanvasElementData element, IsReadOnly: false })
        {
            EnsureElementTextEditSnapshot(element);
        }
    }

    private void EnsureElementTextEditSnapshot(CanvasElementData element)
    {
        if (_isLoading)
        {
            return;
        }
        if (_activeTextEditSnapshotElementId != element.Id || DateTime.UtcNow - _lastPlaneTextEdit > TimeSpan.FromMilliseconds(700))
            PushUndoSnapshot();
        _lastPlaneTextEdit = DateTime.UtcNow;
        _activeTextEditSnapshotElementId = element.Id;
    }

    private void ElementResize_DragStarted(object sender, DragStartedEventArgs e)
    {
        PushUndoSnapshot();
        e.Handled = true;
    }

    private void ElementResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: CanvasElementData element })
        {
            return;
        }

        element.Width = Math.Max(element.Kind == ElementKind.Text ? 90 : 120, element.Width + e.HorizontalChange);
        element.Height = Math.Max(element.Kind == ElementKind.Text ? 50 : 90, element.Height + e.VerticalChange);

        if (_selectedElementView is not null)
        {
            _selectedElementView.Width = element.Width;
            _selectedElementView.Height = element.Height;
            if (FindTextEditor(_selectedElementView) is { } editor)
            {
                editor.Padding = GetElementTextPadding(element);
            }

            foreach (var placeholder in FindVisualChildren<TextBlock>(_selectedElementView)
                         .Where(text => string.Equals(text.Tag?.ToString(), "Placeholder", StringComparison.Ordinal)))
            {
                placeholder.Padding = GetElementTextPadding(element);
            }
        }

        MarkDirty();
        e.Handled = true;
    }

    private void ElementResize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        RenderMinimap();
        e.Handled = true;
    }

    private static Thickness GetElementTextPadding(CanvasElementData element)
    {
        return element.Kind switch
        {
            ElementKind.NoteSheet => element.Template == NotebookTemplate.SpiralTop ? new Thickness(32, 60, 28, 28) : element.Template is NotebookTemplate.SpiralLeft or NotebookTemplate.SpiralBottom ? new Thickness(60, 34, 28, 28) : new Thickness(32, 34, 28, 28),
            ElementKind.Ellipse => new Thickness(
                Math.Max(24, element.Width * 0.18),
                Math.Max(22, element.Height * 0.2),
                Math.Max(24, element.Width * 0.18),
                Math.Max(22, element.Height * 0.2)),
            _ => new Thickness(14)
        };
    }

    private static string GetElementPlaceholder(CanvasElementData element)
    {
        return element.Kind switch
        {
            ElementKind.Text => "Двойной щелчок для текста",
            ElementKind.NoteSheet => "Начните писать",
            _ => ""
        };
    }

    private static void UpdateElementPlaceholder(TextBox editor)
    {
        if (VisualTreeHelper.GetParent(editor) is not Panel panel)
        {
            return;
        }

        foreach (var placeholder in panel.Children.OfType<TextBlock>()
                     .Where(text => string.Equals(text.Tag?.ToString(), "Placeholder", StringComparison.Ordinal)))
        {
            placeholder.Visibility = string.IsNullOrWhiteSpace(editor.Text) && editor.IsReadOnly
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void SelectElement(CanvasElementData element, FrameworkElement? view = null)
    {
        if (_selectedElementView is not null)
        {
            SetElementSelectionVisual(_selectedElementView, false);
        }
        _selectedElement = element;
        _selectedElementView = view ?? ElementLayer.Children.OfType<FrameworkElement>()
            .FirstOrDefault(child => child.Tag is CanvasElementData data && data.Id == element.Id);
        if (_selectedElementView is not null)
        {
            SetElementSelectionVisual(_selectedElementView, true);
        }
        SyncPlaneControlsFromElement(element);
        UpdatePlaneFormattingButtons();
    }

    private static void SetElementSelectionVisual(FrameworkElement view, bool selected)
    {
        if (view is Border border)
        {
            border.BorderBrush = selected ? (Brush)Application.Current.Resources["Accent"] : Brushes.Transparent;
            foreach (var thumb in FindVisualChildren<Thumb>(border))
            {
                thumb.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void DeleteSelectedElement()
    {
        var plane = _currentDocument?.Plane;
        if (plane is null || _selectedElement is null)
        {
            return;
        }

        PushUndoSnapshot();
        plane.Elements.RemoveAll(item => item.Id == _selectedElement.Id);
        _selectedElement = null;
        RenderElements();
        RenderMinimap();
        UpdatePlaneFormattingButtons();
        MarkDirty();
    }

    private void SyncPlaneControlsFromElement(CanvasElementData element)
    {
        if (PlaneFontSizeBox is null || PlaneColorBox is null || PlaneFillColorBox is null)
        {
            return;
        }

        _isSyncingPlaneControls = true;
        try
        {
            SetFontSizeOptions(PlaneFontSizeBox, element.Kind == ElementKind.NoteSheet ? element.Template : NotebookTemplate.Blank);
            PlaneFontSizeBox.Text = FontSizing.GetDisplayFontSize(element.Kind == ElementKind.NoteSheet ? element.Template : NotebookTemplate.Blank, element.FontSize, element.FontFamily).ToString("0.##");
            PlaneFontSizeLabel.Text = element.Kind == ElementKind.NoteSheet && element.Template == NotebookTemplate.Lined ? "Высота текста, клеток" : element.Kind == ElementKind.NoteSheet && element.Template == NotebookTemplate.Ruled ? "Высота текста, линий" : "Размер текста, пт";
            SyncContextToolbar(element);
            SelectComboBoxItemByTag(PlaneColorBox, element.TextColor);
            if (element.Kind != ElementKind.Text)
            {
                SelectComboBoxItemByTag(PlaneFillColorBox, element.Fill);
            }
        }
        finally
        {
            _isSyncingPlaneControls = false;
        }

        UpdatePlaneDrawingAttributes();
        UpdatePlaneFormattingButtons();
    }

    private void PlaneBold_Click(object sender, RoutedEventArgs e)
    {
        ApplyPlaneTextFormatting(element => element.IsBold = !element.IsBold);
    }

    private void PlaneItalic_Click(object sender, RoutedEventArgs e)
    {
        ApplyPlaneTextFormatting(element => element.IsItalic = !element.IsItalic);
    }

    private void PlaneUnderline_Click(object sender, RoutedEventArgs e)
    {
        ApplyPlaneTextFormatting(element => element.IsUnderlined = !element.IsUnderlined);
    }

    private void ApplyPlaneTextFormatting(Action<CanvasElementData> update)
    {
        if (_selectedElement is null)
        {
            return;
        }

        var restoreEditing = TryCaptureActiveElementTextEdit(out var caretIndex);
        PushUndoSnapshot();
        update(_selectedElement);
        RenderElements();
        SelectElement(_selectedElement);
        RestoreActiveElementTextEdit(restoreEditing, caretIndex);
        MarkDirty();
    }

    private bool TryCaptureActiveElementTextEdit(out int caretIndex)
    {
        caretIndex = 0;
        if (_selectedElementView is null || FindTextEditor(_selectedElementView) is not { IsReadOnly: false } editor)
        {
            return false;
        }

        caretIndex = editor.CaretIndex;
        return true;
    }

    private void RestoreActiveElementTextEdit(bool shouldRestore, int caretIndex)
    {
        if (!shouldRestore || _selectedElementView is null || FindTextEditor(_selectedElementView) is not { } editor)
        {
            return;
        }

        editor.IsReadOnly = false;
        editor.Cursor = Cursors.IBeam;
        editor.Focus();
        Keyboard.Focus(editor);
        editor.CaretIndex = Math.Clamp(caretIndex, 0, editor.Text.Length);
        UpdateElementPlaceholder(editor);
    }

    private void UpdatePlaneFormattingButtons()
    {
        if (PlaneBoldButton is null || PlaneItalicButton is null || PlaneUnderlineButton is null)
        {
            return;
        }

        SetButtonActive(PlaneBoldButton, _selectedElement?.IsBold == true);
        SetButtonActive(PlaneItalicButton, _selectedElement?.IsItalic == true);
        SetButtonActive(PlaneUnderlineButton, _selectedElement?.IsUnderlined == true);
    }

    private void PlaneFontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _isSyncingPlaneControls)
        {
            return;
        }

        if (_selectedElement is not null && PlaneFontSizeBox.SelectedItem is ComboBoxItem selected)
        {
            PlaneFontSizeBox.Text = selected.Content?.ToString() ?? "18";
            PlaneFontSize_Commit(sender, e);
        }
    }

    private void PlaneColorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePlaneDrawingAttributes();
        if (_isLoading || _isSyncingPlaneControls || _selectedElement is null || _planeTool is "Ink" or "Erase")
        {
            return;
        }

        var restoreEditing = TryCaptureActiveElementTextEdit(out var caretIndex);
        PushUndoSnapshot();
        _selectedElement.TextColor = GetSelectedComboTag(PlaneColorBox, "#FF29262D");
        RenderElements();
        SelectElement(_selectedElement);
        RestoreActiveElementTextEdit(restoreEditing, caretIndex);
        MarkDirty();
    }

    private void PlaneFillColorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _isSyncingPlaneControls || _selectedElement is null || _selectedElement.Kind == ElementKind.Text)
        {
            return;
        }

        var restoreEditing = TryCaptureActiveElementTextEdit(out var caretIndex);
        PushUndoSnapshot();
        _selectedElement.Fill = GetSelectedComboTag(PlaneFillColorBox, "#FFFFF2B8");
        RenderElements();
        SelectElement(_selectedElement);
        RestoreActiveElementTextEdit(restoreEditing, caretIndex);
        MarkDirty();
    }

    private void PlanePenSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePlaneDrawingAttributes();
    }

    private void UpdatePlaneDrawingAttributes()
    {
        if (PlaneInkCanvas is null)
        {
            return;
        }

        var color = GetSelectedComboTag(PlaneColorBox, "#FF29262D");
        var size = PlanePenSize?.Value ?? 3;
        var eraserSize = Math.Clamp(size * 5, 12, 56);
        PlaneInkCanvas.EraserShape = new EllipseStylusShape(eraserSize, eraserSize);
        PlaneInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString(color),
            Width = size,
            Height = size,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse
        };
    }

    private void UpdatePlaneToolButtons()
    {
        foreach (var button in FindVisualChildren<Button>(PlaneToolbar))
        {
            if (button.Tag is not string tag)
            {
                continue;
            }

            SetButtonActive(button, tag == _planeTool);
        }
    }

    private static string GetSelectedComboTag(ComboBox comboBox, string fallback)
    {
        return comboBox?.SelectedItem is ComboBoxItem { Tag: string value } ? value : fallback;
    }

    private static double GetSelectedComboNumber(ComboBox comboBox, double fallback)
    {
        return double.TryParse(comboBox?.Text, out var value) && double.IsFinite(value) && value > 0
            ? value
            : fallback;
    }

    private static void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void PlaneWorld_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (IsInsideButton(source))
        {
            return;
        }

        if (e.OriginalSource == PlaneWorld || e.OriginalSource == ElementLayer || IsInsideTileLayer(source))
        {
            ClearSelectedElement();
        }
    }

    private void ClearSelectedElement()
    {
        if (_selectedElementView is not null)
        {
            SetElementSelectionVisual(_selectedElementView, false);
        }

        _selectedElement = null;
        _selectedElementView = null;
        ContextToolbar.Visibility = Visibility.Collapsed;
        UpdatePlaneFormattingButtons();
    }

    private void PlaneWorkspace_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_planeTool == "DeleteCanvas" && e.ChangedButton == MouseButton.Left &&
            !IsInsideButton(e.OriginalSource as DependencyObject) && !IsInsideControlPanel(e.OriginalSource as DependencyObject) && !IsInsideMinimap(e.OriginalSource as DependencyObject))
        {
            if (_currentDocument?.Plane is { } plane)
            {
                var point = e.GetPosition(TileLayer);
                var tile = plane.Tiles.FirstOrDefault(item => new Rect(WorldOrigin + item.Column * plane.TileWidth, WorldOrigin + item.Row * plane.TileHeight, plane.TileWidth, plane.TileHeight).Contains(point));
                if (tile is not null) DeleteTile(tile);
            }
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Middle &&
            !(e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(PlaneWorkspace);
        _panStartHorizontal = PlaneScrollViewer.HorizontalOffset;
        _panStartVertical = PlaneScrollViewer.VerticalOffset;
        PlaneWorkspace.CaptureMouse();
        PlaneWorkspace.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void PlaneWorkspace_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var position = e.GetPosition(PlaneWorkspace);
        PlaneScrollViewer.ScrollToHorizontalOffset(_panStartHorizontal - (position.X - _panStart.X));
        PlaneScrollViewer.ScrollToVerticalOffset(_panStartVertical - (position.Y - _panStart.Y));
        RenderMinimap();
        e.Handled = true;
    }

    private void PlaneWorkspace_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        PlaneWorkspace.ReleaseMouseCapture();
        PlaneWorkspace.Cursor = _planeTool == "Ink" ? Cursors.Pen : Cursors.Arrow;
        RenderMinimap();
        MarkDirty();
        e.Handled = true;
    }

    private void PlaneWorkspace_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsInsideControlPanel(e.OriginalSource as DependencyObject) || IsInsideMinimap(e.OriginalSource as DependencyObject)) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            PlaneScrollViewer.ScrollToHorizontalOffset(PlaneScrollViewer.HorizontalOffset - e.Delta);
            RenderMinimap();
            MarkDirty();
            e.Handled = true;
            return;
        }

        var oldZoom = PlaneScale.ScaleX;
        var factor = ZoomSpeedMultiplier;
        var newZoom = Math.Clamp(oldZoom * Math.Pow(factor, e.Delta / 120.0), 0.001, 4.0);
        var pointer = e.GetPosition(PlaneScrollViewer);
        var worldX = (PlaneScrollViewer.HorizontalOffset + pointer.X) / oldZoom;
        var worldY = (PlaneScrollViewer.VerticalOffset + pointer.Y) / oldZoom;
        ApplyPlaneViewport(newZoom, new Point(worldX, worldY), pointer);
        MarkDirty();
        e.Handled = true;
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetPlaneZoom(1.0);

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitAllTiles();

    private void SetPlaneZoom(double zoom)
    {
        var centerX = (PlaneScrollViewer.HorizontalOffset + PlaneScrollViewer.ViewportWidth / 2) / PlaneScale.ScaleX;
        var centerY = (PlaneScrollViewer.VerticalOffset + PlaneScrollViewer.ViewportHeight / 2) / PlaneScale.ScaleY;
        ApplyPlaneViewport(zoom, new Point(centerX, centerY), new Point(PlaneScrollViewer.ViewportWidth / 2, PlaneScrollViewer.ViewportHeight / 2));
        MarkDirty();
    }

    private Rect GetPlaneContentBounds(PlaneData plane, bool includeInk)
    {
        var minX = plane.Tiles.Min(tile => tile.Column * plane.TileWidth);
        var minY = plane.Tiles.Min(tile => tile.Row * plane.TileHeight);
        var maxX = plane.Tiles.Max(tile => (tile.Column + 1) * plane.TileWidth);
        var maxY = plane.Tiles.Max(tile => (tile.Row + 1) * plane.TileHeight);

        foreach (var element in plane.Elements)
        {
            minX = Math.Min(minX, element.X);
            minY = Math.Min(minY, element.Y);
            maxX = Math.Max(maxX, element.X + element.Width);
            maxY = Math.Max(maxY, element.Y + element.Height);
        }

        if (includeInk && PlaneInkCanvas?.Strokes.Count > 0)
        {
            var inkBounds = PlaneInkCanvas.Strokes.GetBounds();
            if (!inkBounds.IsEmpty)
            {
                minX = Math.Min(minX, inkBounds.Left - WorldOrigin);
                minY = Math.Min(minY, inkBounds.Top - WorldOrigin);
                maxX = Math.Max(maxX, inkBounds.Right - WorldOrigin);
                maxY = Math.Max(maxY, inkBounds.Bottom - WorldOrigin);
            }
        }

        return new Rect(new Point(minX, minY), new Point(maxX, maxY));
    }

    private void FitAllTiles(bool markDirty = true)
    {
        var plane = _currentDocument?.Plane;
        if (plane is null || plane.Tiles.Count == 0)
        {
            return;
        }

        var bounds = GetPlaneContentBounds(plane, includeInk: true);
        var minX = WorldOrigin + bounds.Left;
        var minY = WorldOrigin + bounds.Top;
        var maxX = WorldOrigin + bounds.Right;
        var maxY = WorldOrigin + bounds.Bottom;
        var zoom = Math.Clamp(Math.Min(
            Math.Max(200, PlaneWorkspace.ActualWidth - 100) / (maxX - minX),
            Math.Max(200, PlaneWorkspace.ActualHeight - 100) / (maxY - minY)), 0.001, 2.0);
        ApplyPlaneViewport(zoom, new Point((minX + maxX) / 2, (minY + maxY) / 2), new Point(PlaneScrollViewer.ViewportWidth / 2, PlaneScrollViewer.ViewportHeight / 2));
        if (markDirty)
        {
            MarkDirty();
        }
    }

    private void PlaneInkCanvas_StrokesChanged(object sender, EventArgs e)
    {
        if (_isLoading || _clippingInk) return;
        if (e is InkCanvasStrokeCollectedEventArgs) ClipCollectedInk();
        _inkSnapshotCaptured = false;
        MarkDirty();
        RenderMinimap();
    }

    private void RenderMinimap()
    {
        MinimapCanvas.Children.Clear();
        var plane = _currentDocument?.Plane;
        if (plane is null || plane.Tiles.Count == 0 || !_minimapExpanded)
        {
            return;
        }

        var bounds = GetPlaneContentBounds(plane, includeInk: true);
        var minX = bounds.Left;
        var minY = bounds.Top;
        var maxX = bounds.Right;
        var maxY = bounds.Bottom;
        var width = Math.Max(1, MinimapCanvas.ActualWidth);
        var height = Math.Max(1, MinimapCanvas.ActualHeight);
        var scale = Math.Max(0.0001, Math.Min(Math.Max(1, width - 12) / Math.Max(maxX - minX, 1), Math.Max(1, height - 12) / Math.Max(maxY - minY, 1)));
        minX -= (width / scale - bounds.Width) / 2;
        minY -= (height / scale - bounds.Height) / 2;
        _minimapMinX = minX;
        _minimapMinY = minY;
        _minimapScale = scale;

        foreach (var tile in plane.Tiles)
        {
            var rectangle = new Rectangle
            {
                Width = plane.TileWidth * scale,
                Height = plane.TileHeight * scale,
                Fill = BrushFromHex("#FFEEE8DD"),
                Stroke = BrushFromHex("#FF8E8792"),
                StrokeThickness = 1
            };
            Canvas.SetLeft(rectangle, (tile.Column * plane.TileWidth - minX) * scale);
            Canvas.SetTop(rectangle, (tile.Row * plane.TileHeight - minY) * scale);
            MinimapCanvas.Children.Add(rectangle);
        }

        foreach (var stroke in PlaneInkCanvas.Strokes)
        {
            var points = new PointCollection();
            var stride = Math.Max(1, stroke.StylusPoints.Count / 64);
            for (var index = 0; index < stroke.StylusPoints.Count; index += stride)
                points.Add(new Point((stroke.StylusPoints[index].X - WorldOrigin - minX) * scale,
                    (stroke.StylusPoints[index].Y - WorldOrigin - minY) * scale));
            var last = stroke.StylusPoints.Last();
            points.Add(new Point((last.X - WorldOrigin - minX) * scale, (last.Y - WorldOrigin - minY) * scale));
            MinimapCanvas.Children.Add(new Polyline { Points = points, Stroke = new SolidColorBrush(stroke.DrawingAttributes.Color), StrokeThickness = Math.Max(1, stroke.DrawingAttributes.Width * scale), IsHitTestVisible = false });
        }

        foreach (var element in plane.Elements)
        {
            var marker = new Rectangle
            {
                Width = Math.Max(3, element.Width * scale),
                Height = Math.Max(3, element.Height * scale),
                Fill = BrushFromHex("#FFD85050"),
                Opacity = 0.8
            };
            Canvas.SetLeft(marker, (element.X - minX) * scale);
            Canvas.SetTop(marker, (element.Y - minY) * scale);
            MinimapCanvas.Children.Add(marker);
        }

        var zoom = PlaneScale.ScaleX;
        if (zoom > 0 && PlaneScrollViewer.ViewportWidth > 0 && PlaneScrollViewer.ViewportHeight > 0)
        {
            var viewportBounds = new Rect(
                (PlaneScrollViewer.HorizontalOffset / zoom - WorldOrigin - minX) * scale,
                (PlaneScrollViewer.VerticalOffset / zoom - WorldOrigin - minY) * scale,
                PlaneScrollViewer.ViewportWidth / zoom * scale,
                PlaneScrollViewer.ViewportHeight / zoom * scale);
            viewportBounds.Intersect(new Rect(2, 2, Math.Max(1, width - 4), Math.Max(1, height - 4)));
            if (viewportBounds.IsEmpty) return;
            var viewport = new Rectangle
            {
                Width = viewportBounds.Width,
                Height = viewportBounds.Height,
                Stroke = (Brush)Application.Current.Resources["Accent"],
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(viewport, viewportBounds.Left);
            Canvas.SetTop(viewport, viewportBounds.Top);
            MinimapCanvas.Children.Add(viewport);
        }
    }

    private void ToggleMinimap_Click(object sender, RoutedEventArgs e)
    {
        _minimapExpanded = !_minimapExpanded;
        Minimap.Height = _minimapExpanded ? 145 : 35;
        Minimap.Width = _minimapExpanded ? 210 : 70;
        MinimapCanvas.Visibility = _minimapExpanded ? Visibility.Visible : Visibility.Collapsed;
        MinimapToggleButton.Visibility = _minimapExpanded ? Visibility.Visible : Visibility.Collapsed;
        MinimapTitle.Margin = _minimapExpanded ? new Thickness(10, 7, 0, 0) : new Thickness(0);
        MinimapTitle.HorizontalAlignment = _minimapExpanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        MinimapTitle.VerticalAlignment = _minimapExpanded ? VerticalAlignment.Top : VerticalAlignment.Center;
        RenderMinimap();
    }

    private void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_minimapExpanded)
        {
            return;
        }

        ToggleMinimap_Click(sender, e);
        e.Handled = true;
    }

    private void MinimapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_minimapExpanded || _minimapScale <= 0)
        {
            return;
        }

        var point = e.GetPosition(MinimapCanvas);
        _minimapDragging = true;
        MinimapCanvas.CaptureMouse();
        CenterPlaneOnPoint(_minimapMinX + point.X / _minimapScale, _minimapMinY + point.Y / _minimapScale);
        e.Handled = true;
    }

    private void CenterPlaneOnPoint(double planeX, double planeY)
    {
        var zoom = PlaneScale.ScaleX;
        ApplyPlaneViewport(zoom, new Point(WorldOrigin + planeX, WorldOrigin + planeY), new Point(PlaneScrollViewer.ViewportWidth / 2, PlaneScrollViewer.ViewportHeight / 2));
        MarkDirty();
    }

    private static bool IsInsideResizeThumb(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsInsideEditableTextBox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox { IsReadOnly: false })
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private bool IsInsideTileLayer(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, TileLayer))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
