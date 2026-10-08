using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using XRedactor.Models;
using XRedactor.Services;

namespace XRedactor;

public partial class MainWindow : Window
{
    private readonly DocumentStore _store;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly JsonSerializerOptions _historyJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Stack<string> _undoHistory = [];
    private readonly Stack<string> _redoHistory = [];
    private XRedDocument? _currentDocument;
    private string? _currentPath;
    private bool _isLoading;
    private bool _isDirty;

    public MainWindow()
    {
        InitializeComponent();
        NotebookTextEditor.LostKeyboardFocus += NotebookTextEditor_LostKeyboardFocus;
        foreach (var button in FindVisualChildren<Button>(NotebookToolbar).Concat(FindVisualChildren<Button>(PlaneToolbar)).Concat(FindVisualChildren<Button>(ContextToolbar)))
            button.Focusable = false;
        DataObject.AddPastingHandler(NotebookTextEditor, NotebookTextEditor_Pasting);
        ThemeBox.SelectionChanged += ThemeBox_SelectionChanged;
        Activated += (_, _) =>
        {
            if (_selectedTheme == "System") { ApplyTheme("System"); RefreshThemeDependentVisuals(); }
        };

        try
        {
            _store = new DocumentStore();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось создать папку Notebooks рядом с программой.\n\n{exception.Message}\n\n" +
                "Переместите XRedactor в папку, доступную для записи.",
                "XRedactor", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_autosaveIntervalSeconds) };
        _autosaveTimer.Tick += (_, _) =>
        {
            _autosaveTimer.Stop();
            SaveCurrentDocument(false);
        };

        InitializePersistentSettings();

