using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Installers;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// Servicio responsable de gestionar el núcleo primario del juego (El Motor).
    /// Se encargará de invocar CmlLibCore para instalar Forge y las utilidades Vainilla
    /// y proceder con el lanzamiento del proceso pesado de Java.
    /// </summary>
    public class MinecraftService
    {
        #region Propiedades e Inicialización

        private readonly MinecraftLauncher _launcherCore;
        private readonly string _gameDir;

        /// <summary>
        /// Evento notificador de estado textual. Generalmente enlazado a la label de Log.
        /// </summary>
        public event Action<string>? LogUpdate;

        /// <summary>
        /// Evento notificador de progreso. (Porcentaje [0-1], Texto de UI primario)
        /// </summary>
        public event Action<double, string>? ProgressUpdate;

        public MinecraftService(string gameDir)
        {
            _gameDir = gameDir;
            var mcPath = new MinecraftPath(gameDir);
            _launcherCore = new MinecraftLauncher(mcPath);
        }

        #endregion

        #region Instalación Base (Forge, Vanilla)

        /// <summary>
        /// Proceso semi manual que se salta la API publicitaria y los wrappers 
        /// de Forge inyectando el `.jar` en headless mode en una JVM.
        /// Retorna el id resultante de la versión, ej: "1.20.1-forge-47.4.10"
        /// </summary>
        public async Task<string> InstallForgeAsync(string mcVersion, string forgeVersion)
        {
            string versionId = $"{mcVersion}-forge-{forgeVersion}";
            string forgeJsonFile = Path.Combine(_gameDir, "versions", versionId, $"{versionId}.json");

            if (File.Exists(forgeJsonFile))
            {
                FireLogUpdate(string.Format(AppTexts.LogForgeValidatedFormat, forgeVersion));
                return versionId;
            }

            // 1. Descarga el paquete Installer crudo de Maven (Sin redicreciones Focus)
            string installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";
            string installerTempPath = Path.Combine(Path.GetTempPath(), $"forge-{mcVersion}-{forgeVersion}-installer.jar");

            if (!File.Exists(installerTempPath))
            {
                FireProgressUpdate(0, AppTexts.LogForgeDownloadStart);
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "JurassicCraftLauncher");
                var jarBytes = await http.GetByteArrayAsync(installerUrl);
                await File.WriteAllBytesAsync(installerTempPath, jarBytes);
            }

            // 2. Crear perfiles JSON Dummy (Exigido estrcitamente por el Installer de forge para evitar crash)
            string profilesConfPath = Path.Combine(_gameDir, "launcher_profiles.json");
            if (!File.Exists(profilesConfPath))
                File.WriteAllText(profilesConfPath, "{\"profiles\":{},\"selectedProfile\":null,\"clientToken\":\"\",\"authenticationDatabase\":{}}");

            // 3. Ejecutar consola Headless del Instalador
            string javaBinaryPath = TryLocateJava17();
            FireProgressUpdate(0.5, AppTexts.LogForgeExtracting);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaBinaryPath,
                    Arguments = $"-Djava.awt.headless=true -jar \"{installerTempPath}\" --installClient",
                    WorkingDirectory = _gameDir,
                    UseShellExecute = false,              // Requerido falso para Stream de Salida
                    CreateNoWindow = true,               // Sin pop-up negro CMD
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) FireLogUpdate(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) FireLogUpdate(string.Format(AppTexts.LogForgeErrorPrefixFormat, e.Data)); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException(AppTexts.ErrorForgeInstallFailed);

            // Reconocer versiones instaladas para el core del indexador
            await _launcherCore.GetAllVersionsAsync();
            return versionId;
        }

        /// <summary>
        /// Dispara la resolución de dependencias por CmlLib. Descargará assets, binarios nativos
        /// texturas, sonidos y librerías externas faltantes (LWJGL).
        /// </summary>
        public async Task InstallMinecraftResourcesAsync(string targetVersionId)
        {
            var filesProgression = new Progress<InstallerProgressChangedEventArgs>(report =>
            {
                double pct = report.TotalTasks > 0 ? (double)report.ProgressedTasks / report.TotalTasks : 0;
                FireProgressUpdate(pct, string.Format(AppTexts.LogResourcesValidatingFormat, report.ProgressedTasks, report.TotalTasks));

                if (!string.IsNullOrEmpty(report.Name))
                    FireLogUpdate(string.Format(AppTexts.LogResourceAnalyzedFormat, report.Name));
            });

            var netProgression = new Progress<ByteProgress>(report =>
            {
                if (report.TotalBytes > 0)
                    FireLogUpdate(string.Format(AppTexts.LogTrafficFormat, report.ProgressedBytes / 1024 / 1024, report.TotalBytes / 1024 / 1024));
            });

            await _launcherCore.InstallAsync(targetVersionId, filesProgression, netProgression);
        }

        #endregion

        #region Descarga Automática de Java 17 (JRE)

        /// <summary>
        /// Comprueba si el usuario carece de Java 17 en su sistema operativo.
        /// De ser así, instanciará una descarga silenciosa de Eclipse Adoptium (Temurin).
        /// </summary>
        public async Task EnsureJava17InstalledAsync()
        {
            string currentJava = TryLocateJava17();

            // Si TryLocateJava17 devuelve el fallback bruto (no encontró paths absolutos), entonces forzar instalación.
            if (currentJava != "javaw.exe" && File.Exists(currentJava))
            {
                // Ya tiene Java 17
                return;
            }

            FireLogUpdate(AppTexts.StatusInstallingJava);

            string runtimeDir = Path.Combine(AppConstants.AppDataDir, "runtime");
            string zipTempPath = Path.Combine(Path.GetTempPath(), "temurin17-jre.zip");

            Directory.CreateDirectory(runtimeDir);

            // API Oficial de Eclipse Adoptium para la 'latest' standard build JRE de Windows x64.
            string downloadUrl = "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JurassicCraftLauncher");

            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new Exception(string.Format(AppTexts.ErrorJavaDownloadFailedFormat, response.StatusCode, response.ReasonPhrase));

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var fs = new FileStream(zipTempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var downloadStream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[81920];
            var bytesRead = 0L;
            int read;

            while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fs.WriteAsync(buffer, 0, read);
                bytesRead += read;

                if (totalBytes != -1)
                {
                    double progress = (double)bytesRead / totalBytes;
                    FireProgressUpdate(progress, string.Format(AppTexts.StatusJavaDownloadProgress, (int)(progress * 100)));
                }
            }

            fs.Close();

            FireProgressUpdate(1, AppTexts.StatusJavaExtracting);

            // Si ya existieran archivos rotos viejos, limpiarlos antes de extraer
            string javaExtractDir = Path.Combine(runtimeDir, "java17");
            if (Directory.Exists(javaExtractDir))
            {
                try { Directory.Delete(javaExtractDir, true); } catch { }
            }

            Directory.CreateDirectory(javaExtractDir);

            // Extrae el Zip en AppData/runtime/java17
            ZipFile.ExtractToDirectory(zipTempPath, javaExtractDir, overwriteFiles: true);

            try { File.Delete(zipTempPath); } catch { }
        }

        #endregion

        #region Lanzamiento

        /// <summary>
        /// Fabrica los argumentos del entorno, token offline y ejecuta la sesión de juego.
        /// </summary>
        public async Task LaunchAsync(string versionId, string offlineUsername, int maxRamAlloc, string customJvmArgs = "")
        {
            FireLogUpdate(AppTexts.LogLaunchAdjustingProfile);

            string javaPath = TryLocateJava17();
            var authSession = MSession.CreateOfflineSession(offlineUsername);

            var options = new MLaunchOption
            {
                Session = authSession,
                MaximumRamMb = maxRamAlloc,
                JavaPath = javaPath,
                ScreenWidth = 854,
                ScreenHeight = 480
            };

            // ──────────────────────────────────────────────────────────────
            // ARGS BASE: Siempre presentes. Optimizados para Forge 1.20.1
            // con modpack mediano. Se suman SIEMPRE, independiente del input.
            // ──────────────────────────────────────────────────────────────
            var baseJvmArgs = new List<MArgument>
            {
                // Garbage Collector G1 (igual al launcher oficial de Mojang)
                new("-XX:+UnlockExperimentalVMOptions"),
                new("-XX:+UseG1GC"),
                new("-XX:G1NewSizePercent=20"),
                new("-XX:G1ReservePercent=20"),
                new("-XX:MaxGCPauseMillis=50"),
                new("-XX:G1HeapRegionSize=32M"),

                // Estabilidad de red — fix principal para timeouts en servidores
                new("-Djava.net.preferIPv4Stack=true"),
                new("-Djava.net.useSystemProxies=false"),

                // Timeouts extendidos — crítico para Forge con modpack grande
                // Evita el "timed out" al conectar a servidores mientras cargan los mods
                new("-Dfml.readTimeout=120"),
                new("-Dfml.loginTimeout=120"),

                // Mejoras adicionales de rendimiento
                new("-XX:+ParallelRefProcEnabled"),
                new("-XX:+DisableExplicitGC"),
                new("-XX:+AlwaysPreTouch"),
                new("-XX:-UsePerfData"),
            };

            // Inyectar argumentos adicionales si el usuario los definió
            var extraJvmArgs = new List<MArgument>();
            var gameArgs = new List<MArgument>();

            // Inyectar argumentos adicionales si el usuario los definió
            if (!string.IsNullOrWhiteSpace(customJvmArgs))
            {
                var tokens = Regex.Matches(customJvmArgs, @"[\""].+?[\""]|[^ ]+")
                                .Cast<Match>()
                                .Select(m => m.Value)
                                .ToList();

                for (int i = 0; i < tokens.Count; i++)
                {
                    string token = tokens[i];
                    if (token.StartsWith("--"))
                    {
                        if (token.StartsWith("--width") || token.StartsWith("--height"))
                        {
                            string key = token.Contains('=') ? token.Split('=')[0] : token;
                            string? valStr = token.Contains('=') ? token.Split('=')[1] : (i + 1 < tokens.Count ? tokens[i + 1] : null);
                            if (int.TryParse(valStr, out int val))
                            {
                                if (key == "--width") options.ScreenWidth = val;
                                else options.ScreenHeight = val;
                                if (!token.Contains('=')) i++;
                                continue;
                            }
                        }
                        gameArgs.Add(new MArgument(token));
                    }
                    else if (token.StartsWith("-"))
                    {
                        extraJvmArgs.Add(new MArgument(token));
                    }
                    else
                    {
                        gameArgs.Add(new MArgument(token));
                    }
                }
            }

            // Combinar base + usuario (el usuario puede sobreescribir/agregar encima)
            var allJvmArgs = baseJvmArgs.Concat(extraJvmArgs).ToArray();
            options.ExtraJvmArguments = allJvmArgs;

            if (gameArgs.Any()) options.ExtraGameArguments = gameArgs.ToArray();

            FireLogUpdate(string.Format(AppTexts.LogJvmArgsProcessedFormat, baseJvmArgs.Count, extraJvmArgs.Count, gameArgs.Count));

            var process = await _launcherCore.BuildProcessAsync(versionId, options);

            // Trazabilidad de lanzamiento (Ofuscamos el token por seguridad)
            string debugArgs = process.StartInfo.Arguments;
            if (!string.IsNullOrEmpty(options.Session?.AccessToken))
                debugArgs = debugArgs.Replace(options.Session.AccessToken, "HIDDEN_TOKEN");

            FireLogUpdate(string.Format(AppTexts.LogLaunchCommandFormat, debugArgs));
            FireLogUpdate(AppTexts.LogLaunchDelegatingKernel);

            // Configuramos un lanzamiento silencioso de la consola anexanada
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.EnableRaisingEvents = true;

            // Pipeline Log de Minecraft al UI del Launcher (Útil para debugeo)
            process.OutputDataReceived += (s, ev) => { if (!string.IsNullOrEmpty(ev.Data)) FireLogUpdate(string.Format(AppTexts.LogGameOutputFormat, ev.Data)); };
            process.ErrorDataReceived += (s, ev) => { if (!string.IsNullOrEmpty(ev.Data)) FireLogUpdate(string.Format(AppTexts.LogGameErrorFormat, ev.Data)); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        #endregion

        #region Utilidades (Resolución Java)

        /// <summary>
        /// Escanea exhaustivamente directorios globales y temporales de Microsoft en busca
        /// de un JRE versión 17 (Requisito fundamental para MC 1.20)
        /// </summary>
        private string TryLocateJava17()
        {
            try
            {
                var baseDirs = new List<string>();

                string? envJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(envJavaHome)) baseDirs.Add(envJavaHome);

                string varPF = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
                string varPF86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";

                string[] searchTree = { varPF, varPF86 };

                foreach (var folderNode in searchTree)
                {
                    baseDirs.Add(Path.Combine(folderNode, "Java"));
                    baseDirs.Add(Path.Combine(folderNode, "Eclipse Adoptium"));
                    baseDirs.Add(Path.Combine(folderNode, "BellSoft"));
                }

                // NUEVO: Priorizar nuestra propia carpeta custom de Java descargado (si existe)
                string customRuntimeDir = Path.Combine(AppConstants.AppDataDir, "runtime");
                if (Directory.Exists(customRuntimeDir))
                {
                    string[] recursiveHits = Directory.GetFiles(customRuntimeDir, "javaw.exe", SearchOption.AllDirectories);
                    if (recursiveHits.Length > 0) return recursiveHits[0];
                }

                // Barrido algorítmico profundo
                foreach (var absoluteBaseFolder in baseDirs)
                {
                    if (!Directory.Exists(absoluteBaseFolder)) continue;

                    var rapidHit = Path.Combine(absoluteBaseFolder, "bin", "javaw.exe");
                    if (File.Exists(rapidHit) && absoluteBaseFolder.Contains("17")) return rapidHit;

                    foreach (var specificVersionFolder in Directory.GetDirectories(absoluteBaseFolder))
                    {
                        if (specificVersionFolder.Contains("17") || specificVersionFolder.Contains("17."))
                        {
                            var hit = Path.Combine(specificVersionFolder, "bin", "javaw.exe");
                            if (File.Exists(hit)) return hit;
                        }
                    }
                }

                // Barrido final en la caché de Microsoft Store (Mojang Legacy Launcher default route)
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string runtimeGamma = Path.Combine(localAppData, "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime", "java-runtime-gamma", "windows-x64", "java-runtime-gamma", "bin", "javaw.exe");

                if (File.Exists(runtimeGamma)) return runtimeGamma;

            }
            catch { /* Falla Silenciosa permitida */ }

            // Retorno al Path de variables de entorno global
            return "javaw.exe";
        }

        private void FireLogUpdate(string message) => LogUpdate?.Invoke(message);
        private void FireProgressUpdate(double progress, string message) => ProgressUpdate?.Invoke(progress, message);

        #endregion
    }
}

