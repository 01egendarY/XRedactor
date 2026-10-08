using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XRedactor;
using XRedactor.Models;

internal static class EditorRegressionTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckEditor(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Editor regression failed", failure);
    }

    private static void CheckEditor()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow();
        SetField(window, "_autosaveEnabled", false);
        SetField(window, "_loadingPersistentSettings", true);
        var root = (FrameworkElement)window.Content;
        Layout(root);
        Render(root, "home.png");

        foreach (var template in new[] { NotebookTemplate.Lined, NotebookTemplate.Ruled, NotebookTemplate.SpiralLeft, NotebookTemplate.Blank })
        {
            var display = FontSizing.GetDefaultDisplayFontSize(template);
            var pixels = FontSizing.GetPixelFontSize(template, display);
            Assert(Math.Abs(FontSizing.GetDisplayFontSize(template, pixels) - display) < 0.001, "Font size conversion must round-trip");
            var doc = new XRedDocument { Kind = DocumentKind.Notebook, Notebook = new NotebookData { Template = template, FontSize = display } };
            Open(window, doc);
            var editor = (RichTextBox)window.FindName("NotebookTextEditor");
            Assert(Math.Abs(editor.Document.FontSize - pixels) < 0.001, "Notebook default font size");
            Call(window, "CaptureNotebookTextSnapshotBeforeEdit");
            editor.AppendText("First word\nSecond line: Привет");
            Call(window, "PushUndoSnapshot");
            var ink = (InkCanvas)window.FindName("NotebookInkCanvas");
            ink.Strokes.Add(Stroke(100, 100, 200, 180));
            Call(window, "PushUndoSnapshot");
            ink.Strokes.Add(Stroke(250, 200, 300, 240));
            Call(window, "UndoDocumentAction");
            Assert(ink.Strokes.Count == 1, "First undo removes only latest stroke");
            Assert(Text(editor).Contains("Привет"), "Drawing undo preserves text");
            Call(window, "UndoDocumentAction");
            Assert(ink.Strokes.Count == 0 && Text(editor).Contains("First"), "Second undo removes earlier stroke");
            Call(window, "UndoDocumentAction");
            Assert(!Text(editor).Contains("First"), "Third undo removes text action");
            Call(window, "RedoDocumentAction");
            Assert(Text(editor).Contains("First"), "Redo restores text");
            editor.SelectAll();
            editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, pixels * 1.5);
            var historyBeforeSize = Field<Stack<string>>(window, "_undoHistory").Count;
            Call(window, "ApplyNotebookFontSize", display);
            Assert(Field<Stack<string>>(window, "_undoHistory").Count == historyBeforeSize + 1, "Selection size changes require undo even when metadata matches target");
            Call(window, "UndoDocumentAction");
            editor.SelectAll();
            var restoredSize = editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            Assert(restoredSize is double previousSize && Math.Abs(previousSize - pixels * 1.5) < 0.8, $"Undo restores selected text font size: actual={restoredSize}, expected={pixels * 1.5}");
            Render(root, $"notebook-{template}.png");
        }

        Open(window, new XRedDocument { Kind = DocumentKind.Notebook, Notebook = new NotebookData { FontSize = 2 } });
        var typedEditor = (RichTextBox)window.FindName("NotebookTextEditor");
        void TypeGroup(string text)
        {
            var composition = new TextComposition(InputManager.Current, typedEditor, text);
            Call(window, "NotebookTextEditor_PreviewTextInput", typedEditor, new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition));
            typedEditor.AppendText(text);
        }
        TypeGroup("one");
        TypeGroup("two");
        Assert(Field<Stack<string>>(window, "_undoHistory").Count == 1, "Continuous typing is one undo group");
        Thread.Sleep(800);
        Pump();
        TypeGroup("three");
        Assert(Field<Stack<string>>(window, "_undoHistory").Count == 2, "Idle pause starts a new typing undo group");
        Call(window, "UndoDocumentAction");
        Assert(Text(typedEditor).Contains("onetwo") && !Text(typedEditor).Contains("three"), "Undo removes only last typing group");

        var planeDoc = new XRedDocument { Kind = DocumentKind.Plane, Plane = new PlaneData() };
        Open(window, planeDoc);
        Call(window, "AddElement", "NoteSheet");
        var note = planeDoc.Plane!.Elements.Single();
        Assert(Math.Abs(FontSizing.GetDisplayFontSize(note.Template, note.FontSize) - 2) < 0.001, "Plane notebook starts at two cells");
        var sizes = (ComboBox)window.FindName("PlaneFontSizeBox");
        sizes.Text = "3";
        Call(window, "PlaneFontSize_Commit", sizes, new RoutedEventArgs());
        Assert(Math.Abs(FontSizing.GetDisplayFontSize(note.Template, note.FontSize) - 3) < 0.001, "Custom cell size applies");
        Call(window, "PlaneBold_Click", window, new RoutedEventArgs());
        Assert(note.IsBold, "Plane bold formatting");
        Call(window, "UndoDocumentAction");
        var active = Field<XRedDocument>(window, "_currentDocument");
        Assert(!active.Plane!.Elements.Single().IsBold, "Undo formatting");
        Assert(Math.Abs(FontSizing.GetDisplayFontSize(note.Template, active.Plane.Elements.Single().FontSize) - 3) < 0.001, "Undo formatting preserves size");

        var canvas = (InkCanvas)window.FindName("PlaneInkCanvas");
        canvas.Strokes.Add(Stroke(5900, 6200, 6300, 6200));
        canvas.Strokes.Add(Stroke(5600, 5600, 5700, 5700));
        Call(window, "ClipCollectedInk");
        Assert(canvas.Strokes.Count == 1, "Ink outside canvases is discarded");
        Assert(canvas.Strokes[0].StylusPoints.All(point => point.X >= 6000 && point.X <= 6960), "Crossing stroke is clipped at canvas boundary");
        var clip = canvas.Clip;
        Assert(clip.FillContains(new Point(6100, 6100)) && !clip.FillContains(new Point(5900, 6100)), "Ink input is limited to canvas geometry");

        Call(window, "AddElement", "Rectangle");
        var current = Field<XRedDocument>(window, "_currentDocument");
        Assert(current.Plane!.Elements.Count == 2, "Shape creation");
        Call(window, "DeleteSelectedElement");
        Assert(current.Plane.Elements.Count == 1, "Delete selected shape");
        Call(window, "UndoDocumentAction");
        Assert(Field<XRedDocument>(window, "_currentDocument").Plane!.Elements.Count == 2, "Undo shape deletion");

        var scroll = (ScrollViewer)window.FindName("PlaneScrollViewer");
        var scale = (ScaleTransform)window.FindName("PlaneScale");
        var anchor = new Point(300, 200);
        Layout(root);
        var world = new Point((scroll.HorizontalOffset + anchor.X) / scale.ScaleX, (scroll.VerticalOffset + anchor.Y) / scale.ScaleY);
        for (var step = 0; step < 20; step++)
            Call(window, "ApplyPlaneViewport", step % 2 == 0 ? 1.12 : 1.0, world, anchor);
        Assert(Math.Abs((scroll.HorizontalOffset + anchor.X) / scale.ScaleX - world.X) < 0.01, "Repeated zoom preserves horizontal anchor");
        Assert(Math.Abs((scroll.VerticalOffset + anchor.Y) / scale.ScaleY - world.Y) < 0.01, "Repeated zoom preserves vertical anchor");

        for (var column = -8; column <= 9; column++) Call(window, "AddTile", column, 0);
        var largePlane = Field<XRedDocument>(window, "_currentDocument").Plane!;
        Assert(largePlane.WorldOrigin > 6000, "Plane supports growth beyond original negative boundary");
        Assert(((Canvas)window.FindName("PlaneWorld")).Width > 12000, "Plane supports growth beyond original positive boundary");
        Assert(canvas.Strokes.Count == 1, "Expanding plane preserves ink");

        Layout(root);
        Call(window, "FitAllTiles", false);
        Pump();
        Layout(root);
        Call(window, "RenderMinimap");
        var map = (Canvas)window.FindName("MinimapCanvas");
        Assert(map.Children.Count >= 3, "Map renders canvases, objects, and viewport");
        Render(root, "plane.png");
        Field<DispatcherTimer>(window, "_autosaveTimer").Stop();
        app.Shutdown();
    }

    private static void Open(MainWindow window, XRedDocument doc)
    {
        Call(window, "OpenDocument", doc, System.IO.Path.Combine(AppContext.BaseDirectory, "Notebooks", "editor-test.xred"), true, true);
        Layout((FrameworkElement)window.Content);
        Pump();
        Layout((FrameworkElement)window.Content);
    }

    private static Stroke Stroke(double x1, double y1, double x2, double y2) =>
        new(new StylusPointCollection(new[] { new StylusPoint(x1, y1), new StylusPoint(x2, y2) }));

    private static string Text(RichTextBox editor) => new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1280, 820));
        root.Arrange(new Rect(0, 0, 1280, 820));
        root.UpdateLayout();
    }
    private static void Render(FrameworkElement root, string name)
    {
        Layout(root);
        var bitmap = new RenderTargetBitmap(1280, 820, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(stream);
    }
}
