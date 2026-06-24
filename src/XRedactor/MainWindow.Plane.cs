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
    private const double WorldOrigin = 6000;
    private string _planeTool = "Select";
    private CanvasElementData? _selectedElement;
    private FrameworkElement? _selectedElementView;
    private bool _isDraggingElement;
    private Point _elementDragStart;
    private Point _elementOriginalPosition;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartHorizontal;
    private double _panStartVertical;
    private bool _minimapExpanded = true;

    private void ConfigurePlane(PlaneData plane)
    {
        PlaneScale.ScaleX = plane.Zoom;
        PlaneScale.ScaleY = plane.Zoom;
        ZoomText.Text = $"{plane.Zoom:P0}";
        PlaneInkCanvas.Strokes = ToStrokeCollection(plane.Strokes);
        PlaneInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString("#FF29262D"),
            Width = 3,
            Height = 3,
            FitToCurve = true
        };
        SetPlaneTool("Select");
        RenderTiles();
        RenderElements();
        RenderMinimap();

        Dispatcher.BeginInvoke(() =>
        {
            PlaneScrollViewer.ScrollToHorizontalOffset(plane.HorizontalOffset);
            PlaneScrollViewer.ScrollToVerticalOffset(plane.VerticalOffset);
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
            "Erase" => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None
        };
        ElementLayer.IsHitTestVisible = !inkMode;
        TileLayer.IsHitTestVisible = !inkMode;
        PlaneWorkspace.Cursor = tool == "Ink" ? Cursors.Pen : tool == "Erase" ? Cursors.Cross : Cursors.Arrow;
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
                CornerRadius = new CornerRadius(2),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.22
                }
            };
            Canvas.SetLeft(border, left);
            Canvas.SetTop(border, top);
            TileLayer.Children.Add(border);

            var label = new TextBlock
            {
                Text = $"{tile.Column}, {tile.Row}",
                Foreground = BrushFromHex("#88726D73"),
                FontSize = 12,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, left + 14);
            Canvas.SetTop(label, top + 12);
            TileLayer.Children.Add(label);

            if (plane.Tiles.Count > 1)
            {
                var remove = CreateTileActionButton("×", "Удалить этот холст");
                remove.Click += (_, _) => DeleteTile(tile);
                Canvas.SetLeft(remove, left + plane.TileWidth - 36);
                Canvas.SetTop(remove, top + 8);
                TileLayer.Children.Add(remove);
            }

            AddNeighborButtonIfFree(tile.Column - 1, tile.Row, left - 17, top + plane.TileHeight / 2 - 17, occupied);
            AddNeighborButtonIfFree(tile.Column + 1, tile.Row, left + plane.TileWidth - 17, top + plane.TileHeight / 2 - 17, occupied);
            AddNeighborButtonIfFree(tile.Column, tile.Row - 1, left + plane.TileWidth / 2 - 17, top - 17, occupied);
            AddNeighborButtonIfFree(tile.Column, tile.Row + 1, left + plane.TileWidth / 2 - 17, top + plane.TileHeight - 17, occupied);
        }
    }

    private void AddNeighborButtonIfFree(int column, int row, double left, double top, HashSet<(int, int)> occupied)
    {
        if (occupied.Contains((column, row)))
        {
            return;
        }

        var button = CreateTileActionButton("+", "Добавить соседний холст");
        button.Background = BrushFromHex("#FFD85050");
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
        MarkDirty();
    }

    private void DeleteTile(CanvasTileData tile)
    {
        var plane = _currentDocument?.Plane;
        if (plane is null || plane.Tiles.Count <= 1)
        {
            return;
        }

        var intersectsContent = plane.Elements.Any(element => ElementIntersectsTile(element, tile, plane));
        var question = intersectsContent
            ? "На этом холсте есть объекты. Удалить только холст и оставить объекты на плоскости?"
            : "Удалить этот холст?";
        if (MessageBox.Show(question, "Удаление холста", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        PushUndoSnapshot();
        plane.Tiles.Remove(tile);
        RenderTiles();
        RenderMinimap();
        MarkDirty();
    }

    private static bool ElementIntersectsTile(CanvasElementData element, CanvasTileData tile, PlaneData plane)
    {
        var tileRect = new Rect(tile.Column * plane.TileWidth, tile.Row * plane.TileHeight, plane.TileWidth, plane.TileHeight);
        return tileRect.IntersectsWith(new Rect(element.X, element.Y, element.Width, element.Height));
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
            X = centerX - 130,
            Y = centerY - 80,
            Width = kind == ElementKind.NoteSheet ? 420 : 260,
            Height = kind == ElementKind.NoteSheet ? 520 : kind == ElementKind.Text ? 90 : 170,
            Fill = kind switch
            {
                ElementKind.Text => "#00FFFFFF",
                ElementKind.NoteSheet => "#FFFFF9EF",
                _ => "#FFFFF2B8"
            },
            Stroke = kind == ElementKind.Text ? "#0034313A" : "#FF34313A",
            Text = kind switch
            {
                ElementKind.Text => "Двойной щелчок для текста",
                ElementKind.NoteSheet => "Заметки",
                _ => ""
            },
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
            shape.Fill = CreateLinedPaperBrush();
        }
        content.Children.Add(shape);

        var editor = new TextBox
        {
            Text = element.Text,
            Tag = element,
            Background = Brushes.Transparent,
            Foreground = BrushFromHex(element.TextColor),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(element.Kind == ElementKind.NoteSheet ? 30 : 12),
            FontSize = element.FontSize,
            FontWeight = element.IsBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = element.IsItalic ? FontStyles.Italic : FontStyles.Normal,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            Cursor = Cursors.Arrow
        };
        editor.TextChanged += ElementText_TextChanged;
        editor.LostKeyboardFocus += (_, _) => editor.IsReadOnly = true;
        content.Children.Add(editor);
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

        SelectElement(element, frame);
        if (e.ClickCount == 2 && FindTextEditor(frame) is { } editor)
        {
            editor.IsReadOnly = false;
            editor.Cursor = Cursors.IBeam;
            editor.Focus();
            e.Handled = true;
            return;
        }

        PushUndoSnapshot();
        _isDraggingElement = true;
        _elementDragStart = e.GetPosition(ElementLayer);
        _elementOriginalPosition = new Point(element.X, element.Y);
        frame.CaptureMouse();
        e.Handled = true;
    }

    private void ElementView_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingElement || e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement frame || frame.Tag is not CanvasElementData element)
        {
            return;
        }

        var current = e.GetPosition(ElementLayer);
        element.X = _elementOriginalPosition.X + current.X - _elementDragStart.X;
        element.Y = _elementOriginalPosition.Y + current.Y - _elementDragStart.Y;
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
        MarkDirty();
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
    }

    private static void SetElementSelectionVisual(FrameworkElement view, bool selected)
    {
        if (view is Border border)
        {
            border.BorderBrush = selected ? BrushFromHex("#FFD85050") : Brushes.Transparent;
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
        MarkDirty();
    }

    private void PlaneWorld_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == PlaneWorld || e.OriginalSource == ElementLayer)
        {
            if (_selectedElementView is not null) SetElementSelectionVisual(_selectedElementView, false);
            _selectedElement = null;
            _selectedElementView = null;
        }
    }

    private void PlaneWorkspace_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
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
        MarkDirty();
        e.Handled = true;
    }

    private void PlaneWorkspace_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            PlaneScrollViewer.ScrollToHorizontalOffset(PlaneScrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        var oldZoom = PlaneScale.ScaleX;
        var newZoom = Math.Clamp(oldZoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), 0.12, 4.0);
        var pointer = e.GetPosition(PlaneWorkspace);
        var worldX = (PlaneScrollViewer.HorizontalOffset + pointer.X) / oldZoom;
        var worldY = (PlaneScrollViewer.VerticalOffset + pointer.Y) / oldZoom;
        PlaneScale.ScaleX = newZoom;
        PlaneScale.ScaleY = newZoom;
        ZoomText.Text = $"{newZoom:P0}";
        Dispatcher.BeginInvoke(() =>
        {
            PlaneScrollViewer.ScrollToHorizontalOffset(worldX * newZoom - pointer.X);
            PlaneScrollViewer.ScrollToVerticalOffset(worldY * newZoom - pointer.Y);
        }, DispatcherPriority.Background);
        MarkDirty();
        e.Handled = true;
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetPlaneZoom(1.0);

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitAllTiles();

    private void SetPlaneZoom(double zoom)
    {
        var centerX = (PlaneScrollViewer.HorizontalOffset + PlaneScrollViewer.ViewportWidth / 2) / PlaneScale.ScaleX;
        var centerY = (PlaneScrollViewer.VerticalOffset + PlaneScrollViewer.ViewportHeight / 2) / PlaneScale.ScaleY;
        PlaneScale.ScaleX = zoom;
        PlaneScale.ScaleY = zoom;
        ZoomText.Text = $"{zoom:P0}";
        Dispatcher.BeginInvoke(() =>
        {
            PlaneScrollViewer.ScrollToHorizontalOffset(centerX * zoom - PlaneScrollViewer.ViewportWidth / 2);
            PlaneScrollViewer.ScrollToVerticalOffset(centerY * zoom - PlaneScrollViewer.ViewportHeight / 2);
        }, DispatcherPriority.Background);
        MarkDirty();
    }

    private void FitAllTiles()
    {
        var plane = _currentDocument?.Plane;
        if (plane is null || plane.Tiles.Count == 0)
        {
            return;
        }

        var minX = plane.Tiles.Min(tile => WorldOrigin + tile.Column * plane.TileWidth);
        var minY = plane.Tiles.Min(tile => WorldOrigin + tile.Row * plane.TileHeight);
        var maxX = plane.Tiles.Max(tile => WorldOrigin + (tile.Column + 1) * plane.TileWidth);
        var maxY = plane.Tiles.Max(tile => WorldOrigin + (tile.Row + 1) * plane.TileHeight);
        var zoom = Math.Clamp(Math.Min(
            (PlaneWorkspace.ActualWidth - 100) / (maxX - minX),
            (PlaneWorkspace.ActualHeight - 100) / (maxY - minY)), 0.12, 2.0);
        PlaneScale.ScaleX = zoom;
        PlaneScale.ScaleY = zoom;
        ZoomText.Text = $"{zoom:P0}";
        Dispatcher.BeginInvoke(() =>
        {
            PlaneScrollViewer.ScrollToHorizontalOffset((minX + maxX) / 2 * zoom - PlaneScrollViewer.ViewportWidth / 2);
            PlaneScrollViewer.ScrollToVerticalOffset((minY + maxY) / 2 * zoom - PlaneScrollViewer.ViewportHeight / 2);
        }, DispatcherPriority.Background);
        MarkDirty();
    }

    private void PlaneInkCanvas_StrokesChanged(object sender, EventArgs e)
    {
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

        var minX = Math.Min(plane.Tiles.Min(tile => tile.Column * plane.TileWidth),
            plane.Elements.Count > 0 ? plane.Elements.Min(element => element.X) : 0);
        var minY = Math.Min(plane.Tiles.Min(tile => tile.Row * plane.TileHeight),
            plane.Elements.Count > 0 ? plane.Elements.Min(element => element.Y) : 0);
        var maxX = Math.Max(plane.Tiles.Max(tile => (tile.Column + 1) * plane.TileWidth),
            plane.Elements.Count > 0 ? plane.Elements.Max(element => element.X + element.Width) : plane.TileWidth);
        var maxY = Math.Max(plane.Tiles.Max(tile => (tile.Row + 1) * plane.TileHeight),
            plane.Elements.Count > 0 ? plane.Elements.Max(element => element.Y + element.Height) : plane.TileHeight);
        var scale = Math.Min(185 / Math.Max(maxX - minX, 1), 102 / Math.Max(maxY - minY, 1));

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
    }

    private void ToggleMinimap_Click(object sender, RoutedEventArgs e)
    {
        _minimapExpanded = !_minimapExpanded;
        Minimap.Height = _minimapExpanded ? 145 : 35;
        Minimap.Width = _minimapExpanded ? 210 : 46;
        MinimapCanvas.Visibility = _minimapExpanded ? Visibility.Visible : Visibility.Collapsed;
        RenderMinimap();
    }
}
