using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using XRedactor.Models;

namespace XRedactor;

public partial class MainWindow
{
    private UIElement? _notebookDecoration;
    private bool _inkSnapshotCaptured;

    private void ConfigureNotebook(NotebookData notebook)
    {
        NotebookTextEditor.Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 18,
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
                NotebookTextEditor.Document.Blocks.Add(new Paragraph(new Run("[Текст документа не удалось прочитать]")));
            }
        }

        NotebookInkCanvas.Strokes = ToStrokeCollection(notebook.Strokes);
        ApplyNotebookTemplate(notebook.Template);
        SetNotebookMode("Text");
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

        NotebookPaper.Background = template == NotebookTemplate.Lined
            ? CreateLinedPaperBrush()
            : BrushFromHex("#FFFFF9EF");

        NotebookTextEditor.Margin = template switch
        {
            NotebookTemplate.SpiralTop => new Thickness(70, 105, 70, 60),
            NotebookTemplate.SpiralBottom => new Thickness(70, 70, 70, 105),
            _ => new Thickness(70, 80, 70, 60)
        };

        if (template is NotebookTemplate.SpiralTop or NotebookTemplate.SpiralBottom)
        {
            var canvas = new Canvas { IsHitTestVisible = false, Height = 58, Margin = new Thickness(30, 13, 30, 13) };
            canvas.VerticalAlignment = template == NotebookTemplate.SpiralTop
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
            for (var index = 0; index < 13; index++)
            {
                var x = 24 + index * 56;
                var hole = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = BrushFromHex("#FFD7CEC0")
                };
                Canvas.SetLeft(hole, x);
                Canvas.SetTop(hole, 23);
                canvas.Children.Add(hole);

                var ring = new System.Windows.Shapes.Path
                {
                    Stroke = BrushFromHex("#FF5A5660"),
                    StrokeThickness = 2,
                    Data = Geometry.Parse($"M {x + 6},29 C {x + 6},3 {x + 27},3 {x + 27},29 C {x + 27},50 {x + 13},50 {x + 13},34")
                };
                canvas.Children.Add(ring);
            }

            Panel.SetZIndex(canvas, -1);
            _notebookDecoration = canvas;
            NotebookPaperGrid.Children.Insert(0, canvas);
        }
    }

    private static Brush CreateLinedPaperBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(BrushFromHex("#FFFFF9EF"), null,
            new RectangleGeometry(new Rect(0, 0, 40, 40))));
        group.Children.Add(new GeometryDrawing(null,
            new Pen(BrushFromHex("#FFD9DDE4"), 1),
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
        NotebookTextEditor.IsReadOnly = mode != "Text";
        NotebookTextEditor.IsHitTestVisible = mode == "Text";
        NotebookInkCanvas.IsHitTestVisible = mode != "Text";
        NotebookInkCanvas.EditingMode = mode switch
        {
            "Ink" => InkCanvasEditingMode.Ink,
            "Erase" => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None
        };

        if (mode == "Text") NotebookTextEditor.Focus();
        UpdateNotebookDrawingAttributes();
    }

    private void NotebookTextEditor_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    private void NotebookInkCanvas_StrokesChanged(object sender, EventArgs e)
    {
        _inkSnapshotCaptured = false;
        MarkDirty();
    }

    private void InkCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_inkSnapshotCaptured)
        {
            PushUndoSnapshot();
            _inkSnapshotCaptured = true;
        }
    }

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, NotebookTextEditor);
        NotebookTextEditor.Focus();
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, NotebookTextEditor);
        NotebookTextEditor.Focus();
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleUnderline.Execute(null, NotebookTextEditor);
        NotebookTextEditor.Focus();
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeBox?.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString(), out var size) && NotebookTextEditor is not null)
        {
            NotebookTextEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            NotebookTextEditor.Focus();
            MarkDirty();
        }
    }

    private void NotebookColorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotebookColorBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string color ||
            NotebookTextEditor is null || NotebookInkCanvas is null)
        {
            return;
        }

        NotebookTextEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, BrushFromHex(color));
        UpdateNotebookDrawingAttributes();
        MarkDirty();
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
        NotebookInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString(color),
            Width = size,
            Height = size,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse
        };
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
                    FitToCurve = true
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
