using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// Servicio encargo de monitorear la existencia de nuevas versiones del propio Launcher
    /// a través de las Releases de GitHub, descargarlas y aplicar hot-swapping de forma transparente.
    /// </summary>
    public class UpdateService
    {
        /// <summary>
        /// Evento emitido a los suscriptores UI informando el porcentaje [0-1] y el mensaje de estado de descarga.
        /// </summary>
        public event Action<double, string>? ProgressChanged;

        #region Comprobación de Actualizaciones

        /// <summary>
        /// Realiza una solicitud autorizada a la API ReST de GitHub en busca de la "latest" Release del repositorio, 
        /// comparando su Tag (ej. v1.0.1) contra la versión local.
        /// </summary>
        public async Task<GitHubRelease?> CheckForUpdateAsync(string currentVersion)
        {
            try
            {
                using var client = CreateConfiguredHttpClient();
                string url = $"https://api.github.com/repos/{AppConstants.GitHubOwner}/{AppConstants.LauncherRepoName}/releases/latest";
                var response = await client.GetAsync(url);
                
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                
                var options = new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    PropertyNameCaseInsensitive = true 
                };
                
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, options);

                if (release == null || string.IsNullOrEmpty(release.TagName)) return null;

                if (IsNewerVersion(release.TagName, currentVersion)) 
                    return release;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Updater] Error buscando actualizaciones: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region Descarga e Instalación

        /// <summary>
        /// Descarga remotamente el Asset asumiendo un entorno en Windows
        /// e invoca un batch script que sustituye el propio .exe activo.
        /// </summary>
        public async Task DownloadAndApplyUpdate(GitHubRelease release)
        {
            if (release.Assets == null || release.Assets.Count == 0) 
                throw new InvalidOperationException(AppTexts.ErrorUpdaterNoAssets);

            var asset = release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? release.Assets[0];
            string downloadUrl = asset.BrowserDownloadUrl; 
            
            string currentPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName 
                ?? throw new InvalidOperationException(AppTexts.ErrorUpdaterExecutableUnknown);
                
            string tempNewPath = Path.Combine(Path.GetTempPath(), "JurassicCraftLauncher_Update.exe");

            ReportProgress(0, AppTexts.StatusLauncherUpdateStarting);

            using var client = CreateConfiguredHttpClient();
            var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            
            // Repositorios privados rechazan BrowserDownloadUrl anónimo, cambiamos a API estándar.
            if (!response.IsSuccessStatusCode)
            {
                var privateRequest = new HttpRequestMessage(HttpMethod.Get, asset.Url);
                if (!string.IsNullOrWhiteSpace(AppConstants.GitHubToken))
                {
                    privateRequest.Headers.Add("Authorization", $"token {AppConstants.GitHubToken}");
                }
                privateRequest.Headers.Add("User-Agent", "JurassicCraftLauncher");
                privateRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                
                response = await client.SendAsync(privateRequest, HttpCompletionOption.ResponseHeadersRead);
            }

            if (!response.IsSuccessStatusCode) 
                throw new HttpRequestException(string.Format(AppTexts.ErrorUpdaterDownloadDeniedFormat, response.StatusCode));

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await DownloadToDiscStreamed(response, tempNewPath, totalBytes);

            ReportProgress(1, AppTexts.StatusLauncherUpdateCompleted);
            await Task.Delay(1000); 

            ExecuteBatchSwap(currentPath, tempNewPath);
        }

        #endregion

        #region Utilidades Internas

        private async Task DownloadToDiscStreamed(HttpResponseMessage response, string tempNewPath, long totalBytes)
        {
            using var fileStream = new FileStream(tempNewPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var downloadStream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[81920]; // Chunk de 80 KB para mejor eficiencia
            var bytesRead = 0L;
            int read;

            while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                bytesRead += read;

                if (totalBytes != -1)
                {
                    double progress = (double)bytesRead / totalBytes;
                    ReportProgress(progress, string.Format(AppTexts.StatusLauncherUpdateProgress, (int)(progress * 100)));
                }
            }
        }

        private void ExecuteBatchSwap(string oldExe, string newExe)
        {
            string batchPath = Path.Combine(Path.GetTempPath(), "jurassiccraft_launcher_update.bat");
            int pid = Process.GetCurrentProcess().Id;

            // Batch Script auto-inmolativo para reemplazar binarios en Windows esquivando "File In Use".
            string script = $@"
@echo off
setlocal

:wait_process
tasklist /fi ""pid eq {pid}"" | findstr /i ""{pid}"" > nul
if %errorlevel% == 0 (
    timeout /t 1 /nobreak > nul
    goto wait_process
)

del /f /q ""{oldExe}""
move /y ""{newExe}"" ""{oldExe}""

:wait_file
if not exist ""{oldExe}"" (
    timeout /t 1 /nobreak > nul
    goto wait_file
)

start """" ""{oldExe}""
del ""%~f0""
";

            File.WriteAllText(batchPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Environment.Exit(0);
        }

        private HttpClient CreateConfiguredHttpClient()
        {
            var client = new HttpClient();
            if (!string.IsNullOrWhiteSpace(AppConstants.GitHubToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"token {AppConstants.GitHubToken}");
            }
            client.DefaultRequestHeaders.Add("User-Agent", "JurassicCraftLauncher-Updater");
            return client;
        }

        /// <summary>
        /// Extrae el valor semántico de un Tag eliminando convenciones como la letra inicial 'v' 
        /// u otros sufijos de entorno para comprobar si Latest > Current
        /// </summary>
        private bool IsNewerVersion(string latestTag, string currentVersion)
        {
            string latestClean = latestTag.TrimStart('v').Split('-')[0];
            string currentClean = currentVersion.TrimStart('v').Split('-')[0];

            if (Version.TryParse(latestClean, out var latest) && Version.TryParse(currentClean, out var current))
            {
                return latest > current;
            }
            return false;
        }

        private void ReportProgress(double progress, string message)
        {
            ProgressChanged?.Invoke(progress, message);
        }

        #endregion
    }

    #region Clases de Respuesta API Github

    public class GitHubRelease
    {
        public string TagName { get; set; } = "";
        public string Name { get; set; } = "";
        public System.Collections.Generic.List<GitHubAsset>? Assets { get; set; }
    }

    public class GitHubAsset
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = ""; // API URL restrictiva
        public string BrowserDownloadUrl { get; set; } = ""; // CDN pública ideal
        public long Size { get; set; }
    }

    #endregion
}

