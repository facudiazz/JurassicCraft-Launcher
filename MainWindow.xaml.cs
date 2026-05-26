using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// Ventana y Orquestador principal de la aplicación.
    /// Define la navegación y gobierna el ciclo de vida de los servicios de back-end.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Campos de Estado y Dependencias

        private readonly ConfigurationManager _configManager;
        private readonly MinecraftService _minecraftService;
        private readonly ModpackService _modpackService;
        private readonly UpdateService _updateService;

        private readonly HomeView _homeView;
        private readonly SettingsView _settingsView;

        private readonly SkinsView _skinsView;

        private Func<string, (bool Valid, string Error)>? _currentModalValidator;

        #endregion

        #region Propiedades de Versión

        private static readonly string DisplayVersion =
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.1.0";

        private static readonly string LauncherVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";

        #endregion

        #region Inicialización

        public MainWindow()
        {
            InitializeComponent();
            VersionText.Text = DisplayVersion;

            _configManager = new ConfigurationManager();
            _minecraftService = new MinecraftService(AppConstants.GameDir);
            _modpackService = new ModpackService(AppConstants.GameDir);
            _updateService = new UpdateService();

            _homeView = new HomeView();
            _settingsView = new SettingsView();
            _skinsView = new SkinsView();

            SetupViewEvents();
            SetupServiceEvents();

            CargarFlujoInicial();
            NavigateToHome();
        }

        private void SetupViewEvents()
        {
            _homeView.PlayClick += ActionPlaySequence_Event;
            _homeView.EditUserClick += ActionEditUser_Event;

            // Eventos Re-Activos de Settings
            _settingsView.RamChanged += (s, assignedMb) => _configManager.UpdateMaxRam(assignedMb);
            _settingsView.GraphicsPresetChanged += (s, preset) => _configManager.UpdateGraphicsPreset(preset);
            _settingsView.CustomJvmToggled += (s, on) => _configManager.UpdateAdvancedJvmToggle(on);
            _settingsView.CustomJvmArgsChanged += (s, args) => _configManager.UpdateJvmArguments(args);
            _settingsView.WipeRequested += ActionWipe_Requested;
        }

        private void SetupServiceEvents()
        {
            _modpackService.ProgressChanged += (current, total, fileName) =>
            {
                Dispatcher.Invoke(() =>
                {
                    double pct = total > 0 ? (double)current / total : 0;
                    _homeView.ActualizarBarraProgreso(pct);
                    _homeView.SetStatus(string.Format(AppTexts.StatusSyncingProgress, current, total));
                    _homeView.SetLog(string.Format(AppTexts.LogSyncingFileFormat, fileName));
                });
            };

            _minecraftService.LogUpdate += (msg) =>
                Dispatcher.Invoke(() => _homeView.SetLog(msg));

            _minecraftService.ProgressUpdate += (pct, status) =>
                Dispatcher.InvokeAsync(() =>
                {
                    _homeView.ActualizarBarraProgreso(pct);
                    _homeView.SetStatus(status);
                });

            _updateService.ProgressChanged += (pct, status) =>
                Dispatcher.Invoke(() =>
                {
                    _homeView.ActualizarBarraProgreso(pct);
                    _homeView.SetStatus(string.Format(AppTexts.StatusUpdaterProgress, status));
                });
        }

        private async void CargarFlujoInicial()
        {
            _configManager.Load();
            _settingsView.LoadUiState(_configManager.Config);

            if (string.IsNullOrWhiteSpace(_configManager.Config.Username))
            {
                _homeView.SetStatus(AppTexts.StatusWaiting);
            }
            else
            {
                PropagarNombreVisualmente(_configManager.Config.Username);
                _homeView.SetStatus(AppTexts.StatusWaiting);
            }

            await VerificarAutoActualizacion();

            await EnsurePersistedSkinAvailableAsync(_configManager.Config.Username);

            if (string.IsNullOrWhiteSpace(_configManager.Config.Username))
            {
                _homeView.SetStatus(AppTexts.StatusWaiting);
                await SolicitarYGuardarNombreUsuario(esEdicion: false);
            }
        }

        #endregion

        #region Lógica de Lanzamiento Secuencial

        private async void ActionPlaySequence_Event(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_configManager.Config.Username))
            {
                _homeView.SetStatus(AppTexts.StatusRequiresUsername, "#E8C840");
                return;
            }

            _homeView.SetPlayButtonEnabled(false);

            try
            {
                // -- FASE 0: INSTALACIÓN AUTOMÁTICA DE JAVA 17 --
                await EnsurePersistedSkinAvailableAsync(_configManager.Config.Username);
                await _minecraftService.EnsureJava17InstalledAsync();

                // -- FASE 1: METADATA --
                _homeView.SetStatus(AppTexts.StatusCheckingUpdates, "#80C0A0");
                await _modpackService.DownloadMetadata();

                string modpackInfoJsonCache = Path.Combine(AppConstants.GameDir, "modpack-info.json");
                var parserOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rootMeta = JsonSerializer.Deserialize<ModpackInfo>(File.ReadAllText(modpackInfoJsonCache), parserOpts);

                string mcDefVersion = rootMeta?.MinecraftVersion ?? AppConstants.DefaultMinecraftVersion;
                string loaderDefVersion = rootMeta?.ModLoaderVersion ?? AppConstants.DefaultForgeVersion;
                string uniqueVersionId = $"{mcDefVersion}-forge-{loaderDefVersion}";

                // -- FASE 2: MOTOR FORGE --
                _homeView.SetStatus(AppTexts.StatusCheckingForge, "#80C0A0");
                await _minecraftService.InstallForgeAsync(mcDefVersion, loaderDefVersion);

                // -- FASE 3: SMART SYNC MODPACK --
                _homeView.SetStatus(AppTexts.StatusSyncingModpack, "#80C0A0");
                await _modpackService.SyncModpack();

                // -- FASE 4: RESOURCES --
                _homeView.SetStatus(AppTexts.StatusDownloadingResources, "#80C0A0");
                await _minecraftService.InstallMinecraftResourcesAsync(uniqueVersionId);

                // -- FASE 5: LANZAMIENTO EJECUCIÓN --
                _homeView.SetStatus(AppTexts.StatusOpeningGame, "#60FF80");

                string extraJvmArgs = _configManager.Config.UseCustomJvmArgs ? _configManager.Config.CustomJvmArgs : "";

                await _minecraftService.LaunchAsync(
                    uniqueVersionId,
                    _configManager.Config.Username,
                    _configManager.Config.MaxRamMb,
                    _configManager.Config.GraphicsPreset,
                    extraJvmArgs);

                await Task.Delay(1500);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                await ShowModalAsync(AppTexts.ModalErrorTitle,
                    AppTexts.ModalErrorGenericPrefix + ex.Message,
                    new[] { new ModalButton { Text = AppTexts.ModalBtnUnderstood, Result = true } });

                _homeView.SetStatus(AppTexts.StatusLaunchError, "#FF6666");
                _homeView.SetPlayButtonEnabled(true);
            }
        }

        #endregion

        #region Mantenimiento e Internos

        private async Task VerificarAutoActualizacion()
        {
            var detectedRelease = await _updateService.CheckForUpdateAsync(LauncherVersion);

            if (detectedRelease != null)
            {
                _homeView.SetPlayButtonEnabled(false);
                _homeView.SetStatus(AppTexts.StatusLauncherUpdating, "#80C0FF");

                try
                {
                    await _updateService.DownloadAndApplyUpdate(detectedRelease);
                }
                catch (Exception ex)
                {
                    _homeView.SetStatus(AppTexts.StatusLauncherUpdateError, "#FF6666");
                    await ShowModalAsync(AppTexts.ModalSystemErrorTitle,
                        AppTexts.ModalSystemUpdateFailed + ex.Message,
                        new[] { new ModalButton { Text = AppTexts.ModalBtnSkip, Result = true } });
                    _homeView.SetPlayButtonEnabled(true);
                }
            }
        }

        #endregion

        #region Limpieza y Wipe

        private async void ActionWipe_Requested(object? sender, WipeTarget target)
        {
            string warningMessage = target switch
            {
                WipeTarget.Config => AppTexts.WipeConfigWarning,
                WipeTarget.Shaders => AppTexts.WipeShadersWarning,
                WipeTarget.Resources => AppTexts.WipeResourcesWarning,
                WipeTarget.Engine => AppTexts.WipeEngineWarning,
                WipeTarget.Cache => AppTexts.WipeCacheWarning,
                WipeTarget.Total => AppTexts.WipeTotalWarning,
                _ => AppTexts.WipeFallbackWarning
            };

            var objResult = await ShowModalAsync(AppTexts.WipeTitle, warningMessage, new[]
            {
                new ModalButton { Text = AppTexts.ModalBtnYes, Result = "YES", Color = "#CC2222" },
                new ModalButton { Text = AppTexts.ModalBtnNo, Result = "NO", Color = "#888888" }
            });

            if (objResult?.ToString() == "YES")
            {
                ExecuteWipeLogic(target);

                await ShowModalAsync(AppTexts.WipeTitle, AppTexts.WipeMessageSuccess, new[]
                {
                    new ModalButton { Text = AppTexts.WipeBtnAcknowledge, Result = "OK" }
                });
            }
        }

        private void ExecuteWipeLogic(WipeTarget target)
        {
            try
            {
                switch (target)
                {
                    case WipeTarget.Config:
                        DeleteFolderIfExist(Path.Combine(AppConstants.GameDir, "config"));
                        DeleteFileIfExist(Path.Combine(AppConstants.GameDir, "options.txt"));
                        break;
                    case WipeTarget.Shaders:
                        DeleteFolderIfExist(Path.Combine(AppConstants.GameDir, "shaderpacks"));
                        break;
                    case WipeTarget.Resources:
                        DeleteFolderIfExist(Path.Combine(AppConstants.GameDir, "resourcepacks"));
                        break;
                    case WipeTarget.Engine:
                        DeleteFolderIfExist(Path.Combine(AppConstants.GameDir, "assets"));
                        DeleteFolderIfExist(Path.Combine(AppConstants.GameDir, "libraries"));
                        DeleteFolderIfExist(Path.Combine(AppConstants.GameDir, "versions"));
                        break;
                    case WipeTarget.Cache:
                        DeleteCacheContent();
                        break;
                    case WipeTarget.Total:
                        DeleteFolderIfExist(AppConstants.GameDir);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error de wipeo: {ex.Message}");
            }
        }

        private void DeleteFolderIfExist(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private void DeleteFileIfExist(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private void DeleteCacheContent()
        {
            if (!Directory.Exists(AppConstants.GameDir))
            {
                return;
            }

            string[] protectedFolders =
            {
                "assets",
                "config",
                "libraries",
                "mods",
                "resourcepacks",
                "runtime",
                "shaderpacks",
                "versions"
            };

            string[] protectedFiles =
            {
                "options.txt",
                "launcher_profiles.json"
            };

            foreach (string directoryPath in Directory.GetDirectories(AppConstants.GameDir))
            {
                string folderName = Path.GetFileName(directoryPath);

                if (protectedFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.Delete(directoryPath, true);
            }

            foreach (string filePath in Directory.GetFiles(AppConstants.GameDir))
            {
                string fileName = Path.GetFileName(filePath);

                if (protectedFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Delete(filePath);
            }
        }

        #endregion

        #region Gestión de Usuarios

        private async void ActionEditUser_Event(object sender, RoutedEventArgs e)
        {
            await SolicitarYGuardarNombreUsuario(esEdicion: true);
        }

        private async Task SolicitarYGuardarNombreUsuario(bool esEdicion)
        {
            string titulo = esEdicion ? AppTexts.UserEditTitle : AppTexts.UserNewTitle;
            string mensaje = esEdicion ? AppTexts.UserEditMessage : AppTexts.UserNewMessage;
            string btnPrincipal = esEdicion ? AppTexts.UserBtnSave : AppTexts.UserBtnAdd;

            var controlButtons = new System.Collections.Generic.List<ModalButton>
            {
                new ModalButton { Text = btnPrincipal, Result = "OK" }
            };

            if (esEdicion)
            {
                controlButtons.Add(new ModalButton { Text = AppTexts.UserBtnCancel, Result = "CANCEL", Color = "#888888" });
            }

            var dialogResponse = await ShowModalAsync(titulo, mensaje, controlButtons, showInput: true, validator: ValidarReglasNombreUsuario);

            if (dialogResponse is string cleanUsername && !string.IsNullOrWhiteSpace(cleanUsername))
            {
                _configManager.UpdateUsername(cleanUsername);
                await EnsurePersistedSkinAvailableAsync(cleanUsername);
                PropagarNombreVisualmente(cleanUsername);

                if (!esEdicion)
                {
                    _homeView.SetStatus(AppTexts.StatusOperationSuccess);
                }
            }
        }

        private void PropagarNombreVisualmente(string username)
        {
            UsernameLabel.Text = username;
            PanelUsuario.Visibility = Visibility.Visible;
        }

        private static async Task EnsurePersistedSkinAvailableAsync(string username)
        {
            if (SkinPersistenceService.HasCustomSkin())
            {
                if (!SkinPersistenceService.HasGlobalSkin())
                    SkinPersistenceService.EnsureGlobalCopyFromCustom();
                return;
            }

            if (SkinPersistenceService.RestoreCustomSkinFromGlobal())
                return;

            await SkinPersistenceService.TryDownloadPremiumSkinForUsernameAsync(username);
        }

        private (bool Valid, string Error) ValidarReglasNombreUsuario(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                return (false, string.Empty);

            foreach (char c in name)
            {
                bool esLetra = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool esNumero = (c >= '0' && c <= '9');
                bool esGuionBajo = (c == '_');

                if (!esLetra && !esNumero && !esGuionBajo)
                {
                    if (c == ' ') return (false, AppTexts.ErrorUserSpaces);
                    if (c == '-') return (false, AppTexts.ErrorUserDash);
                    return (false, $"{AppTexts.ErrorUserChar} '{c}'");
                }
            }

            return (true, string.Empty);
        }

        #endregion

        #region Interfaz: Rutas y Navegación Menú (Layout)

        private void NavHome_Click(object sender, MouseButtonEventArgs e) => NavigateToHome();

        private void NavSkins_Click(object sender, MouseButtonEventArgs e) => NavigateToSkins();

        private void NavSettings_Click(object sender, MouseButtonEventArgs e) => NavigateToSettings();

        private void NavigateToHome()
        {
            ViewContainer.Content = _homeView;
            AplicarEfectoActivoSidebar(NavHome);
        }

        private void NavigateToSkins()
        {
            ViewContainer.Content = _skinsView;
            AplicarEfectoActivoSidebar(NavSkins);
        }

        private void NavigateToSettings()
        {
            ViewContainer.Content = _settingsView;
            AplicarEfectoActivoSidebar(NavSettings);
        }

        private void AplicarEfectoActivoSidebar(Border activeItem)
        {
            Border[] options = { NavHome, NavSkins, NavSettings };
            foreach (var item in options)
            {
                // Usar ClearValue es CRÍTICO: si se asigna directamente (item.Background = X),
                // se crea un "valor local" con mayor prioridad que los Style Triggers,
                // bloqueando el efecto hover. ClearValue() elimina ese bloqueo.
                item.ClearValue(Border.BackgroundProperty);
                item.ClearValue(UIElement.EffectProperty);
                item.ClearValue(UIElement.OpacityProperty); // La opacidad 0.4 del Style Setter toma efecto

                // Resetear color del ícono hijo
                var tb = FindChildTextBlock(item);
                if (tb != null)
                    tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0B890"));
            }

            // Botón activo: valor local con mayor prioridad que el trigger hover
            activeItem.Opacity = 1.0;
            activeItem.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xE8, 0xB8, 0x20));
            activeItem.Effect = new DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString("#E8B820"),
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.6
            };

            var activeTb = FindChildTextBlock(activeItem);
            if (activeTb != null)
                activeTb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8B820"));
        }

        /// <summary>Busca el primer TextBlock descendiente de un Border (puede estar dentro de un Grid intermedio).</summary>
        private static TextBlock? FindChildTextBlock(Border border)
        {
            if (border.Child is TextBlock tb) return tb;
            if (border.Child is Panel panel)
                foreach (var child in panel.Children)
                    if (child is TextBlock tb2) return tb2;
            return null;
        }

        #endregion

        #region Motor de Modales Inmersivos y Custom (Pop-ups)

        public async Task<object?> ShowModalAsync(string title, string message, System.Collections.Generic.IEnumerable<ModalButton> buttons, bool showInput = false, string? errorText = null, Func<string, (bool Valid, string Error)>? validator = null)
        {
            var asyncResolver = new TaskCompletionSource<object?>();
            _currentModalValidator = validator;

            Dispatcher.Invoke(() =>
            {
                ModalTitle.Text = title.ToUpper();
                ModalMessage.Text = message;
                ModalButtonsContainer.Children.Clear();

                ModalInputContainer.Visibility = showInput ? Visibility.Visible : Visibility.Collapsed;
                ModalInputBox.Text = string.Empty;

                ModalInputError.Text = errorText ?? string.Empty;
                ModalInputError.Visibility = !string.IsNullOrEmpty(errorText) ? Visibility.Visible : Visibility.Hidden;

                RefrescarPlaceholderEnModal();

                foreach (var btnModelo in buttons)
                {
                    var botonOpcion = new Button
                    {
                        Content = btnModelo.Text.ToUpper(),
                        Margin = new Thickness(10, 0, 0, 0),
                        Padding = new Thickness(30, 8, 30, 8),
                        MinWidth = 120,
                        Height = 40,
                        Style = (Style)Application.Current.FindResource("PlayBtn"),
                        Tag = btnModelo.Result
                    };

                    try
                    {
                        if (!string.IsNullOrEmpty(btnModelo.Color))
                        {
                            botonOpcion.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(btnModelo.Color));
                        }
                    }
                    catch { /* Fallback ignorado al default de CSS XAML */ }

                    botonOpcion.Click += (s, e) =>
                    {
                        if (showInput)
                        {
                            if (btnModelo.Result?.ToString() == "CANCEL")
                            {
                                ModalLayer.Visibility = Visibility.Collapsed;
                                asyncResolver.SetResult(null);
                                return;
                            }

                            string payloadFinal = ModalInputBox.Text.Trim();
                            if (string.IsNullOrWhiteSpace(payloadFinal)) return;

                            ModalLayer.Visibility = Visibility.Collapsed;
                            asyncResolver.SetResult(payloadFinal);
                        }
                        else
                        {
                            ModalLayer.Visibility = Visibility.Collapsed;
                            asyncResolver.SetResult(((Button)s).Tag);
                        }
                    };

                    ModalButtonsContainer.Children.Add(botonOpcion);
                }

                if (showInput && _currentModalValidator != null) AuditarEstadoInputEnModal();

                ModalLayer.Visibility = Visibility.Visible;
            });

            return await asyncResolver.Task;
        }

        private void ModalInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefrescarPlaceholderEnModal();
            if (_currentModalValidator != null) AuditarEstadoInputEnModal();
        }

        private void AuditarEstadoInputEnModal()
        {
            if (_currentModalValidator == null) return;

            var (isValid, detectedError) = _currentModalValidator(ModalInputBox.Text);

            ModalInputError.Text = detectedError;
            ModalInputError.Visibility = string.IsNullOrEmpty(detectedError) ? Visibility.Hidden : Visibility.Visible;

            foreach (var nodeUI in ModalButtonsContainer.Children)
            {
                if (nodeUI is Button domBoton)
                {
                    bool IsCancelType = domBoton.Tag?.ToString() == "CANCEL";
                    if (!IsCancelType)
                    {
                        domBoton.IsEnabled = isValid;
                        domBoton.Opacity = isValid ? 1.0 : 0.4;
                    }
                }
            }
        }

        private void RefrescarPlaceholderEnModal() =>
            ModalInputPlaceholder.Visibility = string.IsNullOrEmpty(ModalInputBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        #endregion

        #region Comportamientos de OS Ventana

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        #endregion
    }
}

