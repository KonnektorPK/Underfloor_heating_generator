using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Underfloor_heating_generator.Application.Contracts;
using Underfloor_heating_generator.Application.Models;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace Underfloor_heating_generator.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly IWarmFloorGenerator generator;

        public MainWindow(IWarmFloorOptionsProvider optionsProvider, IWarmFloorGenerator generator)
        {
            InitializeComponent();
            this.generator = generator;

            PipeSelection.ItemsSource = optionsProvider.GetPipeTypes();
            SystemTypePipe.ItemsSource = optionsProvider.GetSystemTypes();
            SelectDrawingDWG.ItemsSource = optionsProvider.GetDrawings();

            StubPipeSelection.ItemsSource = PipeSelection.ItemsSource;
            StubSystemTypePipe.ItemsSource = SystemTypePipe.ItemsSource;

            SelectDefault(PipeDiametr, 0);
            SelectDefault(PipeLayingHeight, 1);
            SelectDefault(PipeSelection, 0);
            SelectDefault(SystemTypePipe, 0);
            SelectDefault(SelectDrawingDWG, 0);

            if (!optionsProvider.HasActiveLevel)
                ShowStatus("Активный вид не связан с уровнем. Переключитесь на план этажа.", false);
        }

        public void SetOwner(IntPtr ownerHandle) => new WindowInteropHelper(this).Owner = ownerHandle;

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
            ProgressLabel.Text = "Создание контуров: 0%";
            CreateWarmFloor.IsEnabled = false;
            try
            {
                WorkflowResult result = generator.Generate(request, (completed, total) =>
                {
                    double percent = total > 0 ? 100.0 * completed / total : 0;
                    int roundedPercent = (int)Math.Round(percent);
                    ContourProgressBar.Value = percent;
                    ProgressPercent.Text = roundedPercent.ToString(CultureInfo.CurrentCulture) + "%";
                    ProgressLabel.Text = string.Format(CultureInfo.CurrentCulture,
                        "Создание контуров: {0}% ({1}/{2})", roundedPercent, completed, total);
                    PumpUiMessages();
                });
                ShowStatus(result.Message, result.Succeeded);
            }
            finally
            {
                CreateWarmFloor.IsEnabled = true;
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
