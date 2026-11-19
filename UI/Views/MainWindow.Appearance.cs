using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Underfloor_heating_generator.UI.Settings;
using WpfControl = System.Windows.Controls.Control;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Underfloor_heating_generator.UI.Views
{
    public partial class MainWindow
    {
        private bool appearanceReady;
        private bool busy;
        private string activePalette;
        private WpfControl firstInvalidField;

        private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
        {
            ["Canvas"] = ("#F2F5F7", "#161C21"),
            ["Surface"] = ("#FFFFFF", "#20282E"),
            ["Elevated"] = ("#EAF0F3", "#29353D"),
            ["Outline"] = ("#D5DFE4", "#3E505A"),
            ["FieldOutline"] = ("#7C9099", "#849AA5"),
            ["Ink"] = ("#152630", "#F0F5F7"),
            ["Muted"] = ("#4A616C", "#B7C8D0"),
            ["Accent"] = ("#086C64", "#7AE3D1"),
            ["OnAccent"] = ("#FFFFFF", "#102824"),
            ["Danger"] = ("#A12832", "#FFABB0"),
            ["Success"] = ("#16644E", "#8DE4BB"),
            ["Warning"] = ("#74500F", "#F4C980")
        };

        private void InitializeAppearance(WarmFloorWindowSettings settings)
        {
            ThemeSelection.SelectedIndex = settings?.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
            ScaleSelection.SelectedIndex = settings?.Scale switch { 125 => 1, 150 => 2, 200 => 3, _ => 0 };
            RememberWindow.IsChecked = settings?.RememberWindow ?? true;
            appearanceReady = true;
            ApplyAppearance();
            DwgScroll.ScrollChanged += (_, _) => FitForm(DwgScroll, DwgForm);
            AreaScroll.ScrollChanged += (_, _) => FitForm(AreaScroll, AreaForm);
            SettingsPage.ScrollChanged += (_, _) => FitForm(SettingsPage, SettingsForm);
            SourceInitialized += (_, _) =>
            {
                ApplyCaption();
                if (RememberWindow.IsChecked == true)
                    RestoreWindowBounds(settings);
            };
            Activated += (_, _) => ApplyAppearance();
            SystemParameters.StaticPropertyChanged += SystemAppearanceChanged;
            Closed += (_, _) => SystemParameters.StaticPropertyChanged -= SystemAppearanceChanged;
            AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler((sender, args) =>
                {
                    if (args.OriginalSource is System.Windows.Controls.ComboBox comboBox)
                        SetFieldError(comboBox, null);
                }));
            AddHandler(WpfTextBox.TextChangedEvent, new TextChangedEventHandler((sender, args) =>
            {
                if (args.OriginalSource is WpfTextBox textBox)
                {
                    WpfControl field = textBox.TemplatedParent as System.Windows.Controls.ComboBox;
                    SetFieldError(field ?? textBox, null);
                }
            }));
        }

        private void SystemAppearanceChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(() => ApplyAppearance(force: true)));

        private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (appearanceReady)
                ApplyAppearance();
        }

        private void ApplyAppearance(bool force = false)
        {
            string preference = GetOptionTag(ThemeSelection);
            bool dark = preference == "Dark" || (preference == "System" && WindowsUsesDarkApps());
            string palette = SystemParameters.HighContrast ? "Contrast" : dark ? "Dark" : "Light";
            if (activePalette != palette || force)
                ApplyPalette(palette);

            FitForm(DwgScroll, DwgForm);
            FitForm(AreaScroll, AreaForm);
            FitForm(SettingsPage, SettingsForm);
        }

        private void ApplyPalette(string palette)
        {
            activePalette = palette;
            foreach (var entry in Palette)
            {
                if (palette == "Contrast")
                {
                    Resources[entry.Key] = entry.Key switch
                    {
                        "Canvas" or "Surface" or "Elevated" => SystemColors.WindowBrush,
                        "Accent" => SystemColors.HighlightBrush,
                        "OnAccent" => SystemColors.HighlightTextBrush,
                        _ => SystemColors.WindowTextBrush
                    };
                }
                else
                {
                    var color = (Color)ColorConverter.ConvertFromString(palette == "Dark" ? entry.Value.Dark : entry.Value.Light);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    Resources[entry.Key] = brush;
                }
            }
            ApplyCaption();
        }

        private void FitForm(ScrollViewer viewport, FrameworkElement form)
        {
            double scale = GetScale() / 100d;
            if (form.LayoutTransform is not ScaleTransform current || current.ScaleX != scale)
                form.LayoutTransform = new ScaleTransform(scale, scale);
            double width = viewport.ViewportWidth - form.Margin.Left - form.Margin.Right;
            bool overflow = scale > 1 && form.MinWidth * scale > width;
            viewport.HorizontalScrollBarVisibility = overflow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            double targetWidth = overflow ? form.MinWidth : double.NaN;
            if (!form.Width.Equals(targetWidth))
                form.Width = targetWidth;
        }

        private static bool WindowsUsesDarkApps()
        {
            try
            {
                return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1) is int value && value == 0;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }

        private void ApplyCaption()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            int dark = activePalette == "Dark" ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private static string GetOptionTag(System.Windows.Controls.ComboBox combo) =>
            (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";

        private int GetScale() => int.TryParse(GetOptionTag(ScaleSelection), CultureInfo.InvariantCulture, out int scale)
            ? scale : 100;

        private void RestoreWindowBounds(WarmFloorWindowSettings settings)
        {
            if (settings?.WindowWidth is not double width || settings.WindowHeight is not double height ||
                settings.WindowLeft is not double left || settings.WindowTop is not double top ||
                !double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(left) || !double.IsFinite(top))
                return;
            // Restore within the current monitor's work area, including when another monitor was disconnected.
            var handle = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var transform = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            var workArea = new Rect(
                transform.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top)),
                transform.Transform(new System.Windows.Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom)));
            Width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, workArea.Width));
            Height = Math.Clamp(height, MinHeight, Math.Max(MinHeight, workArea.Height));
            Left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
            Top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        }

        private void SaveAppearance(WarmFloorWindowSettings settings)
        {
            settings.Theme = GetOptionTag(ThemeSelection);
            settings.Scale = GetScale();
            settings.RememberWindow = RememberWindow.IsChecked == true;
            if (!settings.RememberWindow)
                return;
            Rect bounds = RestoreBounds;
            if (bounds.IsEmpty)
                return;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
        }

        private void Settings_Click(object sender, RoutedEventArgs e) =>
            SetSettingsOpen(SettingsPage.Visibility != Visibility.Visible);

        private void SetSettingsOpen(bool open)
        {
            SettingsPage.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            Modes.Visibility = Footer.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            SettingsButton.Content = open ? "Готово" : "Настройки";
            ViewSubtitle.Text = open ? "Параметры интерфейса" : (Modes.SelectedItem as TabItem)?.Tag as string;
            if (open)
                ThemeSelection.Focus();
            else
                SettingsButton.Focus();
        }

        private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            e.Handled = HandleShortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        }

        private bool HandleShortcut(Key key, ModifierKeys modifiers)
        {
            if (key == Key.Escape && modifiers == ModifierKeys.None)
            {
                if (Keyboard.FocusedElement is FrameworkElement focused &&
                    (focused as System.Windows.Controls.ComboBox ?? focused.TemplatedParent as System.Windows.Controls.ComboBox)
                    is { IsDropDownOpen: true })
                    return false;
                if (!busy)
                {
                    if (SettingsPage.Visibility == Visibility.Visible)
                        SetSettingsOpen(false);
                    else
                        Close();
                }
            }
            else if (!busy && key == Key.OemComma && modifiers == ModifierKeys.Control)
                SetSettingsOpen(SettingsPage.Visibility != Visibility.Visible);
            else if (!busy && modifiers == ModifierKeys.Alt && (key == Key.D || key == Key.P))
            {
                if (SettingsPage.Visibility == Visibility.Visible)
                    SetSettingsOpen(false);
                Modes.SelectedIndex = key == Key.P ? 1 : 0;
                (Modes.SelectedItem as TabItem)?.Focus();
            }
            else if (!busy && key == Key.Enter && modifiers == ModifierKeys.Control &&
                     SettingsPage.Visibility != Visibility.Visible)
            {
                var action = Modes.SelectedIndex == 1 ? CreateAreaWarmFloor : CreateWarmFloor;
                if (action.IsEnabled)
                    action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            else
                return false;
            return true;
        }

        private void Modes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, Modes) || CreateAreaWarmFloor == null)
                return;
            bool areaSelected = Modes.SelectedIndex == 1;
            ViewSubtitle.Text = (Modes.SelectedItem as TabItem)?.Tag as string;
            CreateWarmFloor.Visibility = areaSelected ? Visibility.Collapsed : Visibility.Visible;
            CreateAreaWarmFloor.Visibility = areaSelected ? Visibility.Visible : Visibility.Collapsed;
            HideStatus();
        }

        private void HideStatus()
        {
            StatusPanel.Visibility = Visibility.Collapsed;
            Footer.InvalidateMeasure();
        }

        private void UpdateAvailability()
        {
            Modes.IsEnabled = SettingsButton.IsEnabled = !busy;
            CreateWarmFloor.IsEnabled = !busy && SelectDrawingDWG.HasItems && PipeSelection.HasItems && SystemTypePipe.HasItems;
            CreateAreaWarmFloor.IsEnabled = !busy && StubPipeSelection.HasItems && StubSystemTypePipe.HasItems;
        }

        private void StartOperation(bool showProgress)
        {
            busy = true;
            UpdateAvailability();
            ContourProgressBar.Value = 0;
            ProgressPercent.Text = showProgress ? "0%" : "";
            ProgressPercent.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
            ContourProgressBar.IsIndeterminate = !showProgress && SystemParameters.ClientAreaAnimation;
            ProgressPanel.Visibility = Visibility.Visible;
            ShowStatus("Revit выполняет операцию…", null);
        }

        private void FinishOperation()
        {
            busy = false;
            ProgressPanel.Visibility = Visibility.Collapsed;
            ContourProgressBar.IsIndeterminate = false;
            Footer.InvalidateMeasure();
            UpdateAvailability();
        }

        private void SetFieldError(WpfControl field, string message)
        {
            if (string.IsNullOrEmpty(field.Name) || FindName(field.Name + "Error") is not TextBlock text)
                return;
            text.Text = message ?? "";
            System.Windows.Automation.AutomationProperties.SetHelpText(field, message ?? "");
            if (message == null)
                field.ClearValue(WpfControl.BorderBrushProperty);
            else
            {
                field.SetResourceReference(WpfControl.BorderBrushProperty, "Danger");
                firstInvalidField ??= field;
            }
        }

        private bool ShowValidation(string message)
        {
            if (firstInvalidField == null)
                return true;
            firstInvalidField.BringIntoView();
            firstInvalidField.Focus();
            ShowStatus(message, false);
            return false;
        }

        private static void Announce(TextBlock text) =>
            UIElementAutomationPeer.FromElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
