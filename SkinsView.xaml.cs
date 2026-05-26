using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.Win32;

namespace JurassicCraftLauncher
{
    /// <summary>
    /// B??squeda dual en PARALELO:
    ///   Fuente 1 ??? Mojang API   ??? usuario exacto, aparece primero con borde dorado
    ///   Fuente 2 ??? MSkins ??? hasta 24 skins por keyword
    ///
    /// DEBUG: revisa la salida de Console para ver el HTML recibido y qu?? regex matche??.
    /// </summary>
    public partial class SkinsView : UserControl
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private System.Threading.CancellationTokenSource? _searchCts;
        private const int InitialVisibleWebResults = 32;
        private const int VisibleWebResultsStep = 32;
        private const int MaxBufferedWebResults = 512;
        private const int MaxParallelDownloads = 6;
        private const int MaxMskinsPages = 4;
        private readonly Queue<QueuedSkinResult> _pendingWebResults = new Queue<QueuedSkinResult>();
        private readonly HashSet<string> _queuedWebKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _webCards = new Dictionary<string, Border>(StringComparer.Ordinal);
        private int _displayedWebCount;
        private int _visibleWebLimit = InitialVisibleWebResults;

        // ?????? Cambia a true para imprimir el HTML crudo en la consola ??????????????????????????????????????????????????????
        private static readonly bool DEBUG_HTML = false;

