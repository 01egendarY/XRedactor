using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += (_, _) =>
        {
            _autosaveTimer.Stop();
            SaveCurrentDocument(false);
        };

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
                Notebook = new NotebookData { Template = template }
            };
        }
        else
        {
            if (!double.TryParse(PlaneWidthBox.Text, out var width) ||
                !double.TryParse(PlaneHeightBox.Text, out var height) ||
                width is < 320 or > 4000 || height is < 240 or > 4000)
            {
                MessageBox.Show("Размер холста: ширина 320–4000, высота 240–4000.",
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

    private void OpenDocument(XRedDocument document, string path)
    {
        _isLoading = true;
        _currentDocument = document;
        _currentPath = path;
        _undoHistory.Clear();
        _redoHistory.Clear();
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
            ConfigurePlane(document.Plane!);
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
        _autosaveTimer.Start();
    }

    private void PushUndoSnapshot()
    {
        if (_currentDocument is null || _isLoading)
        {
            return;
        }

        CaptureActiveEditor();
        _undoHistory.Push(JsonSerializer.Serialize(_currentDocument, _historyJsonOptions));
        if (_undoHistory.Count > 60)
        {
            var keep = _undoHistory.Reverse().Skip(1).ToArray();
            _undoHistory.Clear();
            foreach (var item in keep.Reverse())
            {
                _undoHistory.Push(item);
            }
        }
        _redoHistory.Clear();
    }

    private void UndoDocumentAction()
    {
        if (_currentDocument is null || _undoHistory.Count == 0)
        {
            return;
        }

        CaptureActiveEditor();
        _redoHistory.Push(JsonSerializer.Serialize(_currentDocument, _historyJsonOptions));
        RestoreSnapshot(_undoHistory.Pop());
    }

    private void RedoDocumentAction()
    {
        if (_currentDocument is null || _redoHistory.Count == 0)
        {
            return;
        }

        CaptureActiveEditor();
        _undoHistory.Push(JsonSerializer.Serialize(_currentDocument, _historyJsonOptions));
        RestoreSnapshot(_redoHistory.Pop());
    }

    private void RestoreSnapshot(string json)
    {
        var restored = JsonSerializer.Deserialize<XRedDocument>(json, _historyJsonOptions);
        if (restored is null)
        {
            return;
        }

        var undoItems = _undoHistory.ToArray();
        var redoItems = _redoHistory.ToArray();
        _currentDocument = restored;
        OpenDocument(restored, _currentPath!);
        foreach (var item in undoItems.Reverse()) _undoHistory.Push(item);
        foreach (var item in redoItems.Reverse()) _redoHistory.Push(item);
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
        else if (control && e.Key == Key.Z && !NotebookTextEditor.IsKeyboardFocusWithin)
        {
            if (shift) RedoDocumentAction(); else UndoDocumentAction();
            e.Handled = true;
        }
        else if (control && e.Key == Key.Y && !NotebookTextEditor.IsKeyboardFocusWithin)
        {
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
            DeleteSelectedElement();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _autosaveTimer.Stop();
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