        StorageLocationText.Text = $"Файлы: {_store.DirectoryPath}";
        RefreshDocumentList();
    }

    private void RefreshDocumentList()
    {
        DocumentList.ItemsSource = _store.ListDocuments();
    }

    private void ShowCreateDialog_Click(object sender, RoutedEventArgs e)
    {
        NewDocumentName.Text = "Новый документ";
        NotebookKindRadio.IsChecked = true;
        CreateOverlay.Visibility = Visibility.Visible;
        NewDocumentName.Focus();
        NewDocumentName.SelectAll();
    }

    private void CancelCreate_Click(object sender, RoutedEventArgs e)
    {
        CreateOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string theme })
        {
            _selectedTheme = theme;
            ApplyTheme(theme);
            RefreshThemeDependentVisuals();
            PersistSettings();
        }
    }

    private void ApplyTheme(string theme)
    {
        if (theme == "System")
        {
            theme = IsSystemDarkTheme() ? "Black" : "White";
        }
        var palette = theme switch
        {
            "White" => (
                App: "#FFF7F3EC",
                Panel: "#FFFFFFFF",
                Raised: "#FFF1ECE3",
                Workspace: "#FFEDE8E0",
                World: "#FFE5DFD4",
                Primary: "#FF242128",
                Secondary: "#FF6E6873",
                Accent: "#FFB85454"),
            "Black" => (
                App: "#FF08080A",
                Panel: "#FF111114",
                Raised: "#FF1B1B20",
                Workspace: "#FF09090D",
                World: "#FF0D0D12",
                Primary: "#FFF5F1EB",
                Secondary: "#FFAAA5AE",
                Accent: "#FFE45D5D"),
            "Gray" => (
                App: "#FF1F2024",
                Panel: "#FF2A2B31",
                Raised: "#FF363740",
                Workspace: "#FF18191D",
                World: "#FF202127",
                Primary: "#FFF4F1EC",
                Secondary: "#FFBDB8C2",
                Accent: "#FFCF6A55"),
            "DarkBlue" => (
                App: "#FF101822",
                Panel: "#FF172333",
                Raised: "#FF223247",
                Workspace: "#FF0B121B",
                World: "#FF101A27",
                Primary: "#FFF2F6FF",
                Secondary: "#FFAEB9C9",
                Accent: "#FF5D8CFF"),
            _ => (
                App: "#FF17131E",
                Panel: "#FF241F2D",
                Raised: "#FF30283B",
                Workspace: "#FF111015",
                World: "#FF15141A",
                Primary: "#FFF8F2FF",
                Secondary: "#FFB9AFC7",
                Accent: "#FF9A6CFF")
        };

        SetBrush("AppBackground", palette.App);
        SetBrush("PanelBackground", palette.Panel);
        SetBrush("PanelRaised", palette.Raised);
        SetBrush("WorkspaceBackground", palette.Workspace);
        SetBrush("PlaneWorldBackground", palette.World);
        SetBrush("PrimaryText", palette.Primary);
        SetBrush("SecondaryText", palette.Secondary);
        SetBrush("Accent", palette.Accent);
    }

    private static void SetBrush(string key, string color)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void RefreshThemeDependentVisuals()
    {
        if (NotebookTextModeButton is not null)
        {
            SetNotebookMode(_notebookMode);
            UpdateNotebookFormattingButtons();
        }

        if (PlaneToolbar is not null)
        {
            UpdatePlaneToolButtons();
            UpdatePlaneFormattingButtons();
            if (_selectedElementView is not null)
            {
                SetElementSelectionVisual(_selectedElementView, true);
            }
        }

        if (_currentDocument?.Kind == DocumentKind.Plane)
        {
            RenderTiles();
            RenderElements();
            RenderMinimap();
        }
    }

    private void DocumentKind_Changed(object sender, RoutedEventArgs e)
    {
        if (NotebookTemplateOptions is null || PlaneSizeOptions is null)
        {
            return;
        }

        var notebook = NotebookKindRadio.IsChecked == true;
        NotebookTemplateOptions.Visibility = notebook ? Visibility.Visible : Visibility.Collapsed;
        PlaneSizeOptions.Visibility = notebook ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CreateDocument_Click(object sender, RoutedEventArgs e)
    {
        var name = NewDocumentName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Введите название документа.", "XRedactor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        XRedDocument document;
        if (NotebookKindRadio.IsChecked == true)
        {
            var selected = NotebookTemplateBox.SelectedItem as ComboBoxItem;
            Enum.TryParse(selected?.Tag?.ToString(), out NotebookTemplate template);
            document = new XRedDocument
            {
                Name = name,
                Kind = DocumentKind.Notebook,
                Notebook = new NotebookData { Template = template, FontSize = FontSizing.GetDefaultDisplayFontSize(template) }
            };
        }
        else
        {
            if (!double.TryParse(PlaneWidthBox.Text, out var width) ||
                !double.TryParse(PlaneHeightBox.Text, out var height) ||
                width is < 320 or > 4000 || height is < 240 or > 4000)
            {
                MessageBox.Show("Размер холста: ширина 320-4000, высота 240-4000.",
                    "XRedactor", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            document = new XRedDocument
            {
                Name = name,
                Kind = DocumentKind.Plane,
                Plane = new PlaneData { TileWidth = width, TileHeight = height }
            };
        }

        try
        {
            var path = _store.Save(document);
            CreateOverlay.Visibility = Visibility.Collapsed;
            OpenDocument(document, path);
        }
        catch (Exception exception)
        {
            ShowStorageError(exception);
        }
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e) => OpenSelectedDocument();

    private void DocumentList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedDocument();

    private void OpenSelectedDocument()
    {
        if (DocumentList.SelectedItem is not DocumentEntry entry)
        {
            return;
        }

        if (entry.IsDamaged)
        {
            MessageBox.Show("Этот файл повреждён или создан несовместимой версией. Оригинал не изменён.",
                "XRedactor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            OpenDocument(_store.Load(entry.Path), entry.Path);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Не удалось открыть документ.\n\n{exception.Message}",
                "XRedactor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenDocument(XRedDocument document, string path, bool resetHistory = true, bool fitPlaneOnOpen = true)
    {
        _isLoading = true;
        _currentDocument = document;
        _currentPath = path;
        if (resetHistory)
        {
            _undoHistory.Clear();
            _redoHistory.Clear();
        }

        _inkSnapshotCaptured = false;
        _notebookTextSnapshotCaptured = false;
        EditorTitle.Text = document.Name;
        EditorKindLabel.Text = document.Kind == DocumentKind.Notebook ? "Блокнот" : "Плоскость";
        HomeView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;

        if (document.Kind == DocumentKind.Notebook)
        {
            NotebookToolbar.Visibility = Visibility.Visible;
            PlaneToolbar.Visibility = Visibility.Collapsed;
            NotebookScrollViewer.Visibility = Visibility.Visible;
            PlaneWorkspace.Visibility = Visibility.Collapsed;
            ConfigureNotebook(document.Notebook!);
        }
        else
        {
            NotebookToolbar.Visibility = Visibility.Collapsed;
            PlaneToolbar.Visibility = Visibility.Visible;
            NotebookScrollViewer.Visibility = Visibility.Collapsed;
            PlaneWorkspace.Visibility = Visibility.Visible;
            ConfigurePlane(document.Plane!, fitPlaneOnOpen && _fitPlaneOnOpen);
        }

        _isDirty = false;
        SaveStatusText.Text = "Сохранено";
        _isLoading = false;
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentList.SelectedItem is not DocumentEntry entry)
        {
            return;
        }

        if (MessageBox.Show($"Удалить «{entry.Name}» без возможности восстановления?",
                "Удаление документа", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _store.Delete(entry.Path);
            RefreshDocumentList();
        }
        catch (Exception exception)
        {
            ShowStorageError(exception);
        }
    }

    private void BackToHome_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentDocument(false);
        _currentDocument = null;
        _currentPath = null;
        EditorView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
        RefreshDocumentList();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrentDocument(true);

    private void SaveCurrentDocument(bool showConfirmation)
    {
        if (_currentDocument is null || _currentPath is null)
        {
            return;
        }

        try
        {
            CaptureActiveEditor();
            _currentPath = _store.Save(_currentDocument, _currentPath);
            _isDirty = false;
            SaveStatusText.Text = showConfirmation ? "Сохранено сейчас" : "Сохранено";
        }
        catch (Exception exception)
        {
            SaveStatusText.Text = "Ошибка сохранения";
            ShowStorageError(exception);
        }
    }

    private void CaptureActiveEditor()
    {
        if (_currentDocument?.Kind == DocumentKind.Notebook)
        {
            CaptureNotebook(_currentDocument.Notebook!);
        }
        else if (_currentDocument?.Kind == DocumentKind.Plane)
        {
            CapturePlane(_currentDocument.Plane!);
        }
    }

    private void MarkDirty()
    {
        if (_isLoading || _currentDocument is null)
        {
            return;
        }

        _isDirty = true;
        SaveStatusText.Text = "Есть изменения";
        _autosaveTimer.Stop();
        if (_autosaveEnabled)
        {
            _autosaveTimer.Start();
        }
    }

    private void PushUndoSnapshot()
    {
        if (_currentDocument is null || _isLoading)
        {
            return;
        }

        CaptureActiveEditor();
        _undoHistory.Push(JsonSerializer.Serialize(_currentDocument, _historyJsonOptions));
        TrimUndoHistory();
        _redoHistory.Clear();
    }

    private void TrimUndoHistory()
    {
        if (_undoHistory.Count <= 60)
        {
            return;
        }

        var newestItemsInOldestFirstOrder = _undoHistory.Take(60).Reverse().ToArray();
        _undoHistory.Clear();
        foreach (var item in newestItemsInOldestFirstOrder)
        {
            _undoHistory.Push(item);
        }
    }

    private void UndoDocumentAction()
    {
        if (_currentDocument is null || _undoHistory.Count == 0)
        {
            return;
        }

        CaptureActiveEditor();
        var current = JsonSerializer.Serialize(_currentDocument, _historyJsonOptions);
        while (_undoHistory.TryPeek(out var candidate) && SameHistoryContent(current, candidate)) _undoHistory.Pop();
        if (_undoHistory.Count == 0) return;
        _redoHistory.Push(current);
        RestoreSnapshot(_undoHistory.Pop());
    }

    private static bool SameHistoryContent(string first, string second)
    {
        static JsonNode? Content(string json)
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject document)
            {
                document.Remove("ModifiedUtc");
                if (document["Plane"] is JsonObject plane)
                {
                    var origin = plane["WorldOrigin"]?.GetValue<double>() ?? 6000;
                    if (plane["Strokes"] is JsonArray strokes)
                        foreach (var stroke in strokes.OfType<JsonObject>())
                            if (stroke["Points"] is JsonArray points)
                                foreach (var point in points.OfType<JsonObject>())
                                {
                                    point["X"] = Math.Round((point["X"]?.GetValue<double>() ?? 0) - origin, 6);
                                    point["Y"] = Math.Round((point["Y"]?.GetValue<double>() ?? 0) - origin, 6);
                                }
                    plane.Remove("WorldOrigin");
                    plane.Remove("Zoom");
                    plane.Remove("HorizontalOffset");
                    plane.Remove("VerticalOffset");
                }
            }
            return node;
        }
        return JsonNode.DeepEquals(Content(first), Content(second));
    }

    private void RedoDocumentAction()
    {
        if (_currentDocument is null || _redoHistory.Count == 0)
        {
            return;
        }

        CaptureActiveEditor();
        _undoHistory.Push(JsonSerializer.Serialize(_currentDocument, _historyJsonOptions));
        TrimUndoHistory();
        RestoreSnapshot(_redoHistory.Pop());
    }

    private void RestoreSnapshot(string json)
    {
        var restored = JsonSerializer.Deserialize<XRedDocument>(json, _historyJsonOptions);
        if (restored is null)
        {
            return;
        }

        var notebookMode = _notebookMode;
        var planeTool = _planeTool;
        var notebookEditing = NotebookTextEditor.IsKeyboardFocusWithin;
        var notebookCaret = NotebookTextEditor.Document.ContentStart.GetOffsetToPosition(NotebookTextEditor.CaretPosition);
        var notebookHorizontal = NotebookScrollViewer.HorizontalOffset;
        var notebookVertical = NotebookScrollViewer.VerticalOffset;
        var planeEditing = TryCaptureActiveElementTextEdit(out var planeCaret);
        if (restored.Plane is not null && _currentDocument?.Plane is not null)
        {
            restored.Plane.Zoom = PlaneScale.ScaleX;
            var originDelta = restored.Plane.WorldOrigin - WorldOrigin;
            restored.Plane.HorizontalOffset = PlaneScrollViewer.HorizontalOffset + originDelta * PlaneScale.ScaleX;
            restored.Plane.VerticalOffset = PlaneScrollViewer.VerticalOffset + originDelta * PlaneScale.ScaleY;
        }
        var selectedElementId = _selectedElement?.Id;
        _currentDocument = restored;
        OpenDocument(restored, _currentPath!, resetHistory: false, fitPlaneOnOpen: false);

        if (restored.Kind == DocumentKind.Notebook)
        {
            SetNotebookMode(notebookMode);
            if (notebookEditing)
            {
                NotebookTextEditor.Focus();
                NotebookTextEditor.CaretPosition = NotebookTextEditor.Document.ContentStart.GetPositionAtOffset(Math.Min(notebookCaret, NotebookTextEditor.Document.ContentStart.GetOffsetToPosition(NotebookTextEditor.Document.ContentEnd))) ?? NotebookTextEditor.Document.ContentEnd;
            }
            Dispatcher.BeginInvoke(() =>
            {
                NotebookScrollViewer.ScrollToHorizontalOffset(notebookHorizontal);
                NotebookScrollViewer.ScrollToVerticalOffset(notebookVertical);
            });
        }
        else
        {
            SetPlaneTool(planeTool);
            if (selectedElementId is Guid id &&
                restored.Plane?.Elements.FirstOrDefault(element => element.Id == id) is { } element)
            {
                SelectElement(element);
                RestoreActiveElementTextEdit(planeEditing, planeCaret);
            }
        }

        _notebookTextSnapshotCaptured = false;
        _inkSnapshotCaptured = false;
        MarkDirty();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CreateOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CreateOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (SettingsOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (EditorView.Visibility != Visibility.Visible)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (control && e.Key == Key.S)
        {
            SaveCurrentDocument(true);
            e.Handled = true;
        }
        else if (control && e.Key == Key.Z)
        {
            if (ShouldLetTextEditorHandleUndo())
            {
                return;
            }

            if (shift)
            {
                RedoDocumentAction();
            }
            else
            {
                UndoDocumentAction();
            }

            e.Handled = true;
        }
        else if (control && e.Key == Key.Y)
        {
            if (ShouldLetTextEditorHandleUndo())
            {
                return;
            }

            RedoDocumentAction();
            e.Handled = true;
        }
        else if (_currentDocument?.Kind == DocumentKind.Plane && control && e.Key == Key.D0)
        {
            SetPlaneZoom(1.0);
            e.Handled = true;
        }
        else if (_currentDocument?.Kind == DocumentKind.Plane && control && e.Key == Key.D1)
        {
            FitAllTiles();
            e.Handled = true;
        }
        else if (_currentDocument?.Kind == DocumentKind.Plane && e.Key == Key.Delete)
        {
            if (Keyboard.FocusedElement is TextBox { IsReadOnly: false })
            {
                return;
            }

            DeleteSelectedElement();
            e.Handled = true;
        }
        else if (control && (e.Key is Key.B or Key.I or Key.U) && _currentDocument is not null)
        {
            if (_currentDocument?.Kind == DocumentKind.Notebook)
            {
                if (e.Key == Key.B) Bold_Click(this, new RoutedEventArgs());
                else if (e.Key == Key.I) Italic_Click(this, new RoutedEventArgs());
                else Underline_Click(this, new RoutedEventArgs());
            }
            else if (_currentDocument?.Kind == DocumentKind.Plane)
            {
                if (e.Key == Key.B) PlaneBold_Click(this, new RoutedEventArgs());
                else if (e.Key == Key.I) PlaneItalic_Click(this, new RoutedEventArgs());
                else PlaneUnderline_Click(this, new RoutedEventArgs());
            }

            e.Handled = true;
        }
    }

    private bool ShouldLetTextEditorHandleUndo()
    {
        return Keyboard.FocusedElement is TextBox { Tag: not CanvasElementData, IsReadOnly: false };
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _autosaveTimer.Stop();
        PersistSettings();
        if (_isDirty)
        {
            SaveCurrentDocument(false);
        }
    }

    private static void ShowStorageError(Exception exception)
    {
        MessageBox.Show($"Не удалось записать данные.\n\n{exception.Message}",
            "XRedactor", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
