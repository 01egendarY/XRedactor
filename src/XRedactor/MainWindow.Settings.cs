using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XRedactor.Services;

namespace XRedactor;

public partial class MainWindow
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XRedactor", "settings.json");

    private string _selectedTheme = "Purple";
    private int _autosaveIntervalSeconds = 5;
    private bool _autosaveEnabled = true;
    private bool _fitPlaneOnOpen = true;
    private bool _confirmCanvasDelete = true;
    private bool _loadingPersistentSettings;
    private double _zoomSpeed = 1.12;
    private double _homeSidebarWidth = 390;

    private double ZoomSpeedMultiplier => _zoomSpeed;

    private void InitializePersistentSettings()
    {
        UserSettings settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings()
                : new UserSettings();
        }
        catch
        {
            settings = new UserSettings();
        }

        _selectedTheme = settings.Theme;
        _autosaveEnabled = settings.AutosaveEnabled;
        _autosaveIntervalSeconds = settings.AutosaveIntervalSeconds is 2 or 5 or 10 or 30
            ? settings.AutosaveIntervalSeconds : 5;
        _fitPlaneOnOpen = settings.FitPlaneOnOpen;
        _confirmCanvasDelete = settings.ConfirmCanvasDelete;
        _zoomSpeed = Math.Clamp(settings.ZoomSpeed, 1.04, 1.24);
        _homeSidebarWidth = Math.Clamp(settings.HomeSidebarWidth, 300, 650);

        _loadingPersistentSettings = true;
        ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _selectedTheme, StringComparison.Ordinal));
        if (ThemeBox.SelectedItem is null)
        {
            _selectedTheme = "Purple";
            ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _selectedTheme);
        }

        ApplyTheme(_selectedTheme);
        AutosaveEnabledBox.IsChecked = _autosaveEnabled;
        AutosaveIntervalBox.SelectedItem = AutosaveIntervalBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == _autosaveIntervalSeconds.ToString());
        FitPlaneOnOpenBox.IsChecked = _fitPlaneOnOpen;
        ConfirmCanvasDeleteBox.IsChecked = _confirmCanvasDelete;
        ZoomSpeedSlider.Value = 1 + (_zoomSpeed - 1.04) / 0.05;
        HomeSidebarColumn.Width = new GridLength(_homeSidebarWidth);

        FitPlaneOnOpenBox.Checked += FitPreference_Changed;
        FitPlaneOnOpenBox.Unchecked += FitPreference_Changed;
        ConfirmCanvasDeleteBox.Checked += DeleteConfirmationPreference_Changed;
        ConfirmCanvasDeleteBox.Unchecked += DeleteConfirmationPreference_Changed;
        ZoomSpeedSlider.ValueChanged += ZoomSpeedPreference_Changed;
        _loadingPersistentSettings = false;
        _autosaveTimer.Interval = TimeSpan.FromSeconds(_autosaveIntervalSeconds);
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int lightTheme && lightTheme == 0;
        }
        catch
        {
            return false;
        }
    }

    private void PersistSettings()
    {
        if (_loadingPersistentSettings)
        {
            return;
        }

        try
        {
            if (HomeSidebarColumn is not null && HomeSidebarColumn.Width.IsAbsolute)
            {
                _homeSidebarWidth = Math.Clamp(HomeSidebarColumn.Width.Value, 300, 650);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new UserSettings
            {
                Theme = _selectedTheme,
                AutosaveEnabled = _autosaveEnabled,
                AutosaveIntervalSeconds = _autosaveIntervalSeconds,
                FitPlaneOnOpen = _fitPlaneOnOpen,
                ConfirmCanvasDelete = _confirmCanvasDelete,
                ZoomSpeed = _zoomSpeed,
                HomeSidebarWidth = _homeSidebarWidth
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Preferences are best-effort and must not prevent document saving or shutdown.
        }
    }

    private void AutosavePreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingPersistentSettings)
        {
            return;
        }

        _autosaveEnabled = AutosaveEnabledBox.IsChecked == true;
        if (AutosaveIntervalBox.SelectedItem is ComboBoxItem { Tag: string seconds } &&
            int.TryParse(seconds, out var interval))
        {
            _autosaveIntervalSeconds = interval;
        }

        _autosaveTimer.Interval = TimeSpan.FromSeconds(_autosaveIntervalSeconds);
        if (!_autosaveEnabled)
        {
            _autosaveTimer.Stop();
        }
        else if (_isDirty)
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        PersistSettings();
    }

    private void FitPreference_Changed(object sender, RoutedEventArgs e)
    {
        _fitPlaneOnOpen = FitPlaneOnOpenBox.IsChecked == true;
        PersistSettings();
    }

    private void DeleteConfirmationPreference_Changed(object sender, RoutedEventArgs e)
    {
        _confirmCanvasDelete = ConfirmCanvasDeleteBox.IsChecked == true;
        PersistSettings();
    }

    private void ZoomSpeedPreference_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _zoomSpeed = Math.Clamp(1.04 + (ZoomSpeedSlider.Value - 1) * 0.05, 1.04, 1.24);
        PersistSettings();
    }

    private void RenameSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentList.SelectedItem is not DocumentEntry entry || entry.IsDamaged)
        {
            return;
        }

        var dialog = new Window
        {
            Title = "Переименовать документ",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["PanelBackground"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"]
        };
        var input = new TextBox { Text = entry.Name, MinWidth = 300, Margin = new Thickness(0, 8, 0, 16) };
        input.SelectAll();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", MinWidth = 90, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var accept = new Button { Content = "Переименовать", MinWidth = 120, IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = "Новое название" });
        content.Children.Add(input);
        content.Children.Add(buttons);
        dialog.Content = content;
        accept.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => input.Focus();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
        {
            MessageBox.Show("Название должно содержать от 1 до 160 символов.",
                "XRedactor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _store.Rename(entry.Path, name);
            RefreshDocumentList();
        }
        catch (Exception exception)
        {
            ShowStorageError(exception);
        }
    }

    private sealed class UserSettings
    {
        public string Theme { get; set; } = "Purple";
        public bool AutosaveEnabled { get; set; } = true;
        public int AutosaveIntervalSeconds { get; set; } = 5;
        public bool FitPlaneOnOpen { get; set; } = true;
        public bool ConfirmCanvasDelete { get; set; } = true;
        public double ZoomSpeed { get; set; } = 1.12;
        public double HomeSidebarWidth { get; set; } = 390;
    }
}
