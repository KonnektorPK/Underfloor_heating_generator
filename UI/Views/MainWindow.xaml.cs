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

            WarmFloorWindowSettings settings = WarmFloorWindowSettingsStore.Load();
            RestoreSettings(settings);
            InitializeAppearance(settings);
            UpdateAvailability();
            DrawingEmpty.Visibility = SelectDrawingDWG.HasItems ? Visibility.Collapsed : Visibility.Visible;
            if (!PipeSelection.HasItems || !SystemTypePipe.HasItems)
            {
                string missing = !PipeSelection.HasItems ? "В модели нет доступных типов труб." : "В модели нет доступных систем труб.";
                SetFieldError(PipeSelection.HasItems ? SystemTypePipe : PipeSelection, missing);
                SetFieldError(PipeSelection.HasItems ? StubSystemTypePipe : StubPipeSelection, missing);
            }

            if (!optionsProvider.HasActiveLevel)
                ShowStatus("Активный вид не связан с уровнем. Переключитесь на план этажа.", false);
        }

        public void SetOwner(IntPtr ownerHandle) => new WindowInteropHelper(this).Owner = ownerHandle;

        private void RestoreSettings(WarmFloorWindowSettings settings)
        {
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
                if (!string.IsNullOrWhiteSpace(settings.AreaMaximumArea))
                    MaximumAreaInput.Text = settings.AreaMaximumArea;
                else
                {
                    CollectorDistanceInput.Text = settings.AreaCollectorDistance ?? "";
                    MaximumLengthInput.Text = settings.AreaMaximumLength ?? "";
                }
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                return;
            }
            bool save = SaveChanges.IsChecked == true;
            bool saveArea = SaveAreaValues.IsChecked == true;
            var settings = new WarmFloorWindowSettings
            {
                SaveValues = save,
                SaveAreaValues = saveArea
            };
            SaveAppearance(settings);
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
                settings.AreaCollectorDistance = CollectorDistanceInput.Text;
                settings.AreaMaximumLength = MaximumLengthInput.Text;
                settings.AreaMaximumArea = MaximumAreaInput.Text;
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
            firstInvalidField = null;
            var drawing = SelectDrawingDWG.SelectedItem as ElementOption;
            var pipeType = PipeSelection.SelectedItem as ElementOption;
            var systemType = SystemTypePipe.SelectedItem as ElementOption;
            SetFieldError(SelectDrawingDWG, drawing == null ? "Выберите чертёж." : null);
            SetFieldError(PipeSelection, pipeType == null ? "Выберите тип трубы." : null);
            SetFieldError(SystemTypePipe, systemType == null ? "Выберите систему труб." : null);
            SetFieldError(PipeDiametr, !TryGetNumber(PipeDiametr, out double diameter) || diameter <= 0 ? "Введите диаметр больше 0 мм." : null);
            SetFieldError(PipeLayingHeight, !TryGetNumber(PipeLayingHeight, out double height) ? "Введите высоту в миллиметрах." : null);
            if (!ShowValidation("Заполните выделенные поля."))
                return;
            var request = new WarmFloorRequest(pipeType.Id, systemType.Id, drawing.Id, diameter, height,
                AlignDirectionsCheckBox.IsChecked == true);
            StartOperation(showProgress: true);
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
                    Dispatcher.BeginInvoke(new Action(FinishOperation));
                }
            }, out string scheduleError))
            {
                ShowStatus(scheduleError, false);
                FinishOperation();
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
                        SetFieldError(SelectFloorAreaButton, null);
                        Announce(SelectedFloorAreaText);
                        HideStatus();
                    }
                    else if (error != null)
                        ShowStatus(error, false);

                    SelectFloorAreaButton.IsEnabled = true;
                    Show();
                    Activate();
                    if (sender is Button source) source.Focus();
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
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            Hide();

            const string prompt = "Выберите трубу, которая задаёт сторону выводов тёплого пола.";
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
                        selectedStartPipe = pickedPipe;
                        SelectedStartPipeText.Text = "Выбрано: " + pickedPipe.Name;
                        SetFieldError(SelectStartPipeButton, null);
                        Announce(SelectedStartPipeText);
                        HideStatus();
                    }
                    else if (error != null)
                        ShowStatus(error, false);

                    if (button != null) button.IsEnabled = true;
                    Show();
                    Activate();
                    if (sender is Button source) source.Focus();
                }));
            }, out string scheduleError))
            {
                if (button != null) button.IsEnabled = true;
                Show();
                Activate();
                ShowStatus(scheduleError, false);
            }
        }

        private void CircuitLimits_Changed(object sender, TextChangedEventArgs e)
        {
            if (CollectorDistanceInput == null || MaximumLengthInput == null || MaximumAreaInput == null || LimitsHint == null)
                return;
            bool areaEntered = !string.IsNullOrWhiteSpace(MaximumAreaInput.Text);
            bool lengthEntered = !string.IsNullOrWhiteSpace(CollectorDistanceInput.Text) ||
                !string.IsNullOrWhiteSpace(MaximumLengthInput.Text);
            CollectorDistanceInput.IsEnabled = !areaEntered;
            MaximumLengthInput.IsEnabled = !areaEntered;
            MaximumAreaInput.IsEnabled = !lengthEntered;
            LimitsHint.Text = areaEntered
                ? "Чтобы перейти к расчёту по длине, очистите поле площади."
                : lengthEntered
                    ? "Чтобы перейти к расчёту по площади, очистите оба поля длины."
                    : "Задайте площадь или укажите расстояние до коллектора и предельную длину контура.";
            if (!CollectorDistanceInput.IsEnabled) SetFieldError(CollectorDistanceInput, null);
            if (!MaximumLengthInput.IsEnabled) SetFieldError(MaximumLengthInput, null);
            if (!MaximumAreaInput.IsEnabled) SetFieldError(MaximumAreaInput, null);
        }

        private static double? ReadCircuitNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value : double.NaN;
        }

        private void CreateAreaWarmFloor_Click(object sender, RoutedEventArgs e)
        {
            firstInvalidField = null;
            SetFieldError(SelectFloorAreaButton, selectedFloorArea == null ? "Выберите область на плане Revit." : null);
            SetFieldError(SelectStartPipeButton, selectedStartPipe == null ? "Укажите трубу, которая задаёт сторону выводов." : null);

            var pipeType = StubPipeSelection.SelectedItem as ElementOption;
            var systemType = StubSystemTypePipe.SelectedItem as ElementOption;
            SetFieldError(StubPipeSelection, pipeType == null ? "Выберите тип трубы." : null);
            SetFieldError(StubSystemTypePipe, systemType == null ? "Выберите систему труб." : null);
            bool diameterValid = TryGetNumber(StubPipeDiameter, out double diameter) && diameter > 0;
            SetFieldError(StubPipeDiameter, diameterValid ? null : "Введите диаметр больше 0 мм.");
            SetFieldError(StubLayingHeight, !TryGetNumber(StubLayingHeight, out double height) ? "Введите высоту в миллиметрах." : null);
            SetFieldError(WallOffset, !TryGetNumber(WallOffset, out double wallOffset) || wallOffset < 0 ? "Введите отступ не меньше 0 мм." : null);
            SetFieldError(PipeSpacing, !TryGetNumber(PipeSpacing, out double spacing) || spacing <= 0 ||
                (diameterValid && spacing <= diameter) ? "Шаг должен быть больше диаметра трубы." : null);

            var limits = new FloorCircuitLimits(ReadCircuitNumber(CollectorDistanceInput.Text),
                ReadCircuitNumber(MaximumLengthInput.Text), ReadCircuitNumber(MaximumAreaInput.Text));
            string limitError = limits.GetValidationError();
            SetFieldError(CollectorDistanceInput, null);
            SetFieldError(MaximumLengthInput, null);
            SetFieldError(MaximumAreaInput, null);
            if (limitError != null)
            {
                if (limits.MaximumAreaSquareMeters.HasValue)
                    SetFieldError(MaximumAreaInput, "Введите площадь больше 0 м².");
                else if (limits.CollectorDistanceMeters.HasValue || limits.MaximumLengthMeters.HasValue)
                {
                    if (limits.CollectorDistanceMeters is not double distance || !double.IsFinite(distance) || distance < 0)
                        SetFieldError(CollectorDistanceInput, "Введите расстояние не меньше 0 м.");
                    if (limits.MaximumLengthMeters is not double length || !double.IsFinite(length) || length <= 0)
                        SetFieldError(MaximumLengthInput, "Введите длину больше 0 м.");
                    else if (limits.CollectorDistanceMeters is double reserve && length <= 2 * reserve)
                        SetFieldError(MaximumLengthInput, "Длина должна быть больше удвоенного расстояния до коллектора.");
                }
                else
                    SetFieldError(CollectorDistanceInput, "Заполните оба поля длины или укажите площадь.");
            }

            if (!ShowValidation("Заполните выделенные поля."))
                return;
            var request = new AreaWarmFloorRequest(selectedFloorArea.Id,
                selectedStartPipe.Id, pipeType.Id, systemType.Id, diameter, height, wallOffset, spacing, limits);
            StartOperation(showProgress: false);
            if (!apiTaskQueue.TrySchedule(application =>
            {
                try
                {
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
                    Dispatcher.BeginInvoke(new Action(FinishOperation));
                }
            }, out string scheduleError))
            {
                ShowStatus(scheduleError, false);
                FinishOperation();
            }
        }

        private static void PumpUiMessages()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private void ShowStatus(string message, bool? succeeded)
        {
            StatusPanel.Visibility = Visibility.Visible;
            StatusTitle.Text = succeeded switch { true => "Готово", false => "Требуется внимание", null => "Построение" };
            StatusTitle.SetResourceReference(TextBlock.ForegroundProperty,
                succeeded switch { true => "Success", false => "Danger", null => "Ink" });
            StatusText.Text = message;
            Footer.InvalidateMeasure();
            Announce(StatusText);
        }

        private static bool TryGetNumber(WpfComboBox comboBox, out double value)
        {
            string text = comboBox.SelectedItem is ComboBoxItem item
                ? item.Content?.ToString()
                : comboBox.Text;
            return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) && double.IsFinite(value);
        }
    }
}
