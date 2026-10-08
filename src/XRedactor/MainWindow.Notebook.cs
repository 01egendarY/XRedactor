using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using XRedactor.Models;

namespace XRedactor;

public partial class MainWindow
{
    private UIElement? _notebookDecoration;
    private bool _inkSnapshotCaptured;
    private bool _notebookTextSnapshotCaptured;
    private DispatcherTimer? _notebookTextUndoTimer;
    private string _notebookMode = "Text";
    private bool _isSyncingNotebookFontControls;

    private void ConfigureNotebook(NotebookData notebook)
    {
        var fontFamily = string.IsNullOrWhiteSpace(notebook.FontFamily) ? "Segoe UI" : notebook.FontFamily;
        var displaySize = notebook.FontSize > 0 ? notebook.FontSize : FontSizing.GetDefaultDisplayFontSize(notebook.Template);
        var pixelSize = FontSizing.GetPixelFontSize(notebook.Template, displaySize, fontFamily);
        SetFontSizeOptions(FontSizeBox, notebook.Template);
        FontSizeBox.Text = displaySize.ToString("0.##");
        NotebookFontSizeLabel.Text = GetNotebookFontSizeLabel(notebook.Template);
        NotebookTextEditor.FontSize = pixelSize;
        NotebookTextEditor.Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily(fontFamily),
            FontSize = pixelSize,
            Foreground = BrushFromHex("#FF29262D")
        };

        if (!string.IsNullOrWhiteSpace(notebook.RichTextRtfBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(notebook.RichTextRtfBase64);
                using var stream = new MemoryStream(bytes);
                new TextRange(NotebookTextEditor.Document.ContentStart, NotebookTextEditor.Document.ContentEnd)
                    .Load(stream, DataFormats.Rtf);
            }
            catch
            {
                NotebookTextEditor.Document.Blocks.Clear();
                NotebookTextEditor.Document.Blocks.Add(new Paragraph(new Run("[Текст документа не удалось прочитать]")));
            }
        }

