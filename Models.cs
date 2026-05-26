using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JurassicCraftLauncher
{
    #region Launcher configuration

    /// <summary>
    /// Representa la persistencia de las preferencias del usuario.
    /// Almacenado como JSON localmente en AppConstants.ConfigFile.
    /// </summary>
    public class LauncherConfig
    {
        /// <summary>
        /// El nombre de usuario que se utilizará para iniciar sesión de forma offline.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// La cantidad máxima de memoria RAM (en Megabytes) asiganda a la instancia de Java.
        /// </summary>
        public int MaxRamMb { get; set; } = 4096;

        /// <summary>
        /// Nivel seleccionado de calidad gráfica configurable ("Low", "Medium", "High").
        /// </summary>
        public string GraphicsPreset { get; set; } = "Medium";

        /// <summary>
        /// Define si se deben adjuntar argumentos manuales a la JVM.
        /// </summary>
        public bool UseCustomJvmArgs { get; set; } = false;

        /// <summary>
        /// Cadena de texto libre que contiene configuraciones estilo '-XX:+UseG1GC' etc.
        /// </summary>
        public string CustomJvmArgs { get; set; } = string.Empty;
    }

    #endregion

    #region Configuración del Modpack (Local / GitHub)

    /// <summary>
    /// Estructura de la metadata base del modpack. Determina qué versiones
    /// de Minecraft y Módulos de Forge han de cargarse.
    /// </summary>
    public class ModpackInfo
    {
        [JsonPropertyName("minecraft_version")]
        public string? MinecraftVersion { get; set; }

        [JsonPropertyName("mod_loader")]
        public string? ModLoader { get; set; }

        [JsonPropertyName("mod_loader_version")]
        public string? ModLoaderVersion { get; set; }
    }

    /// <summary>
    /// Estructura raíz del manifiesto de actualización descargado del repositorio de GitHub.
    /// </summary>
    public class Manifest
    {
        /// <summary>
        /// Lista conteniendo todos los archivos (mods, configs, scripts) auditados que componen el modpack.
        /// </summary>
        [JsonPropertyName("files")]
        public List<ManifestFile>? Files { get; set; }
    }

    /// <summary>
    /// Metadatos sobre un archivo único dentro del modpack.
    /// </summary>
    public class ManifestFile
    {
        /// <summary>
        /// Ruta relativa dentro del directorio principal del juego. Ejemplo: 'mods/jei.jar'
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Hash SHA256 del archivo utilizado para corroborar la integridad y detectar si requiere actualización.
        /// </summary>
        [JsonPropertyName("hash")]
        public string? Hash { get; set; }

        /// <summary>
        /// El comportamiento de imposición de descarga. 
        /// "forced" significa que siempre debe igualar el hash del master (útil para configs cruciales).
        /// "default" significa que solo se descarga una vez para permitir la edición local del usuario.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Determina el código del Asset si el archivo proviene de una Release de GitHub en vez del código fuente.
        /// Útil para archivos grandes como mods superiores a 100MB.
        /// </summary>
        [JsonPropertyName("asset_id")]
        public long? AssetId { get; set; }
    }

    #endregion

    #region Componentes de la UI

    /// <summary>
    /// Modelo simple empleado por el motor de Modales (Pop-ups) de la interfaz
    /// para configurar el texto y acción que debe retornar cada botón.
    /// </summary>
    public class ModalButton
    {
        /// <summary>
        /// Texto que se mostrará en el cuerpo del botón.
        /// </summary>
        public string Text { get; set; } = "Aceptar";

        /// <summary>
        /// Valor en hex del color de fondo o acentuación del botón.
        /// </summary>
        public string Color { get; set; } = "#E8B820"; // Dorado original por defecto

        /// <summary>
        /// Objeto arbitrario devuelto a la Task del modal cuando el botón es presionado.
        /// Útil para discernir si se hizo 'OK', 'CANCEL', etc.
        /// </summary>
        public object? Result { get; set; } 
    }

    #endregion
}
