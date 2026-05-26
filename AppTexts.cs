namespace JurassicCraftLauncher
{
    /// <summary>
    /// Repositorio central de textos visibles del launcher.
    /// Primero se agrupan textos normales de UI, estados y errores, y al final solo logs.
    /// </summary>
    public static class AppTexts
    {
        public const string AppName = "JurassicCraft Launcher";

        #region Textos Normales

        public const string PlayButtonLabel = "JUGAR";
        public const string PlayButtonIcon = "\u25B6  ";
        public const string ServerActiveLabel = "SERVIDOR ACTIVO";

        public const string WinBtnMinimize = "\u2500";
        public const string WinBtnClose = "\u2715";

        public const string NavIconHome = "\uE80F";
        public const string NavIconSkins = "\uE136";
        public const string NavIconSettings = "\uE713";
        public const string UserEditIcon = "\uE104";
        public const string UserAvatarIcon = "\u263B";

        public const string UserEditTooltip = "Editar Nombre";
        public const string ModalUserInputPlaceholder = "ESCRIBE TU NOMBRE AQUI...";
        public const string ModalDefaultTitle = "TITULO DEL AVISO";
        public const string ModalDefaultMessage = "Este es el mensaje del popup.";

        public const string StatusWaiting = "Listo para jugar";
        public const string StatusRequiresUsername = "\u26A0 Escribe un nombre de usuario primero.";
        public const string StatusCheckingUpdates = "\u25CC Buscando actualizaciones...";
        public const string StatusCheckingForge = "\u25CC Verificando Forge...";
        public const string StatusSyncingModpack = "\u25CC Sincronizando modpack...";
        public const string StatusSyncingProgress = "\u2193 Sincronizando modpack: {0}/{1}";
        public const string StatusDownloadingResources = "\u25CC Cargando recursos de Minecraft...";
        public const string StatusInstallingJava = "\u25CC Descargando Java 17...";
        public const string StatusJavaDownloadProgress = "Descargando Java 17: {0}%";
        public const string StatusJavaExtracting = "Descomprimiendo Java 17... (Puede tardar)";
        public const string StatusOpeningGame = "\u2713 \u00A1Abriendo Minecraft!";
        public const string StatusLaunchError = "\u2717 Error durante el inicio";
        public const string StatusLauncherUpdating = "\u2191 Actualizando launcher...";
        public const string StatusLauncherUpdateError = "\u2717 Fuga en actualizacion";
        public const string StatusLauncherUpdateStarting = "Iniciando actualizacion del launcher...";
        public const string StatusLauncherUpdateCompleted = "\u00A1Descarga completa! Reiniciando...";
        public const string StatusLauncherUpdateProgress = "Actualizando launcher ({0}%)";
        public const string StatusUpdaterProgress = "\u2191 {0}";
        public const string StatusOperationSuccess = "\u2713 Operacion finalizada exitosa.";

        public const string UserEditTitle = "EDITAR NOMBRE DE USUARIO";
        public const string UserNewTitle = "BIENVENIDO";
        public const string UserEditMessage = "Precaucion: Al cambiar el nombre de usuario, el juego te tratara como un jugador nuevo.";
        public const string UserNewMessage = "Ingresa un nombre de usuario para identificarte en el juego:";
        public const string UserBtnSave = "GUARDAR";
        public const string UserBtnAdd = "ACEPTAR";
        public const string UserBtnCancel = "CANCELAR";

        public const string SettingsPageTitle = "CONFIGURACION";
        public const string SettingsSectionGraphicsTitle = "CALIDAD GRAFICA";
        public const string SettingsSectionGraphicsDesc = "Selecciona la configuracion grafica del juego para que se ajuste al rendimiento de tu PC. Estos pre-ajustes se aplicaran sobre la configuracion del juego en el proximo lanzamiento.";
        public const string GraphicsLow = "BAJO";
        public const string GraphicsMedium = "MEDIO";
        public const string GraphicsHigh = "ALTO";

        public const string SettingsSectionRamTitle = "ASIGNACION DE MEMORIA RAM";
        public const string SettingsSectionRamDesc = "Especifica el limite maximo de memoria RAM consumible para el juego. Un valor alto permite renderizar mas chunks y cargar mas rapidamente los mods pero puede afectar el rendimiento del sistema.";

        public const string SettingsSectionJvmTitle = "ARGUMENTOS DE JAVA (AVANZADO)";
        public const string SettingsSectionJvmDesc = "Permite anadir flags personalizados para ajustar el funcionamiento de Java. Estos ajustes se aplicaran junto a los argumentos predeterminados del juego.";
        public const string SettingsJvmToggleLabel = "ACTIVAR";

        public const string SettingsSectionWipeTitle = "LIMPIEZA DE ARCHIVOS";
        public const string SettingsSectionWipeDesc = "Utiliza estas herramientas para solucionar problemas de rendimiento o archivos danados. Ten en cuenta que al seleccionar una opcion se eliminaran por completo los archivos de esa categoria.";
        public const string WipeBtnConfig = "CONFIGURACION";
        public const string WipeBtnShaders = "SHADERS";
        public const string WipeBtnResources = "TEXTURAS";
        public const string WipeBtnEngine = "RECURSOS DEL JUEGO";
        public const string WipeBtnCache = "BORRAR CACHE";
        public const string WipeBtnTotal = "BORRAR TODO";

        public const string WipeTitle = "LIMPIEZA DE ARCHIVOS";
        public const string WipeMessageSuccess = "Los archivos seleccionados han sido limpiados de forma irrecuperable con exito.";
        public const string WipeBtnAcknowledge = "OK";
        public const string WipeConfigWarning = "\u00BFEstas seguro que deseas borrar todas las configuraciones de los mods, controles, gráficos y de sonido? El juego volvera a sus ajustes predeterminados.";
        public const string WipeShadersWarning = "\u00BFEstas seguro que deseas eliminar todos los paquetes de shaders instalados y sus configuraciones personalizadas?";
        public const string WipeResourcesWarning = "\u00BFEstas seguro que deseas borrar todos los paquetes de texturas?";
        public const string WipeEngineWarning = "\u00BFEstas seguro que deseas eliminar los recursos compilados del juego y sus librerias? Esto forzara una descarga tardada.";
        public const string WipeCacheWarning = "\u00BFEstas seguro que deseas borrar la cache del juego? Esto eliminara archivos temporales, logs, cache de mods, HLODs de Distant Horizons y otros datos regenerables.";
        public const string WipeTotalWarning = "PELIGRO: \u00BFDeseas eliminar todos los archivos de JurassicCraft? (incluyendo capturas y configuraciones). Esto no afectara a tus otras instancias de Minecraft externas a este launcher.";
        public const string WipeFallbackWarning = "\u00BFBorrar elementos?";

        public const string ModalErrorTitle = "ERROR DE LANZAMIENTO";
        public const string ModalSystemErrorTitle = "ERROR DE ACTUALIZACION";
        public const string ModalErrorGenericPrefix = "Ocurrio un problema critico abortando el inicio:\n\n";
        public const string ModalSystemUpdateFailed = "No se pudo auto-actualizar el launcher base:\n";
        public const string ModalBtnUnderstood = "ENTENDIDO";
        public const string ModalBtnSkip = "OMITIR";
        public const string ModalBtnYes = "SI, ESTOY SEGURO";
        public const string ModalBtnNo = "NO, CANCELAR";

        public const string SkinsPageTitle = "SELECCION DE SKIN";
        public const string SkinsImportBtn = "IMPORTAR PNG";
        public const string SkinsSearchPlaceholder = "Buscar skins de Minecraft";
        public const string SkinsSearchBtn = "BUSCAR";
        public const string SkinsLoadingDefault = "Cargando skins...";
        public const string SkinsLoadingSearchFormat = "Buscando \"{0}\"...";
        public const string SkinsEmptyLine1 = "Busca skins de Minecraft";
        public const string SkinsEmptyLine2 = "Los resultados apareceran aqui";
        public const string SkinsErrorDefault = "No se pudo cargar";
        public const string SkinsErrorNoResultsFormat = "No se encontraron skins para \"{0}\".";
        public const string SkinsImportDialogTitle = "Seleccionar skin de Minecraft (PNG)";
        public const string SkinsImportDialogFilter = "Skin de Minecraft (*.png)|*.png";
        public const string SkinsApplyErrorFormat = "Error al aplicar la skin \"{0}\": {1}";
        public const string SkinsImportErrorFormat = "Error al importar la skin: {0}";
        public const string SkinsDialogErrorTitle = "ERROR";
        public const string SkinsFallbackLabel = "Skin";

        public const string ErrorUserSpaces = "\u26A0 PROHIBIDOS ESPACIOS INTERMEDIOS";
        public const string ErrorUserDash = "\u26A0 GUIONES MEDIOS (-) PROHIBIDOS";
        public const string ErrorUserChar = "\u26A0 CARACTER RESTRINGIDO";

        public const string ErrorForgeInstallFailed = "El desempaque del motor de Forge fue abortado por un Error I/O Interno de Java.";
        public const string ErrorJavaDownloadFailedFormat = "Fallo al descargar Java 17. Codigo: {0} - {1}";
        public const string ErrorUpdaterNoAssets = "La ultima publicacion no contiene ejecutables (Assets).";
        public const string ErrorUpdaterExecutableUnknown = "No se pudo determinar el ejecutable en proceso activo.";
        public const string ErrorUpdaterDownloadDeniedFormat = "Servidor de GitHub denego la descarga: Estado HTTP {0}.";

        #endregion

        #region Logs

        public const string LogSyncingFileFormat = "Descargando: {0}";
        public const string LogForgeValidatedFormat = "Forge {0} se encuentra validado y presente.";
        public const string LogForgeDownloadStart = "Descargando ecosistema de Forge...";
        public const string LogForgeExtracting = "Descomprimiendo librerias Forge...";
        public const string LogForgeErrorPrefixFormat = "[FGE] {0}";
        public const string LogResourcesValidatingFormat = "Validando recursos: {0}/{1}";
        public const string LogResourceAnalyzedFormat = "Analizado: {0}";
        public const string LogTrafficFormat = "Trafico: {0}MB / {1}MB";
        public const string LogLaunchAdjustingProfile = "Ajustando perfil de rendimiento y argumentos...";
        public const string LogJvmArgsProcessedFormat = "Argumentos procesados: JVM Base={0}, JVM Extra={1}, Game={2}";
        public const string LogLaunchCommandFormat = "[CMD]: {0}";
        public const string LogLaunchDelegatingKernel = "Generacion exitosa. Delegando control del Kernel a JVM.";
        public const string LogGameOutputFormat = "[GAME]: {0}";
        public const string LogGameErrorFormat = "[ERR]: {0}";

        #endregion
    }
}