        NotebookInkCanvas.Strokes = ToStrokeCollection(notebook.Strokes);
        ApplyNotebookTemplate(notebook.Template);
        _notebookTextUndoTimer?.Stop();
        _notebookTextSnapshotCaptured = false;
        _inkSnapshotCaptured = false;
        SetNotebookMode("Text");
        UpdateNotebookFormattingButtons();
    }

    private void CaptureNotebook(NotebookData notebook)
    {
        using var stream = new MemoryStream();
        new TextRange(NotebookTextEditor.Document.ContentStart, NotebookTextEditor.Document.ContentEnd)
            .Save(stream, DataFormats.Rtf);
        notebook.RichTextRtfBase64 = Convert.ToBase64String(stream.ToArray());
        notebook.Strokes = FromStrokeCollection(NotebookInkCanvas.Strokes);
    }

    private void ApplyNotebookTemplate(NotebookTemplate template)
    {
        if (_notebookDecoration is not null)
        {
            NotebookPaperGrid.Children.Remove(_notebookDecoration);
            _notebookDecoration = null;
        }

        NotebookPaper.Background = template switch
        {
            NotebookTemplate.Lined => CreateGridPaperBrush(),
            NotebookTemplate.Ruled => CreateRuledPaperBrush(),
            _ => BrushFromHex("#FFFFF9EF")
        };

        NotebookTextEditor.Margin = template switch
        {
            NotebookTemplate.SpiralTop => new Thickness(70, 105, 70, 60),
            NotebookTemplate.SpiralLeft or NotebookTemplate.SpiralBottom => new Thickness(105, 80, 70, 60),
            _ => new Thickness(70, 80, 70, 60)
        };

        if (template is NotebookTemplate.SpiralTop or NotebookTemplate.SpiralLeft or NotebookTemplate.SpiralBottom)
        {
            var spiralOnTop = template == NotebookTemplate.SpiralTop;
            var canvas = new Canvas
            {
                IsHitTestVisible = false,
                Width = spiralOnTop ? double.NaN : 58,
                Height = spiralOnTop ? 58 : double.NaN,
                Margin = spiralOnTop ? new Thickness(30, 13, 30, 13) : new Thickness(13, 30, 13, 30)
            };
            canvas.VerticalAlignment = spiralOnTop ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            canvas.HorizontalAlignment = spiralOnTop ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

            var count = spiralOnTop ? 13 : 16;
            for (var index = 0; index < count; index++)
            {
                var primary = spiralOnTop ? 24 + index * 56 : 24 + index * 62;
                var hole = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = BrushFromHex("#FFD7CEC0")
                };
                Canvas.SetLeft(hole, spiralOnTop ? primary : 23);
                Canvas.SetTop(hole, spiralOnTop ? 23 : primary);
                canvas.Children.Add(hole);

                var ring = new System.Windows.Shapes.Path
                {
                    Stroke = BrushFromHex("#FF5A5660"),
                    StrokeThickness = 2,
                    Data = spiralOnTop
                        ? Geometry.Parse($"M {primary + 6},29 C {primary + 6},3 {primary + 27},3 {primary + 27},29 C {primary + 27},50 {primary + 13},50 {primary + 13},34")
                        : Geometry.Parse($"M 29,{primary + 6} C 3,{primary + 6} 3,{primary + 27} 29,{primary + 27} C 50,{primary + 27} 50,{primary + 13} 34,{primary + 13}")
                };
                canvas.Children.Add(ring);
            }

            Panel.SetZIndex(canvas, -1);
            _notebookDecoration = canvas;
            NotebookPaperGrid.Children.Insert(0, canvas);
        }
    }

    private static Brush CreateGridPaperBrush(string background = "#FFFFF9EF")
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(BrushFromHex(background), null,
            new RectangleGeometry(new Rect(0, 0, 40, 40))));
        group.Children.Add(new GeometryDrawing(null,
            new Pen(BrushFromHex("#FFE3E0D8"), 1),
            new LineGeometry(new Point(39, 0), new Point(39, 40))));
        group.Children.Add(new GeometryDrawing(null,
            new Pen(BrushFromHex("#FFE3E0D8"), 1),
            new LineGeometry(new Point(0, 39), new Point(40, 39))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 40, 40),
            ViewportUnits = BrushMappingMode.Absolute
        };
    }

    private static Brush CreateRuledPaperBrush(string background = "#FFFFF9EF")
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(BrushFromHex(background), null,
            new RectangleGeometry(new Rect(0, 0, 40, 40))));
        group.Children.Add(new GeometryDrawing(null,
            new Pen(BrushFromHex("#FFE3E0D8"), 1),
            new LineGeometry(new Point(0, 39), new Point(40, 39))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 40, 40),
            ViewportUnits = BrushMappingMode.Absolute
        };
    }

    private void NotebookMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            SetNotebookMode(button.Tag?.ToString() ?? "Text");
        }
    }

    private void SetNotebookMode(string mode)
    {
        _notebookMode = mode;
        NotebookTextEditor.IsReadOnly = mode != "Text";
        NotebookTextEditor.IsHitTestVisible = mode == "Text";
        NotebookInkCanvas.IsHitTestVisible = mode != "Text";
        NotebookInkCanvas.EditingMode = mode switch
        {
            "Ink" => InkCanvasEditingMode.Ink,
            "Erase" => InkCanvasEditingMode.EraseByPoint,
            _ => InkCanvasEditingMode.None
        };
        NotebookInkCanvas.EraserShape = new EllipseStylusShape(22, 22);

        if (mode != "Text" && NotebookTextEditor.IsKeyboardFocusWithin)
        {
            Keyboard.ClearFocus();
        }

        if (mode != "Text")
        {
            _notebookTextUndoTimer?.Stop();
            _notebookTextSnapshotCaptured = false;
        }

        SetButtonActive(NotebookTextModeButton, mode == "Text");
        SetButtonActive(NotebookInkModeButton, mode == "Ink");
        SetButtonActive(NotebookEraseModeButton, mode == "Erase");
        UpdateNotebookDrawingAttributes();
    }

    private void NotebookTextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        MarkDirty();
        if (_notebookTextSnapshotCaptured)
        {
            StartNotebookTextUndoTimer();
        }
    }

    private void NotebookTextEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateNotebookFormattingButtons();
    }

    private void NotebookTextEditor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        CaptureNotebookTextSnapshotBeforeEdit();
    }

    private void NotebookTextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
            _notebookTextSnapshotCaptured = false;
        var changesText =
            e.Key is Key.Back or Key.Delete or Key.Enter or Key.Space or Key.Tab ||
            control && e.Key is Key.V or Key.X;

        if (changesText)
        {
            if (e.Key is Key.Back or Key.Delete or Key.Enter or Key.Space or Key.Tab)
            {
                _notebookTextUndoTimer?.Stop();
                _notebookTextSnapshotCaptured = false;
            }

            CaptureNotebookTextSnapshotBeforeEdit();
        }
    }

    private void NotebookTextEditor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        CaptureNotebookTextSnapshotBeforeEdit();
    }

    private void NotebookTextEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _notebookTextUndoTimer?.Stop();
        _notebookTextSnapshotCaptured = false;
    }

    private void CaptureNotebookTextSnapshotBeforeEdit()
    {
        if (_isLoading || _notebookMode != "Text" || _notebookTextSnapshotCaptured)
        {
            return;
        }

        PushUndoSnapshot();
        _notebookTextSnapshotCaptured = true;
        StartNotebookTextUndoTimer();
    }

    private void StartNotebookTextUndoTimer()
    {
        _notebookTextUndoTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _notebookTextUndoTimer.Stop();
        _notebookTextUndoTimer.Tick -= NotebookTextUndoTimer_Tick;
        _notebookTextUndoTimer.Tick += NotebookTextUndoTimer_Tick;
        _notebookTextUndoTimer.Start();
    }

    private void NotebookTextUndoTimer_Tick(object? sender, EventArgs e)
    {
        _notebookTextUndoTimer?.Stop();
        _notebookTextSnapshotCaptured = false;
    }

    private void NotebookInkCanvas_StrokesChanged(object sender, EventArgs e)
    {
        _inkSnapshotCaptured = false;
        MarkDirty();
    }

    private void InkCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _notebookTextSnapshotCaptured = false;
        if (!_inkSnapshotCaptured)
        {
            PushUndoSnapshot();
            _inkSnapshotCaptured = true;
        }
    }

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        PreserveNotebookViewport(() =>
        {
            PushUndoSnapshot();
            _notebookTextUndoTimer?.Stop();
            _notebookTextSnapshotCaptured = false;
            EditingCommands.ToggleBold.Execute(null, NotebookTextEditor);
            UpdateNotebookFormattingButtons();
        });
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        PreserveNotebookViewport(() =>
        {
            PushUndoSnapshot();
            _notebookTextUndoTimer?.Stop();
            _notebookTextSnapshotCaptured = false;
            EditingCommands.ToggleItalic.Execute(null, NotebookTextEditor);
            UpdateNotebookFormattingButtons();
        });
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        PreserveNotebookViewport(() =>
        {
            PushUndoSnapshot();
            _notebookTextUndoTimer?.Stop();
            _notebookTextSnapshotCaptured = false;
            EditingCommands.ToggleUnderline.Execute(null, NotebookTextEditor);
            UpdateNotebookFormattingButtons();
        });
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _isSyncingNotebookFontControls) return;
        if (FontSizeBox?.SelectedItem is ComboBoxItem item) FontSizeBox.Text = item.Content?.ToString() ?? "2";
        if (FontSizeBox is not null && NotebookTextEditor is not null && TryGetNotebookDisplaySize(out var size))
        {
            ApplyNotebookFontSize(size);
        }
    }

    private void NotebookFontSize_Commit(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isSyncingNotebookFontControls) return;
        if (TryGetNotebookDisplaySize(out var size))
        {
            ApplyNotebookFontSize(size);
        }
        else if (_currentDocument?.Notebook is { } notebook)
        {
            FontSizeBox.Text = notebook.FontSize.ToString("0.##");
        }
    }

    private void NotebookFontSize_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NotebookFontSize_Commit(sender, e);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void NotebookFontSizeStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string incrementText } && int.TryParse(incrementText, out var increment))
        {
            var current = TryGetNotebookDisplaySize(out var value)
                ? value
                : FontSizing.GetDefaultDisplayFontSize(GetCurrentNotebookTemplate());
            var next = Math.Clamp(current + increment, 1, 200);
            FontSizeBox.Text = next.ToString("0.##");
            ApplyNotebookFontSize(next);
        }
    }

    private bool TryGetNotebookDisplaySize(out double size)
    {
        size = 0;
        return FontSizeBox is not null && double.TryParse(FontSizeBox.Text, out size) &&
               double.IsFinite(size) && size >= 1 && size <= 200;
    }

    private void ApplyNotebookFontSize(double displaySize)
    {
        if (NotebookTextEditor is null || _currentDocument?.Notebook is not { } notebook ||
            !double.IsFinite(displaySize) || displaySize < 1 || displaySize > 200)
        {
            return;
        }

        var pixelSize = FontSizing.GetPixelFontSize(notebook.Template, displaySize, notebook.FontFamily);
        PreserveNotebookViewport(() =>
        {
            var currentSize = NotebookTextEditor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            var selectionChanges = currentSize is not double actual || Math.Abs(actual - pixelSize) > 0.001;
            if (!_isLoading && (selectionChanges || Math.Abs(notebook.FontSize - displaySize) > 0.001))
            {
                PushUndoSnapshot();
                _notebookTextUndoTimer?.Stop();
                _notebookTextSnapshotCaptured = false;
            }

            notebook.FontSize = displaySize;
            NotebookTextEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, pixelSize);
            MarkDirty();
        });
    }

    private NotebookTemplate GetCurrentNotebookTemplate() =>
        _currentDocument?.Notebook?.Template ?? NotebookTemplate.Lined;

    private static string GetNotebookFontSizeLabel(NotebookTemplate template) => template switch
    {
        NotebookTemplate.Lined => "Размер текста (клетки)",
        NotebookTemplate.Ruled => "Размер текста (строки)",
        _ => "Размер текста (pt)"
    };

    private void NotebookColorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotebookColorBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string color ||
            NotebookTextEditor is null || NotebookInkCanvas is null)
        {
            return;
        }

        PreserveNotebookViewport(() =>
        {
            if (!_isLoading && _notebookMode == "Text")
            {
                PushUndoSnapshot();
                _notebookTextSnapshotCaptured = false;
            }
            if (_notebookMode == "Text")
                NotebookTextEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, BrushFromHex(color));
            UpdateNotebookDrawingAttributes();
            MarkDirty();
        });
    }

    private void NotebookPenSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NotebookInkCanvas is not null)
        {
            UpdateNotebookDrawingAttributes();
        }
    }

    private void UpdateNotebookDrawingAttributes()
    {
        if (NotebookColorBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string color)
        {
            return;
        }

        var size = NotebookPenSize?.Value ?? 3;
        var eraserSize = Math.Clamp(size * 5, 12, 48);
        NotebookInkCanvas.EraserShape = new EllipseStylusShape(eraserSize, eraserSize);
        NotebookInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString(color),
            Width = size,
            Height = size,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse
        };
    }

    private void UpdateNotebookFormattingButtons()
    {
        if (NotebookTextEditor is null || BoldButton is null || ItalicButton is null || UnderlineButton is null)
        {
            return;
        }

        SetButtonActive(BoldButton,
            NotebookTextEditor.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight &&
            weight == FontWeights.Bold);
        SetButtonActive(ItalicButton,
            NotebookTextEditor.Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle style &&
            style == FontStyles.Italic);

        var decorations = NotebookTextEditor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        SetButtonActive(UnderlineButton, decorations is TextDecorationCollection collection &&
                                      collection.Any(decoration => decoration.Location == TextDecorationLocation.Underline));
        if (_currentDocument?.Notebook is { } notebook &&
            NotebookTextEditor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double pixelSize)
        {
            _isSyncingNotebookFontControls = true;
            try { FontSizeBox.Text = FontSizing.GetDisplayFontSize(notebook.Template, pixelSize, notebook.FontFamily).ToString("0.##"); }
            finally { _isSyncingNotebookFontControls = false; }
        }
    }

    private void PreserveNotebookViewport(Action action)
    {
        if (NotebookScrollViewer is null)
        {
            action();
            return;
        }

        var horizontal = NotebookScrollViewer.HorizontalOffset;
        var vertical = NotebookScrollViewer.VerticalOffset;
        action();
        Dispatcher.BeginInvoke(() =>
        {
            NotebookScrollViewer.ScrollToHorizontalOffset(horizontal);
            NotebookScrollViewer.ScrollToVerticalOffset(vertical);
        }, DispatcherPriority.Background);
    }

    private static void SetButtonActive(Button button, bool active)
    {
        button.Background = active ? (Brush)Application.Current.Resources["Accent"] : (Brush)Application.Current.Resources["PanelRaised"];
        button.Foreground = active ? Brushes.White : (Brush)Application.Current.Resources["PrimaryText"];
    }

    private static void SelectComboBoxItemByContent(ComboBox comboBox, string content)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), content, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static List<InkStrokeData> FromStrokeCollection(StrokeCollection strokes)
    {
        return strokes.Select(stroke => new InkStrokeData
        {
            Color = stroke.DrawingAttributes.Color.ToString(),
            Thickness = stroke.DrawingAttributes.Width,
            Points = stroke.StylusPoints.Select(point => new InkPointData
            {
                X = point.X,
                Y = point.Y,
                Pressure = point.PressureFactor
            }).ToList()
        }).ToList();
    }

    private static StrokeCollection ToStrokeCollection(IEnumerable<InkStrokeData> strokes)
    {
        var collection = new StrokeCollection();
        foreach (var data in strokes)
        {
            if (data.Points.Count == 0)
            {
                continue;
            }

            var points = new StylusPointCollection(data.Points.Select(point =>
                new StylusPoint(point.X, point.Y, Math.Clamp(point.Pressure, 0f, 1f))));
            var stroke = new Stroke(points)
            {
                DrawingAttributes = new DrawingAttributes
                {
                    Color = (Color)ColorConverter.ConvertFromString(data.Color),
                    Width = data.Thickness,
                    Height = data.Thickness,
                    FitToCurve = true,
                    StylusTip = StylusTip.Ellipse
                }
            };
            collection.Add(stroke);
        }

        return collection;
    }

    private static SolidColorBrush BrushFromHex(string value)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
        catch
        {
            return new SolidColorBrush(Colors.Magenta);
        }
    }
}
