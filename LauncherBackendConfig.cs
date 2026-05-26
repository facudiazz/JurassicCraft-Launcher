using System;
using System.IO;
using System.Text.Json;

namespace JurassicCraftLauncher
{
    public class LauncherBackendConfig
    {
        public string GitHubOwner { get; set; } = "facudiazz";

        public string ModpackRepoName { get; set; } = "JurassicCraft-Modpack";

        public string LauncherRepoName { get; set; } = "JurassicCraft-Launcher";

        public string GitHubBranch { get; set; } = "main";

        public string ModpackReleaseTag { get; set; } = "v1.0.0";

        public string GitHubToken { get; set; } = string.Empty;

        public string DefaultMinecraftVersion { get; set; } = "1.20.1";

        public string DefaultForgeVersion { get; set; } = "47.4.10";
    }

    public static class LauncherBackend
    {
        private static readonly Lazy<LauncherBackendConfig> _current = new(LoadCurrent);

        public static LauncherBackendConfig Current => _current.Value;

        private static LauncherBackendConfig LoadCurrent()
        {
            LauncherBackendConfig config = new();

            try
            {
                Directory.CreateDirectory(AppConstants.AppDataDir);

                if (File.Exists(AppConstants.BackendConfigFile))
                {
                    var loaded = JsonSerializer.Deserialize<LauncherBackendConfig>(File.ReadAllText(AppConstants.BackendConfigFile));
                    if (loaded != null)
                    {
                        config = loaded;
                    }
                }
                else
                {
                    WriteTemplate(config);
                }
            }
            catch
            {
            }

            config.GitHubOwner = ReadEnvOrDefault("JC_GITHUB_OWNER", config.GitHubOwner);
            config.ModpackRepoName = ReadEnvOrDefault("JC_MODPACK_REPO", config.ModpackRepoName);
            config.LauncherRepoName = ReadEnvOrDefault("JC_LAUNCHER_REPO", config.LauncherRepoName);
            config.GitHubBranch = ReadEnvOrDefault("JC_GITHUB_BRANCH", config.GitHubBranch);
            config.ModpackReleaseTag = ReadEnvOrDefault("JC_MODPACK_RELEASE_TAG", config.ModpackReleaseTag);
            config.GitHubToken = ReadEnvOrDefault("JC_GITHUB_TOKEN", config.GitHubToken);
            config.DefaultMinecraftVersion = ReadEnvOrDefault("JC_MINECRAFT_VERSION", config.DefaultMinecraftVersion);
            config.DefaultForgeVersion = ReadEnvOrDefault("JC_FORGE_VERSION", config.DefaultForgeVersion);

            return config;
        }

        private static string ReadEnvOrDefault(string envVar, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(envVar);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static void WriteTemplate(LauncherBackendConfig config)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            File.WriteAllText(AppConstants.BackendConfigFile, JsonSerializer.Serialize(config, options));
        }
    }
}
