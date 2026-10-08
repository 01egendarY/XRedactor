using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using XRedactor.Models;

namespace XRedactor;

public partial class MainWindow
{
    private DateTime _lastPlaneTextEdit;
    private bool _clippingInk;
    private bool _minimapDragging;
    private bool _updatingPlaneViewport;

    private void UpdateInkClip()
    {
        if (_currentDocument?.Plane is not { } plane) return;
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var tile in plane.Tiles)
            geometry.Children.Add(new RectangleGeometry(new Rect(WorldOrigin + tile.Column * plane.TileWidth,
                WorldOrigin + tile.Row * plane.TileHeight, plane.TileWidth, plane.TileHeight)));
        PlaneInkCanvas.Clip = geometry;
    }

    private void UpdatePlaneExtents(PlaneData plane)
    {
        var bounds = GetPlaneContentBounds(plane, true);
        var origin = Math.Max(WorldOrigin, Math.Max(-bounds.Left, -bounds.Top) + 1000);
        if (origin > WorldOrigin)
        {
            var delta = origin - WorldOrigin;
            _clippingInk = true;
            try { PlaneInkCanvas.Strokes.Transform(new Matrix(1, 0, 0, 1, delta, delta), false); }
            finally { _clippingInk = false; }
            WorldOrigin = origin;
            plane.WorldOrigin = origin;
            PlaneScrollViewer.ScrollToHorizontalOffset(PlaneScrollViewer.HorizontalOffset + delta * PlaneScale.ScaleX);
            PlaneScrollViewer.ScrollToVerticalOffset(PlaneScrollViewer.VerticalOffset + delta * PlaneScale.ScaleY);
            RenderElements();
        }
        var width = Math.Max(12000, WorldOrigin + bounds.Right + 1000);
        var height = Math.Max(12000, WorldOrigin + bounds.Bottom + 1000);
        foreach (var layer in new FrameworkElement[] { PlaneWorld, TileLayer, PlaneInkCanvas, ElementLayer })
        { layer.Width = width; layer.Height = height; }
    }

    private void ClipCollectedInk()
    {
        if (_clippingInk || _currentDocument?.Plane is not { } plane) return;
        _clippingInk = true;
        try
        {
            var rectangles = plane.Tiles.Select(tile => new Rect(WorldOrigin + tile.Column * plane.TileWidth,
                WorldOrigin + tile.Row * plane.TileHeight, plane.TileWidth, plane.TileHeight)).ToArray();
            var clipped = new StrokeCollection();
            foreach (var stroke in PlaneInkCanvas.Strokes)
                foreach (var rectangle in rectangles)
                    clipped.Add(stroke.GetClipResult(rectangle));
            PlaneInkCanvas.Strokes = clipped;
        }
        finally { _clippingInk = false; }
    }

    private void ShowShapes_Click(object sender, RoutedEventArgs e)
    {
        SetPlaneTool("Select");
        ContextToolbar.Visibility = Visibility.Visible;
        SetButtonActive(PlaneShapesButton, true);
        ShapeTools.Visibility = Visibility.Visible;
        NoteTools.Visibility = Visibility.Collapsed;
        if (_selectedElement is null || _selectedElement.Kind == ElementKind.NoteSheet)
        {
            _isSyncingPlaneControls = true;
            ShapeFillBox.SelectedIndex = 1;
            ShapeOutlineColorBox.SelectedIndex = 0;
            _isSyncingPlaneControls = false;
        }
    }

    private static UIElement CreateNoteSpiral(CanvasElementData element)
    {
        var top = element.Template == NotebookTemplate.SpiralTop;
        var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        var extent = top ? element.Width : element.Height;
        for (var offset = 24.0; offset < extent - 24; offset += 32)
        {
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = top ? 12 : 32, Height = top ? 32 : 12,
                Stroke = BrushFromHex("#FF726D73"), StrokeThickness = 2, Fill = Brushes.Transparent
            };
            Canvas.SetLeft(ring, top ? offset : 8);
            Canvas.SetTop(ring, top ? 8 : offset);
            canvas.Children.Add(ring);
        }
        return canvas;
    }

    private void CloseContextToolbar_Click(object sender, RoutedEventArgs e)
    {
        ContextToolbar.Visibility = Visibility.Collapsed;
        SetButtonActive(PlaneShapesButton, false);
    }

    private void SyncContextToolbar(CanvasElementData element)
    {
        ContextToolbar.Visibility = Visibility.Visible;
        var note = element.Kind == ElementKind.NoteSheet;
        SetButtonActive(PlaneShapesButton, element.Kind is ElementKind.Rectangle or ElementKind.Ellipse);
        NoteTools.Visibility = note ? Visibility.Visible : Visibility.Collapsed;
        ShapeTools.Visibility = note || element.Kind == ElementKind.Text ? Visibility.Collapsed : Visibility.Visible;
        if (note)
        {
            SelectComboBoxItemByTag(NoteTemplateBox, element.Template.ToString());
            if (NoteFontBox.Items.Count == 0)
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(font => font.Source)) NoteFontBox.Items.Add(font.Source);
            NoteFontBox.SelectedItem = element.FontFamily;
        }
        else
        {
            SelectComboBoxItemByTag(ShapeFillBox, element.Fill);
            SelectComboBoxItemByTag(ShapeOutlineColorBox, element.Stroke);
            ShapeOutlineBox.IsChecked = element.StrokeThickness > 0;
            ShapeOutlineWidthBox.Text = Math.Max(1, element.StrokeThickness).ToString("0.##");
        }
    }

    private void ShapeStyle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isSyncingPlaneControls || _selectedElement is not { Kind: ElementKind.Rectangle or ElementKind.Ellipse } element) return;
        if (!double.TryParse(ShapeOutlineWidthBox.Text, out var width) || !double.IsFinite(width) || width is < 0 or > 40) return;
        var fill = GetSelectedComboTag(ShapeFillBox, element.Fill);
        var stroke = GetSelectedComboTag(ShapeOutlineColorBox, element.Stroke);
        var thickness = ShapeOutlineBox.IsChecked == true ? width : 0;
        if (fill == element.Fill && stroke == element.Stroke && thickness == element.StrokeThickness) return;
        ApplyPlaneTextFormatting(item => { item.Fill = fill; item.Stroke = stroke; item.StrokeThickness = thickness; });
    }

    private void NoteStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _isSyncingPlaneControls || _selectedElement is not { Kind: ElementKind.NoteSheet } element) return;
        if (!Enum.TryParse<NotebookTemplate>(GetSelectedComboTag(NoteTemplateBox, element.Template.ToString()), out var template)) return;
        var font = NoteFontBox.SelectedItem as string ?? element.FontFamily;
        if (template == element.Template && font == element.FontFamily) return;
        ApplyPlaneTextFormatting(item =>
        {
            var displaySize = FontSizing.GetDisplayFontSize(item.Template, item.FontSize, item.FontFamily);
            if (item.Template != template)
                displaySize = FontSizing.GetDefaultDisplayFontSize(template);
            item.Template = template;
            item.FontFamily = font;
            item.FontSize = FontSizing.GetPixelFontSize(template, displaySize, font);
        });
    }

    private void PlaneFontSize_Commit(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isSyncingPlaneControls || _selectedElement is null) return;
        if (!double.TryParse(PlaneFontSizeBox.Text, out var display) || !double.IsFinite(display) || display is < 0.25 or > 300)
        { SyncPlaneControlsFromElement(_selectedElement); return; }
        var pixels = FontSizing.GetPixelFontSize(_selectedElement.Kind == ElementKind.NoteSheet ? _selectedElement.Template : NotebookTemplate.Blank, display, _selectedElement.FontFamily);
        if (Math.Abs(pixels - _selectedElement.FontSize) < 0.01) return;
        ApplyPlaneTextFormatting(item => item.FontSize = pixels);
    }

    private void PlaneFontSize_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PlaneFontSize_Commit(sender, e);
        e.Handled = true;
    }

    private void PlaneFontSizeStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string step } || !double.TryParse(step, out var delta)) return;
        var value = GetSelectedComboNumber(PlaneFontSizeBox, 18);
        PlaneFontSizeBox.Text = Math.Clamp(value + delta, 0.25, 300).ToString("0.##");
        PlaneFontSize_Commit(sender, e);
    }

    private void PlaneScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_currentDocument?.Kind == DocumentKind.Plane && !_isLoading && !_updatingPlaneViewport) RenderMinimap();
    }

    private void ApplyPlaneViewport(double zoom, Point worldPoint, Point anchor)
    {
        _updatingPlaneViewport = true;
        try
        {
            var shift = Math.Max(anchor.X / zoom - worldPoint.X, anchor.Y / zoom - worldPoint.Y);
            if (shift > 0 && _currentDocument?.Plane is { } plane)
            {
                shift += 1000;
                _clippingInk = true;
                try { PlaneInkCanvas.Strokes.Transform(new Matrix(1, 0, 0, 1, shift, shift), false); }
                finally { _clippingInk = false; }
                WorldOrigin += shift;
                plane.WorldOrigin = WorldOrigin;
                worldPoint = new Point(worldPoint.X + shift, worldPoint.Y + shift);
                RenderTiles();
                RenderElements();
            }
            var width = Math.Max(PlaneWorld.Width, worldPoint.X + PlaneScrollViewer.ViewportWidth / zoom + 1000);
            var height = Math.Max(PlaneWorld.Height, worldPoint.Y + PlaneScrollViewer.ViewportHeight / zoom + 1000);
            foreach (var layer in new FrameworkElement[] { PlaneWorld, TileLayer, PlaneInkCanvas, ElementLayer })
            { layer.Width = width; layer.Height = height; }
            PlaneScale.ScaleX = zoom;
            PlaneScale.ScaleY = zoom;
            ZoomText.Text = $"{zoom:P0}";
            PlaneScrollViewer.UpdateLayout();
            PlaneScrollViewer.ScrollToHorizontalOffset(worldPoint.X * zoom - anchor.X);
            PlaneScrollViewer.ScrollToVerticalOffset(worldPoint.Y * zoom - anchor.Y);
            PlaneScrollViewer.UpdateLayout();
        }
        finally { _updatingPlaneViewport = false; }
        RenderMinimap();
    }

    private bool IsInsideControlPanel(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, PlaneToolbar) || ReferenceEquals(source, ContextToolbar)) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static void SetFontSizeOptions(ComboBox box, NotebookTemplate template)
    {
        var cellular = template is NotebookTemplate.Lined or NotebookTemplate.Ruled;
        if (box.Tag is bool previous && previous == cellular) return;
        box.Tag = cellular;
        box.Items.Clear();
        foreach (var value in cellular ? new[] { 1, 2, 3, 4, 5, 6 } : new[] { 10, 12, 14, 18, 24, 32, 42, 72 })
            box.Items.Add(new ComboBoxItem { Content = value.ToString() });
    }

    private bool IsInsideMinimap(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, Minimap)) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void MinimapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_minimapDragging || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(MinimapCanvas);
        CenterPlaneOnPoint(_minimapMinX + point.X / _minimapScale, _minimapMinY + point.Y / _minimapScale);
        e.Handled = true;
    }

    private void MinimapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _minimapDragging = false;
        MinimapCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }
}