        public SkinsView()
        {
            InitializeComponent();
            if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
                _http.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/124.0.0.0 Safari/537.36");

            this.Loaded += (s, e) => LoadInitialSkin();
            GalleryScroll.ScrollChanged += GalleryScroll_ScrollChanged;
        }

        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        //  CARGA INICIAL
        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        private void LoadInitialSkin()
        {
            try
            {
                if (!File.Exists(SkinPersistenceService.CustomSkinFile))
                    SkinPersistenceService.RestoreCustomSkinFromGlobal();

                string f = SkinPersistenceService.CustomSkinFile;
                if (File.Exists(f)) UpdatePreview(f);
            }
            catch { }
        }

        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        //  EVENTOS UI
        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtSearchPlaceholder.Visibility =
                string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = SearchSkinsAsync(TxtSearch.Text.Trim());
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            string q = TxtSearch.Text.Trim();
            if (!string.IsNullOrEmpty(q)) _ = SearchSkinsAsync(q);
        }

        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        //  B??SQUEDA PRINCIPAL
        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        private async Task SearchSkinsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            var ct = _searchCts.Token;

            ResetSearchResultsState();
            SetGalleryState(GalleryState.Loading, string.Format(AppTexts.SkinsLoadingSearchFormat, query));

            // Lanzar todas las fuentes en paralelo
            var mojangTask  = FetchMojangSkinAsync(query, ct);
            var mskinsTask = FetchMSkinsAsync(query, ct, skin => PublishSkinResult(skin, isMojang: false, ct));
            var skinMcTask = FetchSkinMcAsync(query, ct, skin => PublishSkinResult(skin, isMojang: false, ct));
            var mcSkinsTask = FetchMcSkinsAsync(query, ct, skin => PublishSkinResult(skin, isMojang: false, ct));

            // Mojang es m??s r??pido ??? mostrar primero
            SkinResult? mojangResult = null;
            try { mojangResult = await mojangTask; } catch { }

            if (ct.IsCancellationRequested) return;

            if (mojangResult != null)
                PublishSkinResult(mojangResult, isMojang: true, ct);

            try { await Task.WhenAll(mskinsTask, skinMcTask, mcSkinsTask); } catch { }

            if (ct.IsCancellationRequested) return;

            if (mojangResult == null && _displayedWebCount == 0 && _pendingWebResults.Count == 0)
            {
                SetGalleryState(GalleryState.Error, string.Format(AppTexts.SkinsErrorNoResultsFormat, query));
                return;
            }
        }

        private void ResetSearchResultsState()
        {
            _pendingWebResults.Clear();
            _queuedWebKeys.Clear();
            _webCards.Clear();
            _displayedWebCount = 0;
            _visibleWebLimit = InitialVisibleWebResults;

            Dispatcher.Invoke(() =>
            {
                SkinsGallery.Children.Clear();
                GalleryScroll.ScrollToVerticalOffset(0);
            });
        }

        private void PublishSkinResult(
            SkinResult skin, bool isMojang, System.Threading.CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            string key = NormalizeSearchText(skin.Label);
            Dispatcher.Invoke(() =>
            {
                if (ct.IsCancellationRequested) return;

                if (isMojang)
                {
                    SetGalleryState(GalleryState.Results);
                    AddCardToGallery(skin, isMojang: false);
                    return;
                }

                if (_webCards.ContainsKey(key) || _queuedWebKeys.Contains(key))
                    return;

                _pendingWebResults.Enqueue(new QueuedSkinResult { Key = key, Skin = skin });
                _queuedWebKeys.Add(key);
                FlushPendingWebResults();
            });
        }

        private void FlushPendingWebResults()
        {
            while (_displayedWebCount < _visibleWebLimit && _pendingWebResults.Count > 0)
            {
                var pending = _pendingWebResults.Dequeue();
                _queuedWebKeys.Remove(pending.Key);

                if (_webCards.ContainsKey(pending.Key))
                    continue;

                SetGalleryState(GalleryState.Results);
                Border card = AddCardToGallery(pending.Skin, isMojang: false);
                _webCards[pending.Key] = card;
                _displayedWebCount++;
            }
        }

        private void GalleryScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (GalleryScroll.Visibility != Visibility.Visible) return;
            if (e.ExtentHeightChange != 0) return;
            if (e.VerticalChange <= 0) return;
            if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 48) return;

            _visibleWebLimit += VisibleWebResultsStep;
            FlushPendingWebResults();
        }

        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        //  FUENTE 1 ??? Mojang API
        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        private async Task<SkinResult?> FetchMojangSkinAsync(
            string username, System.Threading.CancellationToken ct)
        {
            try
            {
                string profileJson = await _http.GetStringAsync(
                    $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(username)}", ct);

                string? uuid       = ExtractJsonValue(profileJson, "id");
                string? playerName = ExtractJsonValue(profileJson, "name");
                if (string.IsNullOrEmpty(uuid)) return null;

                string sessionJson = await _http.GetStringAsync(
                    $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}", ct);

                string? textureValue = ExtractJsonValue(sessionJson, "value");
                if (string.IsNullOrEmpty(textureValue)) return null;

                string textureJson = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(textureValue));

                string? skinUrl = ExtractSkinUrl(textureJson);
                if (string.IsNullOrEmpty(skinUrl)) return null;

                byte[] bytes  = await _http.GetByteArrayAsync(skinUrl, ct);
                var    bitmap = BytesToBitmapImage(bytes);

                return new SkinResult { Label = playerName ?? username, Bytes = bytes, Bitmap = bitmap };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Mojang] Error: {ex.Message}");
                return null;
            }
        }

        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        //  FUENTE 2 ??? MSkins (busqueda paginada con links estables)
        // ?????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????
        private async Task FetchMSkinsAsync(
            string keyword, System.Threading.CancellationToken ct, Action<SkinResult> onResult)
        {
            try
            {
                var entries = await FetchMSkinsEntriesAsync(keyword, ct);

                if (DEBUG_HTML)
                    Console.WriteLine($"[DEBUG] Parsed {entries.Count} skin entries.");

                if (entries.Count == 0) return;

                await DownloadSkinBatchAsync(entries, ct, onResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MSkins] Error: {ex.Message}");
            }
        }

        private async Task FetchSkinMcAsync(
            string keyword, System.Threading.CancellationToken ct, Action<SkinResult> onResult)
        {
            try
            {
                var entries = await FetchSkinMcEntriesAsync(keyword, ct);

                if (DEBUG_HTML)
                    Console.WriteLine($"[DEBUG] Parsed {entries.Count} SkinMC entries.");

                if (entries.Count == 0) return;

                await DownloadSkinBatchAsync(entries, ct, onResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SkinMC] Error: {ex.Message}");
            }
        }

        private async Task FetchMcSkinsAsync(
            string keyword, System.Threading.CancellationToken ct, Action<SkinResult> onResult)
        {
            try
            {
                var entries = await FetchMcSkinsEntriesAsync(keyword, ct);

                if (DEBUG_HTML)
                    Console.WriteLine($"[DEBUG] Parsed {entries.Count} mcskins.top entries.");

                if (entries.Count == 0) return;

                await DownloadSkinBatchAsync(entries, ct, onResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[mcskins.top] Error: {ex.Message}");
            }
        }

        private async Task<List<SkinEntry>> FetchMSkinsEntriesAsync(
            string keyword, System.Threading.CancellationToken ct)
        {
            var merged = new List<SkinEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string variant in BuildSearchVariants(keyword, maxVariants: 3))
            {
                for (int page = 1; page <= MaxMskinsPages; page++)
                {
                    if (ct.IsCancellationRequested || merged.Count >= MaxBufferedWebResults) break;

                    string url = page == 1
                        ? $"https://mskins.net/en/search?q={Uri.EscapeDataString(variant)}"
                        : $"https://mskins.net/en/search?q={Uri.EscapeDataString(variant)}&page={page}";

                    try
                    {
                        string html = await _http.GetStringAsync(url, ct);
                        if (DEBUG_HTML)
                        {
                            Console.WriteLine($"\n[DEBUG MSkins] {url}\n");
                            Console.WriteLine(html.Length > 3000 ? html.Substring(0, 3000) : html);
                            Console.WriteLine("\n[DEBUG END]\n");
                        }

                        var pageEntries = ParseMSkinsEntries(html, keyword);
                        foreach (var entry in pageEntries)
                        {
                            if (!seen.Add(entry.Id)) continue;
                            merged.Add(entry);
                            if (merged.Count >= MaxBufferedWebResults) return merged;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (DEBUG_HTML)
                            Console.WriteLine($"[MSkins] {url} -> {ex.Message}");
                    }
                }
            }

            return merged;
        }

        private List<SkinEntry> ParseMSkinsEntries(string html, string keyword)
        {
            var entries = new List<SkinEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var resultPattern = new Regex(
                @"<a\s+class=""skin_link""\s+href=""(https://mskins\.net/en/skin/([^""#?]+))""",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in resultPattern.Matches(html))
            {
                string pageUrl = m.Groups[1].Value;
                string slug = m.Groups[2].Value;
                string name = CleanupSkinNameFromMskinsSlug(slug);

                TryAddSkinEntry(entries, seen, keyword, slug, name, pageUrl, $"{pageUrl}/download");
                if (entries.Count >= MaxBufferedWebResults) break;
            }

            if (DEBUG_HTML)
                Console.WriteLine($"[DEBUG] MSkins matched {entries.Count} skins.");
            return entries;
        }

        private async Task<List<SkinEntry>> FetchSkinMcEntriesAsync(
            string keyword, System.Threading.CancellationToken ct)
        {
            var merged = new List<SkinEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string variant in BuildSearchVariants(keyword, maxVariants: 5))
            {
                if (ct.IsCancellationRequested || merged.Count >= MaxBufferedWebResults) break;

                foreach (string url in BuildSkinMcSearchUrls(variant))
                {
                    if (ct.IsCancellationRequested || merged.Count >= MaxBufferedWebResults) break;

                    try
                    {
                        string html = await _http.GetStringAsync(url, ct);
                        if (DEBUG_HTML)
                        {
                            Console.WriteLine($"\n[DEBUG SkinMC] {url}\n");
                            Console.WriteLine(html.Length > 3000 ? html.Substring(0, 3000) : html);
                            Console.WriteLine("\n[DEBUG END]\n");
                        }

                        var pageEntries = ParseSkinMcEntries(html, keyword, variant);
                        foreach (var entry in pageEntries)
                        {
                            if (!seen.Add(entry.Id)) continue;
                            merged.Add(entry);
                            if (merged.Count >= MaxBufferedWebResults) return merged;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (DEBUG_HTML)
                            Console.WriteLine($"[SkinMC] {url} -> {ex.Message}");
                    }
                }
            }

            return merged;
        }

        private async Task<List<SkinEntry>> FetchMcSkinsEntriesAsync(
            string keyword, System.Threading.CancellationToken ct)
        {
            var merged = new List<SkinEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string variant in BuildSearchVariants(keyword, maxVariants: 4))
            {
                if (ct.IsCancellationRequested || merged.Count >= MaxBufferedWebResults) break;

                foreach (string url in BuildMcSkinsSearchUrls(variant))
                {
                    if (ct.IsCancellationRequested || merged.Count >= MaxBufferedWebResults) break;

                    try
                    {
                        string html = await _http.GetStringAsync(url, ct);
                        if (DEBUG_HTML)
                        {
                            Console.WriteLine($"\n[DEBUG mcskins.top] {url}\n");
                            Console.WriteLine(html.Length > 3000 ? html.Substring(0, 3000) : html);
                            Console.WriteLine("\n[DEBUG END]\n");
                        }

                        var pageEntries = ParseMcSkinsEntries(html, keyword);
                        foreach (var entry in pageEntries)
                        {
                            if (!seen.Add(entry.Id)) continue;
                            merged.Add(entry);
                            if (merged.Count >= MaxBufferedWebResults) return merged;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (DEBUG_HTML)
                            Console.WriteLine($"[mcskins.top] {url} -> {ex.Message}");
                    }
                }
            }

            return merged;
        }

        private List<SkinEntry> ParseSkinMcEntries(string html, string keyword, string matchedVariant)
        {
            var entries = new List<SkinEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string baseLabel = HumanizeSearchLabel(matchedVariant);

            var resultPattern = new Regex(
                @"/skins/([0-9a-f\-]{36})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in resultPattern.Matches(html))
            {
                string id = m.Groups[1].Value;
                if (!seen.Add(id)) continue;

                entries.Add(new SkinEntry
                {
                    Id = id,
                    Name = $"{baseLabel} #{entries.Count + 1}",
                    PageUrl = $"https://skinmc.net/skins/{id}",
                    DownloadUrl = $"https://skinmc.net/api/v1/renders/skins/{id}/skin"
                });

                if (entries.Count >= MaxBufferedWebResults) break;
            }

            if (DEBUG_HTML)
                Console.WriteLine($"[DEBUG] SkinMC matched {entries.Count} skins.");
            return entries;
        }

        private List<SkinEntry> ParseMcSkinsEntries(string html, string keyword)
        {
            var entries = new List<SkinEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var resultPattern = new Regex(
                @"<div\s+class=""search-res-block"">.*?<a\s+href=""(/skin/([^""/#?]+))""[^>]*class=""searchprev"".*?<img[^>]*alt=""([^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in resultPattern.Matches(html))
            {
                string relativePath = m.Groups[1].Value;
                string slug = m.Groups[2].Value;
                string name = CleanupSkinTitle(m.Groups[3].Value, slug);

                TryAddSkinEntry(
                    entries,
                    seen,
                    keyword,
                    slug,
                    name,
                    "https://mcskins.top" + relativePath,
                    "https://mcskins.top" + relativePath,
                    needsDetailPage: true);

                if (entries.Count >= MaxBufferedWebResults) break;
            }

            if (DEBUG_HTML)
                Console.WriteLine($"[DEBUG] mcskins.top matched {entries.Count} skins.");
            return entries;
        }

        private List<string> BuildSearchVariants(string keyword, int maxVariants = 8)
        {
            var variants = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddVariant(string value)
            {
                string normalized = NormalizeSearchText(value);
                if (string.IsNullOrWhiteSpace(normalized)) return;
                if (seen.Add(normalized))
                    variants.Add(normalized);
            }

            AddVariant(keyword);

            var tokens = NormalizeSearchText(keyword)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0) return variants;

            string joined = string.Concat(tokens);
            AddVariant(joined);
            AddVariant(string.Join("-", tokens));
            AddVariant(string.Join("_", tokens));

            if (tokens.Length > 1)
            {
                foreach (string token in tokens)
                    AddVariant(token);

                for (int i = 0; i < tokens.Length - 1; i++)
                {
                    string left = tokens[i];
                    string right = tokens[i + 1];
                    AddVariant(left + right);
                    AddVariant(left + "-" + right);
                    AddVariant(left + "_" + right);
                }
            }

            if (variants.Count > maxVariants)
                variants = variants.Take(maxVariants).ToList();

            return variants;
        }

        private IEnumerable<string> BuildSkinMcSearchUrls(string variant)
        {
            string normalized = NormalizeSearchText(variant);
            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            string tagSlug = normalized.Replace(" ", "-");
            yield return $"https://skinmc.net/skins/tagged/{Uri.EscapeDataString(tagSlug)}";
            yield return $"https://skinmc.net/s?search={Uri.EscapeDataString(variant)}";
        }

        private IEnumerable<string> BuildMcSkinsSearchUrls(string variant)
        {
            yield return $"https://mcskins.top/search?s={Uri.EscapeDataString(variant)}&opt=1";
            yield return $"https://mcskins.top/search?s={Uri.EscapeDataString(variant)}&opt=3";
        }

        private async Task DownloadSkinBatchAsync(
            List<SkinEntry> entries, System.Threading.CancellationToken ct, Action<SkinResult> onResult)
        {
            int limit = Math.Min(entries.Count, MaxBufferedWebResults);
            var activeTasks = new List<Task<SkinResult?>>(MaxParallelDownloads);
            int nextIndex = 0;

            while (!ct.IsCancellationRequested && (nextIndex < limit || activeTasks.Count > 0))
            {
                while (nextIndex < limit && activeTasks.Count < MaxParallelDownloads)
                    activeTasks.Add(DownloadSkinAsync(entries[nextIndex++], ct));

                if (activeTasks.Count == 0) break;

                Task<SkinResult?> finishedTask = await Task.WhenAny(activeTasks);
                activeTasks.Remove(finishedTask);

                var skin = await finishedTask;
                if (skin != null)
                    onResult(skin);
            }
        }

        private void TryAddSkinEntry(
            List<SkinEntry> entries,
            HashSet<string> seen,
            string keyword,
            string slug,
            string name,
            string pageUrl,
            string downloadUrl,
            bool needsDetailPage = false)
        {
            if (!seen.Add(slug)) return;
            if (!MatchesKeyword(keyword, slug, name)) return;

            int score = ScoreSearchRelevance(keyword, slug, name);
            if (score < 40) return;

            entries.Add(new SkinEntry
            {
                Id = slug,
                Name = name,
                PageUrl = pageUrl,
                DownloadUrl = downloadUrl,
                NeedsDetailPage = needsDetailPage
            });
        }

        private async Task<SkinResult?> DownloadSkinAsync(
            SkinEntry entry, System.Threading.CancellationToken ct)
        {
            try
            {
                string downloadUrl = entry.DownloadUrl;
                if (entry.NeedsDetailPage)
                {
                    string? resolvedUrl = await ResolveDetailDownloadUrlAsync(entry, ct);
                    if (string.IsNullOrWhiteSpace(resolvedUrl))
                    {
                        Console.WriteLine($"[DEBUG] Skin {entry.Id} detail page had no downloadable image.");
                        return null;
                    }

                    downloadUrl = resolvedUrl;
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                req.Headers.Referrer = new Uri(entry.PageUrl);
                req.Headers.TryAddWithoutValidation("Accept", "image/png,image/*;q=0.9,*/*;q=0.1");

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 100)
                {
                    Console.WriteLine($"[DEBUG] Skin {entry.Id} returned {bytes.Length} bytes (too small, skipping).");
                    return null;
                }

                var bitmap = BytesToBitmapImage(bytes);
                return new SkinResult { Label = entry.Name, Bytes = bytes, Bitmap = bitmap };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Skin {entry.Id} download failed: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> ResolveDetailDownloadUrlAsync(
            SkinEntry entry, System.Threading.CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, entry.PageUrl);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                string html = await resp.Content.ReadAsStringAsync(ct);
                if (DEBUG_HTML)
                {
                    Console.WriteLine($"\n[DEBUG Detail] {entry.PageUrl}\n");
                    Console.WriteLine(html.Length > 3000 ? html.Substring(0, 3000) : html);
                    Console.WriteLine("\n[DEBUG END]\n");
                }

                string[] patterns =
                {
                    @"name=""skin_image_data""\s+value=""(/assets/images/skin/[^""]+\.png(?:\?[^""]*)?)""",
                    @"href=""(/assets/snippets/download/skin\.php\?n=\d+)""",
                    @"href=""(https://www\.minecraftskins\.com/uploads/skins/[^""]+\.png)""",
                    @"src=""(https://www\.minecraftskins\.com/uploads/skins/[^""]+\.png)""",
                    @"href=""(/uploads/skins/[^""]+\.png)""",
                    @"src=""(/uploads/skins/[^""]+\.png)"""
                };

                foreach (string pattern in patterns)
                {
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (!match.Success) continue;

                    string candidate = match.Groups[1].Value;
                    if (candidate.StartsWith("/"))
                        candidate = entry.PageUrl.Contains("mcskins.top", StringComparison.OrdinalIgnoreCase)
                            ? "https://mcskins.top" + candidate
                            : "https://www.minecraftskins.com" + candidate;
                    return candidate;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Skin {entry.Id} detail lookup failed: {ex.Message}");
                return null;
            }
        }

        private class SkinEntry
        {
            public string Id          { get; set; } = "";
            public string Name        { get; set; } = "";
            public string PageUrl     { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public bool NeedsDetailPage { get; set; }
        }

        private class SkinResult
        {
            public string      Label  { get; set; } = "";
            public byte[]      Bytes  { get; set; } = Array.Empty<byte>();
            public BitmapImage Bitmap { get; set; } = null!;
        }

        private class QueuedSkinResult
        {
            public string Key { get; set; } = "";
            public SkinResult Skin { get; set; } = null!;
        }

        private string CleanupSkinTitle(string rawTitle, string fallbackSlug)
        {
            string withoutTags = Regex.Replace(rawTitle, "<.*?>", " ");
            string normalized = System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return string.IsNullOrWhiteSpace(normalized) ? HumanizeSkinSlug(fallbackSlug) : TrimSkinLabel(normalized);
        }

        private string HumanizeSkinSlug(string slug)
        {
            string textValue = slug.Replace("-", " ").Replace("_", " ").Trim('/');
            textValue = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(textValue.ToLowerInvariant());
            return TrimSkinLabel(textValue);
        }

        private string CleanupSkinNameFromMskinsSlug(string slug)
        {
            int dash = slug.LastIndexOf('-');
            string baseName = dash > 0 ? slug.Substring(0, dash) : slug;
            return TrimSkinLabel(baseName);
        }

        private string TrimSkinLabel(string value)
        {
            return value.Length > 20 ? value.Substring(0, 19) + "..." : value;
        }

        private string HumanizeSearchLabel(string value)
        {
            string textValue = NormalizeSearchText(value);
            if (string.IsNullOrWhiteSpace(textValue))
                return AppTexts.SkinsFallbackLabel;

            textValue = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(textValue);
            return TrimSkinLabel(textValue);
        }

        private bool MatchesKeyword(string keyword, string slug, string name)
        {
            var tokens = keyword.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return true;

            string haystack = NormalizeSearchText($"{slug} {name}");
            foreach (string token in tokens)
            {
                if (!haystack.Contains(NormalizeSearchText(token)))
                    return false;
            }
            return true;
        }

        private int ScoreSearchRelevance(string keyword, string slug, string name)
        {
            string query = NormalizeSearchText(keyword);
            string slugText = NormalizeSearchText(slug);
            string nameText = NormalizeSearchText(name);

            if (string.IsNullOrWhiteSpace(query)) return 0;
            if (nameText == query || slugText == query) return 100;
            if (nameText.StartsWith(query + " ", StringComparison.Ordinal) ||
                slugText.StartsWith(query + " ", StringComparison.Ordinal))
                return 90;
            if (nameText.StartsWith(query, StringComparison.Ordinal) ||
                slugText.StartsWith(query, StringComparison.Ordinal))
                return query.Length >= 6 ? 75 : 80;
            if (nameText.Contains(" " + query + " ", StringComparison.Ordinal) ||
                slugText.Contains(" " + query + " ", StringComparison.Ordinal))
                return 72;
            if (nameText.Contains(query, StringComparison.Ordinal) ||
                slugText.Contains(query, StringComparison.Ordinal))
                return query.Length >= 6 ? 55 : 65;
            return 0;
        }

        private string NormalizeSearchText(string value)
        {
            string textValue = value.ToLowerInvariant().Replace("-", " ").Replace("_", " ").Trim();
            return Regex.Replace(textValue, @"\s+", " ");
        }

        private Border AddCardToGallery(SkinResult skin, bool isMojang)
        {
            var preview  = RenderSkinCardPreview(skin.Bitmap);
            var card     = BuildSkinCard(skin.Label, preview, highlight: false);

            var cap = skin;
            card.MouseLeftButtonUp += (s, e) => _ = ApplyDownloadedSkinAsync(cap.Bytes, cap.Label);
            card.MouseEnter += (s, e) =>
                ((Border)s).Background = new SolidColorBrush(Color.FromArgb(30, 232, 184, 32));
            card.MouseLeave += (s, e) =>
                ((Border)s).Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));

            SkinsGallery.Children.Add(card);
            return card;
        }

        private Border BuildSkinCard(string label, BitmapSource face, bool highlight)
        {
            var card = new Border
            {
                Width           = 104,
                Height          = 162,
                Background      = new SolidColorBrush(
                    highlight ? Color.FromArgb(20, 232, 184, 32) : Color.FromArgb(16, 255, 255, 255)),
                BorderBrush     = highlight
                    ? new SolidColorBrush(Color.FromArgb(100, 232, 184, 32))
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = highlight ? new Thickness(1) : new Thickness(0),
                CornerRadius    = new CornerRadius(8),
                Margin          = new Thickness(0, 0, 8, 8),
                Cursor          = Cursors.Hand,
                ToolTip         = label
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var img = new Image
            {
                Source              = face,
                Width               = 72,
                Height              = 108,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 8, 0, 4)
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            stack.Children.Add(img);

            stack.Children.Add(new TextBlock
            {
                Text                = label.Length > 10 ? label.Substring(0, 9) + "…" : label,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily          = new FontFamily("Consolas"),
                FontSize            = 9,
                Foreground          = new SolidColorBrush(Color.FromRgb(216, 200, 160)),
                TextWrapping        = TextWrapping.NoWrap,
                Margin              = new Thickness(4, 0, 4, 0)
            });

            card.Child = stack;
            return card;
        }

        private BitmapSource RenderSkinCardPreview(BitmapSource skinTexture)
        {
            try
            {
                bool isSlim = AnalyzeSkinModel(skinTexture) == "slim";

                var viewport = new Viewport3D
                {
                    Width = 96,
                    Height = 138,
                    ClipToBounds = true
                };

                viewport.Camera = new PerspectiveCamera
                {
                    Position = new Point3D(16, 18, 42),
                    LookDirection = new Vector3D(-16, -1, -42),
                    UpDirection = new Vector3D(0, 1, 0),
                    FieldOfView = 30
                };

                viewport.Children.Add(new ModelVisual3D
                {
                    Content = new Model3DGroup
                    {
                        Children =
                        {
                            new AmbientLight(Color.FromRgb(150, 150, 150)),
                            new DirectionalLight(Colors.White, new Vector3D(-1, -2, -3)),
                            new DirectionalLight(Color.FromRgb(120, 120, 120), new Vector3D(1, 1, 0))
                        }
                    }
                });

                viewport.Children.Add(new ModelVisual3D
                {
                    Content = SkinModel3DBuilder.BuildModel(skinTexture, isSlim)
                });

                viewport.Measure(new Size(viewport.Width, viewport.Height));
                viewport.Arrange(new Rect(0, 0, viewport.Width, viewport.Height));
                viewport.UpdateLayout();

                var rtb = new RenderTargetBitmap(
                    (int)viewport.Width,
                    (int)viewport.Height,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                rtb.Render(viewport);
                rtb.Freeze();
                return rtb;
            }
            catch
            {
                return skinTexture is BitmapImage bitmapImage
                    ? CropSkinFace(bitmapImage)
                    : skinTexture;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  APLICAR SKIN
        // ═══════════════════════════════════════════════════════════════════════════════
        private async Task ApplyDownloadedSkinAsync(byte[] bytes, string name)
        {
            try
            {
                await SkinPersistenceService.SaveSkinBytesAsync(bytes);
                UpdatePreview(SkinPersistenceService.CustomSkinFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(AppTexts.SkinsApplyErrorFormat, name, ex.Message),
                    AppTexts.SkinsDialogErrorTitle);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  IMPORTAR DESDE ARCHIVO LOCAL
        // ═══════════════════════════════════════════════════════════════════════════════
        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = AppTexts.SkinsImportDialogTitle,
                Filter = AppTexts.SkinsImportDialogFilter,
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                await SkinPersistenceService.ImportSkinFromFileAsync(dlg.FileName);
                UpdatePreview(SkinPersistenceService.CustomSkinFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(AppTexts.SkinsImportErrorFormat, ex.Message),
                    AppTexts.SkinsDialogErrorTitle);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  DETECCIÓN DE MODELO
        // ═══════════════════════════════════════════════════════════════════════════════
        private string AnalyzeSkinModel(string filePath)
        {
            try
            {
                using var stream  = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var       decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return AnalyzeSkinModel(decoder.Frames[0]);
            }
            catch { }
            return "default";
        }

        private string AnalyzeSkinModel(BitmapSource frame)
        {
            try
            {
                if (frame.PixelWidth == 64 && frame.PixelHeight == 32) return "default";
                if (frame.PixelWidth == 64 && frame.PixelHeight == 64 &&
                    (CheckTransparency(frame, 54, 20) || CheckTransparency(frame, 42, 48)))
                    return "slim";
            }
            catch { }
            return "default";
        }

        private bool CheckTransparency(BitmapSource frame, int x, int y)
        {
            if (x < 0 || x >= frame.PixelWidth || y < 0 || y >= frame.PixelHeight) return false;
            var px = new byte[4];
            frame.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
            return px[3] == 0;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  PREVIEW 3D
        // ═══════════════════════════════════════════════════════════════════════════════
        private void UpdatePreview(string skinPath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource     = new Uri(skinPath);
                bmp.CacheOption   = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();

                bool isSlim = AnalyzeSkinModel(skinPath) == "slim";
                CharacterModelContainer.Content = SkinModel3DBuilder.BuildModel(bmp, isSlim);
                SkinViewport.Opacity            = 1;
                PlaceholderIcon.Visibility      = Visibility.Collapsed;
                PlaceholderOverlay.Visibility   = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                SkinViewport.Opacity          = 0;
                PlaceholderIcon.Visibility    = Visibility.Visible;
                PlaceholderOverlay.Visibility = Visibility.Visible;
                Console.WriteLine($"[Preview] Error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  UTILIDADES DE IMAGEN
        // ═══════════════════════════════════════════════════════════════════════════════
        private BitmapImage BytesToBitmapImage(byte[] bytes)
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private BitmapSource CropSkinFace(BitmapImage fullSkin)
        {
            try
            {
                double sx = fullSkin.PixelWidth  / 64.0;
                double sy = fullSkin.PixelHeight / 64.0;
                int x = (int)(8 * sx), y = (int)(8 * sy);
                int w = Math.Min((int)(8 * sx), fullSkin.PixelWidth  - x);
                int h = Math.Min((int)(8 * sy), fullSkin.PixelHeight - y);
                var cropped = new CroppedBitmap(fullSkin, new Int32Rect(x, y, w, h));
                cropped.Freeze();
                return cropped;
            }
            catch { return fullSkin; }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  ESTADO DE LA GALERÍA
        // ═══════════════════════════════════════════════════════════════════════════════
        private enum GalleryState { Loading, Results, Error, Empty }

        private void SetGalleryState(GalleryState state, string message = "")
        {
            Dispatcher.Invoke(() =>
            {
                LoadingPanel.Visibility  = Visibility.Collapsed;
                EmptyPanel.Visibility    = Visibility.Collapsed;
                ErrorPanel.Visibility    = Visibility.Collapsed;
                GalleryScroll.Visibility = Visibility.Collapsed;

                switch (state)
                {
                    case GalleryState.Loading:
                        TxtLoadingMsg.Text      = message;
                        LoadingPanel.Visibility = Visibility.Visible;
                        break;
                    case GalleryState.Results:
                        GalleryScroll.Visibility = Visibility.Visible;
                        break;
                    case GalleryState.Error:
                        TxtErrorMsg.Text      = message;
                        ErrorPanel.Visibility = Visibility.Visible;
                        break;
                    default:
                        EmptyPanel.Visibility = Visibility.Visible;
                        break;
                }
            });
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  PARSEO JSON MÍNIMO
        // ═══════════════════════════════════════════════════════════════════════════════
        private string? ExtractJsonValue(string json, string key)
        {
            string search = $"\"{key}\"";
            int ki = json.IndexOf(search, StringComparison.Ordinal);
            if (ki < 0) return null;

            int ci = json.IndexOf(':', ki + search.Length);
            if (ci < 0) return null;

            int s = ci + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t' || json[s] == '\n' || json[s] == '\r'))
                s++;
            if (s >= json.Length) return null;

            if (json[s] == '"')
            {
                int e = json.IndexOf('"', s + 1);
                return e < 0 ? null : json.Substring(s + 1, e - s - 1);
            }

            int en = s;
            while (en < json.Length && json[en] != ',' && json[en] != '}' && json[en] != ']') en++;
            return json.Substring(s, en - s).Trim();
        }

        private string? ExtractSkinUrl(string textureJson)
        {
            int idx = textureJson.IndexOf("\"SKIN\"", StringComparison.Ordinal);
            if (idx < 0) return null;
            return ExtractJsonValue(textureJson.Substring(idx), "url");
        }
    }
}
