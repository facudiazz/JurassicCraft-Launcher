using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace JurassicCraftLauncher
{
    public static class SkinPersistenceService
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        static SkinPersistenceService()
        {
            if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
                _http.DefaultRequestHeaders.Add("User-Agent", "JurassicCraftLauncher");
        }

        public static string GlobalSkinFile => Path.Combine(AppConstants.AppDataDir, "skin.png");
        public static string GlobalModelFile => Path.Combine(AppConstants.AppDataDir, "model.txt");
        public static string CustomSkinsDir => Path.Combine(AppConstants.GameDir, "customskins");
        public static string CustomSkinFile => Path.Combine(CustomSkinsDir, "skin.png");
        public static string CustomModelFile => Path.Combine(CustomSkinsDir, "model.txt");

        public static bool HasGlobalSkin() => File.Exists(GlobalSkinFile);
        public static bool HasCustomSkin() => File.Exists(CustomSkinFile);

        public static async Task<bool> TryDownloadPremiumSkinForUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            try
            {
                using var profileResponse = await _http.GetAsync(
                    $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(username)}");

                if (!profileResponse.IsSuccessStatusCode)
                    return false;

                using var profileDocument = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync());
                if (!profileDocument.RootElement.TryGetProperty("id", out JsonElement idElement))
                    return false;

                string? uuid = idElement.GetString();
                if (string.IsNullOrWhiteSpace(uuid))
                    return false;

                using var sessionResponse = await _http.GetAsync(
                    $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}");

                if (!sessionResponse.IsSuccessStatusCode)
                    return false;

                using var sessionDocument = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
                if (!sessionDocument.RootElement.TryGetProperty("properties", out JsonElement propertiesElement) ||
                    propertiesElement.ValueKind != JsonValueKind.Array)
                    return false;

                string? texturePayload = null;
                foreach (JsonElement property in propertiesElement.EnumerateArray())
                {
                    if (!property.TryGetProperty("name", out JsonElement propertyName) ||
                        !string.Equals(propertyName.GetString(), "textures", StringComparison.Ordinal))
                        continue;

                    if (property.TryGetProperty("value", out JsonElement propertyValue))
                    {
                        texturePayload = propertyValue.GetString();
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(texturePayload))
                    return false;

                string decodedTextureJson = Encoding.UTF8.GetString(Convert.FromBase64String(texturePayload));
                using var textureDocument = JsonDocument.Parse(decodedTextureJson);

                if (!textureDocument.RootElement.TryGetProperty("textures", out JsonElement texturesElement) ||
                    !texturesElement.TryGetProperty("SKIN", out JsonElement skinElement) ||
                    !skinElement.TryGetProperty("url", out JsonElement urlElement))
                    return false;

                string? skinUrl = urlElement.GetString();
                if (string.IsNullOrWhiteSpace(skinUrl))
                    return false;

                string model = "default";
                if (skinElement.TryGetProperty("metadata", out JsonElement metadataElement) &&
                    metadataElement.TryGetProperty("model", out JsonElement modelElement) &&
                    string.Equals(modelElement.GetString(), "slim", StringComparison.OrdinalIgnoreCase))
                {
                    model = "slim";
                }

                byte[] bytes = await _http.GetByteArrayAsync(skinUrl);
                await SaveSkinBytesAsync(bytes, model);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task SaveSkinBytesAsync(byte[] skinBytes, string? modelOverride = null)
        {
            EnsureManagedDirectories();

            await File.WriteAllBytesAsync(CustomSkinFile, skinBytes);
            string model = modelOverride ?? DetectSkinModel(CustomSkinFile);
            await File.WriteAllTextAsync(CustomModelFile, model);

            await File.WriteAllBytesAsync(GlobalSkinFile, skinBytes);
            await File.WriteAllTextAsync(GlobalModelFile, model);
        }

        public static async Task ImportSkinFromFileAsync(string sourceSkinPath)
        {
            byte[] bytes = await File.ReadAllBytesAsync(sourceSkinPath);
            string model = DetectSkinModel(sourceSkinPath);
            await SaveSkinBytesAsync(bytes, model);
        }

        public static bool EnsureGlobalCopyFromCustom()
        {
            try
            {
                if (!File.Exists(CustomSkinFile))
                    return false;

                Directory.CreateDirectory(AppConstants.AppDataDir);
                File.Copy(CustomSkinFile, GlobalSkinFile, true);

                string model = File.Exists(CustomModelFile)
                    ? File.ReadAllText(CustomModelFile)
                    : DetectSkinModel(CustomSkinFile);

                File.WriteAllText(GlobalModelFile, model);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool RestoreCustomSkinFromGlobal()
        {
            try
            {
                if (!File.Exists(GlobalSkinFile))
                    return false;

                Directory.CreateDirectory(CustomSkinsDir);
                File.Copy(GlobalSkinFile, CustomSkinFile, true);

                string model = File.Exists(GlobalModelFile)
                    ? File.ReadAllText(GlobalModelFile)
                    : DetectSkinModel(GlobalSkinFile);

                File.WriteAllText(CustomModelFile, model);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureManagedDirectories()
        {
            Directory.CreateDirectory(AppConstants.AppDataDir);
            Directory.CreateDirectory(CustomSkinsDir);
        }

        private static string DetectSkinModel(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return DetectSkinModel(decoder.Frames[0]);
            }
            catch
            {
                return "default";
            }
        }

        private static string DetectSkinModel(BitmapSource frame)
        {
            try
            {
                if (frame.PixelWidth == 64 && frame.PixelHeight == 32)
                    return "default";

                if (frame.PixelWidth == 64 && frame.PixelHeight == 64 &&
                    (IsTransparent(frame, 54, 20) || IsTransparent(frame, 42, 48)))
                    return "slim";
            }
            catch
            {
            }

            return "default";
        }

        private static bool IsTransparent(BitmapSource frame, int x, int y)
        {
            if (x < 0 || x >= frame.PixelWidth || y < 0 || y >= frame.PixelHeight)
                return false;

            var pixel = new byte[4];
            frame.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return pixel[3] == 0;
        }
    }
}
