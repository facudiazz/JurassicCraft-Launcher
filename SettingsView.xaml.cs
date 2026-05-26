using System;
using System.Windows;
using System.Windows.Controls;

namespace JurassicCraftLauncher
{
    public enum WipeTarget { Config, Shaders, Resources, Engine, Cache, Total }

    /// <summary>
    /// Código subyacente de la pestaña de "Ajustes".
    /// Domina la gestión visual y eventos reactivos de sliders y opciones de Launcher.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        private bool _isLoadingState;
        #region Eventos Públicos

        public event EventHandler<int>? RamChanged;
        public event EventHandler<string>? GraphicsPresetChanged;
        public event EventHandler<bool>? CustomJvmToggled;
        public event EventHandler<string>? CustomJvmArgsChanged;
        public event EventHandler<WipeTarget>? WipeRequested;

        #endregion

        public SettingsView()
        {
            InitializeComponent();
        }

        #region Rutinas de Carga / Inyección desde JSON

        /// <summary>
        /// Sincroniza la UI con los datos cargados desde el ConfigurationManager
        /// </summary>
        public void LoadUiState(LauncherConfig config)
        {
            _isLoadingState = true;
            try
            {
            // Memoria
            SetRamValue(config.MaxRamMb);

            // Gráficos
            BtnGraphicLow.IsChecked = config.GraphicsPreset == "Low";
            BtnGraphicMedium.IsChecked = config.GraphicsPreset == "Medium";
            BtnGraphicHigh.IsChecked = config.GraphicsPreset == "High";

            // Argumentos Custom
            ToggleJvmArgs.IsChecked = config.UseCustomJvmArgs;
            InputJvmArgs.Text = config.CustomJvmArgs;
            }
            finally
            {
                _isLoadingState = false;
            }
        }

        private void SetRamValue(int megaBytes)
        {
            double gigaBytes = megaBytes / 1024.0;
            RamSlider.Value = gigaBytes;
            UpdateRamTexts(gigaBytes);
        }

        #endregion

        #region Comandos Locales (UI Handlers)

        // -- RAM --
        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RamValueText == null || RamMbText == null) return;

            double sanitizedGbAmount = Math.Round(e.NewValue * 2) / 2.0; 
            int absoluteMegabytes = (int)(sanitizedGbAmount * 1024);
            
            UpdateRamTexts(sanitizedGbAmount);
            if (_isLoadingState) return;
            RamChanged?.Invoke(this, absoluteMegabytes);
        }

        private void UpdateRamTexts(double gb)
        {
            RamValueText.Text = $"{gb:F1} GB";
            RamMbText.Text = $"{(int)(gb * 1024)} MB";
        }

        // -- GRAFICOS --
        private void GraphicPreset_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingState) return;
            if (sender is RadioButton rb && rb.IsChecked == true && rb.Tag != null)
            {
                GraphicsPresetChanged?.Invoke(this, rb.Tag.ToString()!);
            }
        }

        // -- JVM ARGS --
        private void ToggleJvmArgs_Changed(object sender, RoutedEventArgs e)
        {
            if (JvmArgsContainer == null) return;

            bool isEnabled = ToggleJvmArgs.IsChecked ?? false;
            // El nuevo diseño muestra/oculta el contenedor completo en lugar de bajar opacidad
            JvmArgsContainer.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (_isLoadingState) return;
            CustomJvmToggled?.Invoke(this, isEnabled);
        }

        private void InputJvmArgs_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingState) return;
            CustomJvmArgsChanged?.Invoke(this, InputJvmArgs.Text);
        }

        // -- WIPES --
        private void ActionWipe_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                if (Enum.TryParse<WipeTarget>(btn.Tag.ToString(), out var target))
                {
                    WipeRequested?.Invoke(this, target);
                }
            }
        }

        #endregion
    }
}
