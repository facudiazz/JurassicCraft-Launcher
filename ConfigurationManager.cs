using System;
using System.IO;
using System.Text.Json;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// Administra la carga y persistencia de la configuración del usuario en el disco,
    /// asegurando que siempre exista una instancia de configuración válida en memoria.
    /// </summary>
    public class ConfigurationManager
    {
        /// <summary>
        /// Configuración actual en memoria.
        /// </summary>
        public LauncherConfig Config { get; private set; }

        public ConfigurationManager()
        {
            Config = new LauncherConfig(); // Inicialización por defecto pre-carga
        }

        #region Manejo de Configuración

        /// <summary>
        /// Carga la configuración desde el archivo en disco.
        /// Si el archivo no existe o está corrupto, crea una nueva configuración base.
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(AppConstants.ConfigFile))
                {
                    string json = File.ReadAllText(AppConstants.ConfigFile);
                    Config = JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
                }
                else
                {
                    Config = new LauncherConfig();
                    Save(); // Persistir los valores iniciales.
                }
            }
            catch (Exception)
            {
                // En caso de corrupción JSON u otro error, forzamos reinicio de la config.
                Config = new LauncherConfig();
            }
        }

        /// <summary>
        /// Persiste el estado actual de la configuración de forma segura en disco, 
        /// creando los directorios necesarios si no existieran.
        /// </summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppConstants.AppDataDir);
                
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Config, options);
                
                File.WriteAllText(AppConstants.ConfigFile, json);
            }
            catch (Exception ex)
            {
                // Podría lanzarse una advertencia o ser interceptada en niveles superiores
                System.Diagnostics.Debug.WriteLine($"Error guardando configuración: {ex.Message}");
            }
        }

        #endregion

        #region Propiedades de Conveniencia

        /// <summary>
        /// Establece o actualiza el nombre de usuario y guarda los cambios enseguida.
        /// </summary>
        public void UpdateUsername(string username)
        {
            Config.Username = username;
            Save();
        }

        /// <summary>
        /// Actualiza la RAM máxima y guarda los cambios enseguida.
        /// </summary>
        public void UpdateMaxRam(int ramMb)
        {
            Config.MaxRamMb = ramMb;
            Save();
        }

        /// <summary>
        /// Actualiza el perfil gráfico y guarda.
        /// </summary>
        public void UpdateGraphicsPreset(string preset)
        {
            Config.GraphicsPreset = preset;
            Save();
        }

        /// <summary>
        /// Indica si el sistema debe cargar los argumentos personalizados del JVM.
        /// </summary>
        public void UpdateAdvancedJvmToggle(bool useAdvancedArgs)
        {
            Config.UseCustomJvmArgs = useAdvancedArgs;
            Save();
        }

        /// <summary>
        /// Permite guardar cadenas especiales de arranque y guarda de inmediato.
        /// </summary>
        public void UpdateJvmArguments(string arguments)
        {
            Config.CustomJvmArgs = arguments;
            Save();
        }

        #endregion
    }
}
