using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// Servicio de sincronizaciÃ³n principal. Responsable de conectarse al repositorio
    /// en GitHub definido en AppConstants, analizar el manifiesto y asegurar que los 
    /// archivos locales del modpack estÃ©n Ã­ntegros e idempotentes.
    /// </summary>
    public class ModpackService
    {
        private static readonly TimeSpan LargeDownloadTimeout = TimeSpan.FromMinutes(45);

        #region Campos Privados y Rutas

        private readonly string _gameDir;
        private readonly string _manifestPath;
        private readonly string _infoPath;
        private readonly string _syncStatePath;

        /// <summary>
        /// Evento emitido a componentes UI suscribiÃ©ndose al progreso.
        /// Devuelve (archivoActual, totalArchivos, nombreDeArchivoDescargando).
        /// </summary>
        public event Action<int, int, string>? ProgressChanged;

        public ModpackService(string gameDir)
        {
            _gameDir      = gameDir;
            _manifestPath = Path.Combine(gameDir, "manifest.json");
            _infoPath     = Path.Combine(gameDir, "modpack-info.json");
            _syncStatePath = Path.Combine(gameDir, "sync_state.json");
        }

        #endregion

        #region Proceso Central de SincronizaciÃ³n

        /// <summary>
        /// Descarga Ãºnicamente los metadatos obligatorios (manifest e info) ignorando los binarios pesados.
        /// Este es usualmente el "Paso 1" del lanzamiento, vital para comprobar versiones de Forge/Minecraft.
        /// </summary>
        public async Task DownloadMetadata()
        {
            Directory.CreateDirectory(_gameDir);
            await DownloadFileTreeFromApi("modpack-info.json", _infoPath);
            await DownloadFileTreeFromApi("manifest.json", _manifestPath);
        }

        /// <summary>
        /// En base al manifest.json descargado en el Paso 1, lee los cambios requeridos 
        /// y lleva a cabo la descarga recursiva e inteligente de mods obsoletos/nuevos.
        /// </summary>
        public async Task SyncModpack()
        {
            if (!File.Exists(_manifestPath))
                throw new FileNotFoundException("No se encontrÃ³ el archivo manifest.json localmente impidiendo la sincronizaciÃ³n.");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(_manifestPath), options);

            if (manifest?.Files != null && manifest.Files.Any())
            {
                await SyncManifestFiles(manifest.Files);
            }
        }

        #endregion

        #region LÃ³gica Predictiva (Smart Sync)

        /// <summary>
        /// Compara el estado local actual (existencia, Hashes y polÃ­tica de actualizaciÃ³n) 
        /// y define la ruta Ã³ptima de descargas necesarias.
        /// </summary>
        private async Task SyncManifestFiles(List<ManifestFile> manifestFiles)
        {
            // 1. Cargar el Ãºltimo estado validado de sincronizaciÃ³n
            var localSyncState = ReadSyncState();
            var pendingDownloadQueue = new List<ManifestFile>();

            // 2. Crear cola de descargas determinando cuÃ¡les archivos tienen prioridad
            foreach (var file in manifestFiles)
            {
                if (string.IsNullOrEmpty(file.Path)) continue;

                string absolutePath = Path.Combine(_gameDir, file.Path);
                bool needsDownload = DetermineIfDownloadNeeded(file, absolutePath, localSyncState);

                if (needsDownload)
                    pendingDownloadQueue.Add(file);
            }

            // 3. Ejecutar cola de descargas
            int totalPending = pendingDownloadQueue.Count;
            int completed = 0;

            foreach (var file in pendingDownloadQueue)
            {
                // ReSharper disable once PossibleNullReferenceException
                string absolutePath = Path.Combine(_gameDir, file.Path!);
                EnsureDirectoryExists(absolutePath);

                completed++;
                string fileName = Path.GetFileName(file.Path!);
                ProgressChanged?.Invoke(completed, totalPending, fileName);

                if (file.AssetId.HasValue)
                    await DownloadAssetFromRelease(file.AssetId.Value, absolutePath);
                else
                    await DownloadFileTreeFromApi(file.Path!, absolutePath);
            }

            // 4. Inmortalizar el estado, de tal forma que los updates "default" ya no se repitan
            PersistNewSyncState(manifestFiles);

            // 5. Cleanup: Remover mods de terceras partes no declarados en el manifest
            // CleanGhostMods(manifestFiles);
        }

        private bool DetermineIfDownloadNeeded(ManifestFile manifestNode, string localPath, Dictionary<string, string> localSyncState)
        {
            if (!File.Exists(localPath) && TryGetDisabledOptionalModPath(manifestNode.Path, out string? disabledPath) && File.Exists(disabledPath))
            {
                return CalculateSha256(disabledPath) != manifestNode.Hash;
            }

            if (!File.Exists(localPath)) 
                return true;

            // 'forced' implica que pase lo que pase si se modifica un archivo que no deba, el launcher lo revertirÃ¡ 
            if (manifestNode.Type == "forced")
            {
                return CalculateSha256(localPath) != manifestNode.Hash;
            }

            // 'default' implica ignorar los cambios que haga el usuario e instalarlos 1 sola vez en base al tracking JSON.
            if (manifestNode.Type == "default")
            {
                localSyncState.TryGetValue(manifestNode.Path!, out string? lastSessionHash);
                return lastSessionHash != manifestNode.Hash;
            }

            return false;
        }

        private bool TryGetDisabledOptionalModPath(string? manifestRelativePath, out string? disabledPath)
        {
            disabledPath = null;

            if (string.IsNullOrWhiteSpace(manifestRelativePath))
            {
                return false;
            }

            string normalized = manifestRelativePath.Replace('/', Path.DirectorySeparatorChar);
            string modsPrefix = "mods" + Path.DirectorySeparatorChar;
            if (!normalized.StartsWith(modsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            disabledPath = Path.Combine(AppConstants.DisabledOptionalModsDir, fileName);
            return true;
        }

        #endregion

        #region Operaciones de Red con GitHub

        /// <summary>
        /// Recupera un archivo "raw" en string base consultando su path relativo al Repositorio.
        /// </summary>
        private async Task DownloadFileTreeFromApi(string repoRelativePath, string diskDestinationPath)
        {
            string normalizedPath = repoRelativePath.Replace("\\", "/");
            string url = $"https://raw.githubusercontent.com/{AppConstants.GitHubOwner}/{AppConstants.ModpackRepoName}/{AppConstants.GitHubBranch}/{normalizedPath}";

            using var client = CreateLongDownloadHttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JurassicCraftLauncher");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Fallo localizando archivo '{repoRelativePath}'. Raw GitHub retorno: {response.StatusCode}");

            await using var fs = new FileStream(diskDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);
        }

        /// <summary>
        /// Al enfrentarse a binarios grandes que GitHub rechaza por API standard,
        /// navega los Assets adjuntados a una release pÃºblica.
        /// </summary>
        private async Task DownloadAssetFromRelease(long assetId, string diskDestinationPath)
        {
            string fileName = Path.GetFileName(diskDestinationPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException($"No se pudo resolver el nombre de archivo para el asset {assetId}.");
            }

            string publicReleaseUrl = $"https://github.com/{AppConstants.GitHubOwner}/{AppConstants.ModpackRepoName}/releases/download/{AppConstants.ModpackReleaseTag}/{Uri.EscapeDataString(fileName)}";
            using var publicClient = CreateLongDownloadHttpClient();
            publicClient.DefaultRequestHeaders.Add("User-Agent", "JurassicCraftLauncher");
            using var publicResponse = await publicClient.GetAsync(publicReleaseUrl, HttpCompletionOption.ResponseHeadersRead);

            if (publicResponse.IsSuccessStatusCode)
            {
                await using var publicFs = new FileStream(diskDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await publicResponse.Content.CopyToAsync(publicFs);
                return;
            }

            string url = $"https://api.github.com/repos/{AppConstants.GitHubOwner}/{AppConstants.ModpackRepoName}/releases/assets/{assetId}";

            // Fallback por API solo si la URL pública directa no respondió bien.
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = CreateLongDownloadHttpClient(handler);
            ApplyGitHubHeaders(client, "JurassicCraftLauncher");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            HttpResponseMessage finalResponse = response;

            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 && response.Headers.Location != null)
            {
                var cdnUrl = response.Headers.Location.ToString();
                using var cdnRequest = new HttpRequestMessage(HttpMethod.Get, cdnUrl);
                cdnRequest.Headers.Add("User-Agent", "JurassicCraftLauncher");
                finalResponse = await client.SendAsync(cdnRequest, HttpCompletionOption.ResponseHeadersRead);
            }

            if (!finalResponse.IsSuccessStatusCode)
                throw new HttpRequestException($"La descarga del Asset N° {assetId} falló. URL pública: {publicResponse.StatusCode}. Fallback API/CDN: {finalResponse.StatusCode}");

            await using var fs = new FileStream(diskDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await finalResponse.Content.CopyToAsync(fs);
        }

        private HttpClient CreateGithubHttpClient()
        {
            return CreateGithubHttpClient("JurassicCraftLauncher");
        }

        private HttpClient CreateGithubHttpClient(string userAgent)
        {
            var client = CreateLongDownloadHttpClient();
            ApplyGitHubHeaders(client, userAgent);
            return client;
        }

        private HttpClient CreateLongDownloadHttpClient(HttpMessageHandler? handler = null)
        {
            var client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = LargeDownloadTimeout;
            return client;
        }

        private void ApplyGitHubHeaders(HttpClient client, string userAgent)
        {
            if (!string.IsNullOrWhiteSpace(AppConstants.GitHubToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"token {AppConstants.GitHubToken}");
            }

            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        }

        #endregion

        #region Herramientas de Mantenimiento / Archivos

        private Dictionary<string, string> ReadSyncState()
        {
            if (!File.Exists(_syncStatePath)) return new Dictionary<string, string>();
            
            try 
            { 
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_syncStatePath)) ?? new(); 
            }
            catch 
            { 
                return new Dictionary<string, string>(); 
            }
        }

        private void PersistNewSyncState(List<ManifestFile> allManifestFiles)
        {
            var dict = new Dictionary<string, string>();
            foreach (var file in allManifestFiles.Where(f => f.Type == "default" && !string.IsNullOrEmpty(f.Path)))
            {
                dict[file.Path!] = file.Hash ?? string.Empty;
            }

            try 
            { 
                 File.WriteAllText(_syncStatePath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true })); 
            } 
            catch { /* Tolerable failure */ }
        }

        /// <summary>
        /// Borra cualqueir fichero que figure en la carpeta Mods (Local) pero que no aparezca en el JSON Central.
        /// Mantiene a raya corrupciones e instalaciones dobles accidentales.
        /// </summary>
        private void CleanGhostMods(List<ManifestFile> manifestFiles)
        {
            var allowedPaths = new HashSet<string>(
                manifestFiles.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p))!
            );

            var localFilesTree = Directory.GetFiles(_gameDir, "*.*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_gameDir, f).Replace("\\", "/"));

            foreach (var relativeLocalPath in localFilesTree)
            {
                // Solo se restringe a carpeta Mods (Para no borrar capturas o saves)
                if (relativeLocalPath.StartsWith("mods/") && !allowedPaths.Contains(relativeLocalPath))
                {
                    try { File.Delete(Path.Combine(_gameDir, relativeLocalPath)); } catch { /* Ignorado por concurrencia u otros de SO */ }
                }
            }
        }

        private string CalculateSha256(string absolutePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(absolutePath);
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private void EnsureDirectoryExists(string absoluteFilePath)
        {
            string? dirName = Path.GetDirectoryName(absoluteFilePath);
            if (!string.IsNullOrEmpty(dirName)) Directory.CreateDirectory(dirName);
        }

        #endregion
    }
}
