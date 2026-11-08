using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Underfloor_heating_generator.UI.Settings;
using Underfloor_heating_generator.Application.Contracts;
using Underfloor_heating_generator.Application.Models;
using Underfloor_heating_generator.Infrastructure.Revit;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace Underfloor_heating_generator.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly IWarmFloorGenerator generator;
        private readonly IAreaWarmFloorGenerator areaGenerator;
        private readonly IFloorAreaPicker floorAreaPicker;
        private readonly RevitApiTaskQueue apiTaskQueue;
        private ElementOption selectedFloorArea;
        private ElementOption selectedStartPipe;
        private ElementOption selectedEndPipe;

        public MainWindow(IWarmFloorOptionsProvider optionsProvider, IWarmFloorGenerator generator,
            IAreaWarmFloorGenerator areaGenerator, IFloorAreaPicker floorAreaPicker,
            RevitApiTaskQueue apiTaskQueue)
        {
            InitializeComponent();
            this.generator = generator;
            this.areaGenerator = areaGenerator;
            this.floorAreaPicker = floorAreaPicker;
            this.apiTaskQueue = apiTaskQueue;

            PipeSelection.ItemsSource = optionsProvider.GetPipeTypes();
            SystemTypePipe.ItemsSource = optionsProvider.GetSystemTypes();
            SelectDrawingDWG.ItemsSource = optionsProvider.GetDrawings();

            StubPipeSelection.ItemsSource = PipeSelection.ItemsSource;
            StubSystemTypePipe.ItemsSource = SystemTypePipe.ItemsSource;
            SelectDefault(StubPipeSelection, 0);
            SelectDefault(StubSystemTypePipe, 0);
            SelectDefault(StubPipeDiameter, 0);
            SelectDefault(StubLayingHeight, 0);
            SelectDefault(WallOffset, 0);
            SelectDefault(PipeSpacing, 0);

            RestoreSettings();

            if (!optionsProvider.HasActiveLevel)
                ShowStatus("Активный вид не связан с уровнем. Переключитесь на план этажа.", false);
        }

        public void SetOwner(IntPtr ownerHandle) => new WindowInteropHelper(this).Owner = ownerHandle;

        private void RestoreSettings()
        {
            WarmFloorWindowSettings settings = WarmFloorWindowSettingsStore.Load();
            if (settings == null)
            {
                SelectDefault(PipeDiametr, 0);
                SelectDefault(PipeLayingHeight, 1);
                SelectDefault(PipeSelection, 0);
                SelectDefault(SystemTypePipe, 0);
                SelectDefault(SelectDrawingDWG, 0);
                return;
            }

            SaveChanges.IsChecked = settings.SaveValues;
            if (settings.SaveValues)
            {
                SelectById(PipeSelection, settings.PipeTypeId);
                SelectById(SystemTypePipe, settings.SystemTypeId);
                SelectById(SelectDrawingDWG, settings.DrawingId);
                SetEditableValue(PipeDiametr, settings.PipeDiameter);
                SetEditableValue(PipeLayingHeight, settings.LayingHeight);
                AlignDirectionsCheckBox.IsChecked = settings.AlignDirectionsToAllowedAngles;
            }
            else
            {
                ClearInputs();
            }

            SaveAreaValues.IsChecked = settings.SaveAreaValues;
            if (settings.SaveAreaValues)
            {
                SelectById(StubPipeSelection, settings.AreaPipeTypeId);
                SelectById(StubSystemTypePipe, settings.AreaSystemTypeId);
                if (settings.AreaPipeDiameter != null) SetEditableValue(StubPipeDiameter, settings.AreaPipeDiameter);
                if (settings.AreaLayingHeight != null) SetEditableValue(StubLayingHeight, settings.AreaLayingHeight);
                if (settings.AreaWallOffset != null) SetEditableValue(WallOffset, settings.AreaWallOffset);
                if (settings.AreaPipeSpacing != null) SetEditableValue(PipeSpacing, settings.AreaPipeSpacing);
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool save = SaveChanges.IsChecked == true;
            bool saveArea = SaveAreaValues.IsChecked == true;
            var settings = new WarmFloorWindowSettings
            {
                SaveValues = save,
                SaveAreaValues = saveArea
            };
            if (save)
            {
                settings.PipeTypeId = GetSelectedId(PipeSelection);
                settings.SystemTypeId = GetSelectedId(SystemTypePipe);
                settings.DrawingId = GetSelectedId(SelectDrawingDWG);
                settings.PipeDiameter = GetEditableValue(PipeDiametr);
                settings.LayingHeight = GetEditableValue(PipeLayingHeight);
                settings.AlignDirectionsToAllowedAngles = AlignDirectionsCheckBox.IsChecked == true;
            }
            if (saveArea)
            {
                settings.AreaPipeTypeId = GetSelectedId(StubPipeSelection);
                settings.AreaSystemTypeId = GetSelectedId(StubSystemTypePipe);
                settings.AreaPipeDiameter = GetEditableValue(StubPipeDiameter);
                settings.AreaLayingHeight = GetEditableValue(StubLayingHeight);
                settings.AreaWallOffset = GetEditableValue(WallOffset);
                settings.AreaPipeSpacing = GetEditableValue(PipeSpacing);
            }

            try
            {
                WarmFloorWindowSettingsStore.Save(settings);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Не удалось сохранить значения окна: " + exception.Message,
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearInputs()
        {
            PipeSelection.SelectedIndex = -1;
            SystemTypePipe.SelectedIndex = -1;
            SelectDrawingDWG.SelectedIndex = -1;
            PipeDiametr.SelectedIndex = -1;
            PipeDiametr.Text = string.Empty;
            PipeLayingHeight.SelectedIndex = -1;
            PipeLayingHeight.Text = string.Empty;
        }

        private static void SelectById(WpfComboBox comboBox, long? id)
        {
            if (!id.HasValue)
                return;

            foreach (ElementOption option in comboBox.Items)
            {
                if (option.Id.Value == id.Value)
                {
                    comboBox.SelectedItem = option;
                    return;
                }
            }
        }

        private static long? GetSelectedId(WpfComboBox comboBox) =>
            (comboBox.SelectedItem as ElementOption)?.Id.Value;

        private static string GetEditableValue(WpfComboBox comboBox) =>
            comboBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() : comboBox.Text;

        private static void SetEditableValue(WpfComboBox comboBox, string value)
        {
            comboBox.SelectedIndex = -1;
            comboBox.Text = value ?? string.Empty;
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Content?.ToString(), value, StringComparison.Ordinal))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private static void SelectDefault(WpfComboBox comboBox, int index)
        {
            if (comboBox.Items.Count > index)
                comboBox.SelectedIndex = index;
        }

        private void CreateWarmFloor_Click(object sender, RoutedEventArgs e)
        {
            WpfComboBox[] required = { PipeSelection, SystemTypePipe, SelectDrawingDWG, PipeDiametr, PipeLayingHeight };
            bool valid = true;

            foreach (WpfComboBox comboBox in required)
            {
                bool hasValue = comboBox.SelectedItem != null ||
                    (comboBox.IsEditable && !string.IsNullOrWhiteSpace(comboBox.Text));
                comboBox.BorderBrush = hasValue
                    ? System.Windows.Media.Brushes.LightGray
                    : System.Windows.Media.Brushes.OrangeRed;
                valid &= hasValue;
            }

            if (!TryGetNumber(PipeDiametr, out double diameter) || diameter <= 0)
            {
                PipeDiametr.BorderBrush = System.Windows.Media.Brushes.OrangeRed;
                valid = false;
            }
            if (!TryGetNumber(PipeLayingHeight, out double height))
            {
                PipeLayingHeight.BorderBrush = System.Windows.Media.Brushes.OrangeRed;
                valid = false;
            }

            if (!valid)
            {
                ShowStatus("Ошибка валидации данных, проверьте значения.", false);
                return;
            }

            var pipeType = PipeSelection.SelectedItem as ElementOption;
            var systemType = SystemTypePipe.SelectedItem as ElementOption;
            var drawing = SelectDrawingDWG.SelectedItem as ElementOption;
            if (pipeType == null || systemType == null || drawing == null)
            {
                ShowStatus("Выберите тип трубы, тип системы и чертеж на активном уровне.", false);
                return;
            }

            var request = new WarmFloorRequest(pipeType.Id, systemType.Id, drawing.Id, diameter, height,
                AlignDirectionsCheckBox.IsChecked == true);
            ProgressPanel.Visibility = Visibility.Visible;
            ContourProgressBar.Value = 0;
            ProgressPercent.Text = "0%";
            CreateWarmFloor.IsEnabled = false;
            if (!apiTaskQueue.TrySchedule(application =>
            {
                try
                {
                    WorkflowResult result = generator.Generate(request, (completed, total) =>
                    {
                        double percent = total > 0 ? 100.0 * completed / total : 0;
                        int roundedPercent = (int)Math.Round(percent);
                        Dispatcher.Invoke(() =>
                        {
                            ContourProgressBar.Value = percent;
                            ProgressPercent.Text = roundedPercent.ToString(CultureInfo.CurrentCulture) + "%";
                        });
                        PumpUiMessages();
                    });
                    Dispatcher.BeginInvoke(new Action(() => ShowStatus(result.Message, result.Succeeded)));
                }
                catch (Exception exception)
                {
                    Dispatcher.BeginInvoke(new Action(() => ShowStatus(
                        "Не удалось построить теплый пол: " + exception.Message, false)));
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() => CreateWarmFloor.IsEnabled = true));
                }
            }, out string scheduleError))
            {
                ShowStatus(scheduleError, false);
                CreateWarmFloor.IsEnabled = true;
            }
        }

        private void SelectFloorArea_Click(object sender, RoutedEventArgs e)
        {
            SelectFloorAreaButton.IsEnabled = false;
            Hide();
            if (!apiTaskQueue.TrySchedule(application =>
            {
                ElementOption pickedArea = null;
                string error = null;
                try
                {
                    pickedArea = floorAreaPicker.PickArea();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Escape cancels selection and keeps the previous target.
                }
                catch (Exception exception)
                {
                    error = "Не удалось выбрать область: " + exception.Message;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (pickedArea != null)
                    {
                        selectedFloorArea = pickedArea;
                        SelectedFloorAreaText.Text = "Выбрано: " + selectedFloorArea.Name;
                        ShowStatus("Область выбрана.", true);
                    }
                    else if (error != null)
                        ShowStatus(error, false);

                    SelectFloorAreaButton.IsEnabled = true;
                    Show();
                    Activate();
                }));
            }, out string scheduleError))
            {
                SelectFloorAreaButton.IsEnabled = true;
                Show();
                Activate();
                ShowStatus(scheduleError, false);
            }
        }

        private void SelectPipeEndpoint_Click(object sender, RoutedEventArgs e)
        {
            bool isStart = string.Equals((sender as FrameworkElement)?.Tag?.ToString(), "Start",
                StringComparison.Ordinal);
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            Hide();

            string prompt = isStart
                ? "Выберите трубу, от которой начнется спираль."
                : "Выберите трубу, к которой присоединится конец спирали.";
            if (!apiTaskQueue.TrySchedule(application =>
            {
                ElementOption pickedPipe = null;
                string error = null;
                try
                {
                    pickedPipe = floorAreaPicker.PickPipe(prompt);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Escape cancels selection and keeps the previous pipe.
                }
                catch (Exception exception)
                {
                    error = "Не удалось выбрать трубу: " + exception.Message;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (pickedPipe != null)
                    {
                        if (isStart)
                        {
                            selectedStartPipe = pickedPipe;
                            SelectedStartPipeText.Text = "Выбрано: " + pickedPipe.Name;
                        }
                        else
                        {
                            selectedEndPipe = pickedPipe;
                            SelectedEndPipeText.Text = "Выбрано: " + pickedPipe.Name;
                        }
                        ShowStatus("Труба выбрана.", true);
                    }
                    else if (error != null)
                        ShowStatus(error, false);

                    if (button != null) button.IsEnabled = true;
                    Show();
                    Activate();
                }));
            }, out string scheduleError))
            {
                if (button != null) button.IsEnabled = true;
                Show();
                Activate();
                ShowStatus(scheduleError, false);
            }
        }

        private void CreateAreaWarmFloor_Click(object sender, RoutedEventArgs e)
        {
            if (selectedFloorArea == null)
            {
                ShowStatus("Сначала выберите помещение или цветовую область на плане.", false);
                return;
            }
            if (selectedStartPipe == null || selectedEndPipe == null)
            {
                ShowStatus("Выберите начальную и конечную трубу для подключения змейки.", false);
                return;
            }
            if (selectedStartPipe.Id.Value == selectedEndPipe.Id.Value)
            {
                ShowStatus("Начальной и конечной должны быть разные трубы.", false);
                return;
            }

            bool valid = true;
            WpfComboBox[] required =
            {
                StubPipeSelection, StubSystemTypePipe, StubPipeDiameter, StubLayingHeight, WallOffset, PipeSpacing
            };
            foreach (WpfComboBox comboBox in required)
            {
                bool hasValue = comboBox.SelectedItem != null ||
                    (comboBox.IsEditable && !string.IsNullOrWhiteSpace(comboBox.Text));
                comboBox.BorderBrush = hasValue
                    ? System.Windows.Media.Brushes.LightGray
                    : System.Windows.Media.Brushes.OrangeRed;
                valid &= hasValue;
            }

            bool diameterValid = TryGetNumber(StubPipeDiameter, out double diameter) && diameter > 0;
            bool heightValid = TryGetNumber(StubLayingHeight, out double height);
            bool edgeValid = TryGetNumber(WallOffset, out double wallOffset) && wallOffset >= 0;
            bool spacingValid = TryGetNumber(PipeSpacing, out double spacing) && spacing > 0;
            if (!diameterValid) StubPipeDiameter.BorderBrush = System.Windows.Media.Brushes.OrangeRed;
            if (!heightValid) StubLayingHeight.BorderBrush = System.Windows.Media.Brushes.OrangeRed;
            if (!edgeValid) WallOffset.BorderBrush = System.Windows.Media.Brushes.OrangeRed;
            if (!spacingValid) PipeSpacing.BorderBrush = System.Windows.Media.Brushes.OrangeRed;
            valid &= diameterValid && heightValid && edgeValid && spacingValid && spacing > diameter;

            var pipeType = StubPipeSelection.SelectedItem as ElementOption;
            var systemType = StubSystemTypePipe.SelectedItem as ElementOption;
            valid &= pipeType != null && systemType != null;
            if (!valid)
            {
                ShowStatus("Проверьте параметры. Шаг укладки должен быть больше диаметра трубы.", false);
                return;
            }

            CreateAreaWarmFloor.IsEnabled = false;
            if (!apiTaskQueue.TrySchedule(application =>
            {
                try
                {
                    var request = new AreaWarmFloorRequest(selectedFloorArea.Id,
                        selectedStartPipe.Id, selectedEndPipe.Id, pipeType.Id, systemType.Id,
                        diameter, height, wallOffset, spacing);
                    WorkflowResult result = areaGenerator.GenerateOnArea(request);
                    Dispatcher.BeginInvoke(new Action(() => ShowStatus(result.Message, result.Succeeded)));
                }
                catch (Exception exception)
                {
                    Dispatcher.BeginInvoke(new Action(() => ShowStatus(
                        "Не удалось построить теплый пол: " + exception.Message, false)));
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() => CreateAreaWarmFloor.IsEnabled = true));
                }
            }, out string scheduleError))
            {
                ShowStatus(scheduleError, false);
                CreateAreaWarmFloor.IsEnabled = true;
            }
        }

        private static void PumpUiMessages()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private void ShowStatus(string message, bool succeeded)
        {
            StatusText.Text = message;
            StatusText.Foreground = succeeded
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.Firebrick;
        }

        private static bool TryGetNumber(WpfComboBox comboBox, out double value)
        {
            string text = comboBox.SelectedItem is ComboBoxItem item
                ? item.Content?.ToString()
                : comboBox.Text;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
