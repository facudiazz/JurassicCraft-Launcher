using System;
using System.IO;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// Centraliza todas las constantes y configuraciones globales de la aplicación.
    /// Evita tener valores quemados ("hardcodeados") en múltiples clases.
    /// </summary>
    public static class AppConstants
    {
        #region Directorios de la Aplicación

        /// <summary>
        /// Directorio base en AppData donde se guardará toda la información del Launcher y el juego.
        /// </summary>
        public static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JurassicCraft");

        /// <summary>
        /// Ruta al archivo de configuración del usuario (perfil, RAM seleccionada, etc).
        /// </summary>
        public static readonly string ConfigFile = Path.Combine(AppDataDir, "config.json");

        /// <summary>
        /// Ruta al archivo de configuración del backend remoto del launcher.
        /// </summary>
        public static readonly string BackendConfigFile = Path.Combine(AppDataDir, "backend.json");

        /// <summary>
        /// Directorio base donde se instalará Minecraft, Forge y el Modpack.
        /// </summary>
        public static readonly string GameDir = Path.Combine(AppDataDir, "game");

        #endregion

        #region Configuración de GitHub

        public static string GitHubToken => LauncherBackend.Current.GitHubToken;

        /// <summary>
        /// Nombre del propietario u organización en GitHub.
        /// </summary>
        public static string GitHubOwner => LauncherBackend.Current.GitHubOwner;

        /// <summary>
        /// Nombre del repositorio que contiene los archivos del Modpack (manifest, mods, config).
        /// </summary>
        public static string ModpackRepoName => LauncherBackend.Current.ModpackRepoName;

        /// <summary>
        /// Nombre del repositorio del Launcher (para buscar nuevas versiones o auto-updater).
        /// </summary>
        public static string LauncherRepoName => LauncherBackend.Current.LauncherRepoName;

        #endregion

        #region Versiones por Defecto

        /// <summary>
        /// Versión base de Minecraft en caso de que el manifest no lo declare correctamente.
        /// </summary>
        public static string DefaultMinecraftVersion => LauncherBackend.Current.DefaultMinecraftVersion;

        /// <summary>
        /// Versión de Forge (Modloader) por defecto.
        /// </summary>
        public static string DefaultForgeVersion => LauncherBackend.Current.DefaultForgeVersion;

        #endregion
    }
}
