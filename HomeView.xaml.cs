using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// Código subyacente para la pestaña "Inicio".
    /// Se encarga exclusivamente de las animaciones visuales de estado y progreso,
    /// exponiendo eventos semánticos al orquestador principal.
    /// </summary>
    public partial class HomeView : UserControl
    {
        #region Eventos Públicos

        /// <summary>
        /// Disparado cuando el usuario hace clic deliberadamente sobre el botón principal JUGAR.
        /// </summary>
        public event RoutedEventHandler? PlayClick;

        /// <summary>
        /// Disparado cuando se solicita editar el nombre de usuario offline actual.
        /// </summary>
        public event RoutedEventHandler? EditUserClick;

        #endregion

        public HomeView()
        {
            InitializeComponent();
        }

        #region Actualización Visual de Estados

        /// <summary>
        /// Modifica el texto en pantalla que indica el paso actual del lanzamiento.
        /// </summary>
        /// <param name="text">Descripción de la acción que se realiza.</param>
        /// <param name="colorHex">Tinte indicativo en HEX, ej. "#80C0A0" para verde azulado neutro.</param>
        public void SetStatus(string text, string colorHex = "#80C0A0")
        {
            StatusText.Text = text;
            try 
            {
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            } 
            catch { /* Fallback transparente ignorado */ }
        }

        /// <summary>
        /// Expone información en formato consola invisible / log inferior debajo de la barra de progreso.
        /// </summary>
        public void SetLog(string text) => LogText.Text = text;

        /// <summary>
        /// Anima horizontalmente el relleno de la barra de progresos basándose en el porcentaje numérico.
        /// </summary>
        public void ActualizarBarraProgreso(double pct)
        {
            double parentWidth = ProgressContainer.ActualWidth;
            if (parentWidth > 0)
                ProgressFill.Width = parentWidth * Math.Clamp(pct, 0, 1);
        }

        /// <summary>
        /// Habilita o deshabilita la interacción física con el Botón grande de lanzamiento.
        /// </summary>
        public void SetPlayButtonEnabled(bool isEnabled)
        {
            PlayButtonLogued.IsEnabled = isEnabled;
        }

        #endregion

        #region Controladores de Interfaz

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            PlayClick?.Invoke(this, e);
        }

        private void EditarUsuario_Click(object sender, RoutedEventArgs e)
        {
            EditUserClick?.Invoke(this, e);
        }

        #endregion
    }
}
