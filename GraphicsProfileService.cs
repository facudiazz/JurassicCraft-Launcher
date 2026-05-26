using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace JurassicCraftLauncher
{
    public class GraphicsProfileService
    {
        private readonly string _gameDir;
        private readonly string _modsDir;
        private readonly string _disabledModsDir;
        private readonly string _optionsPath;

        public GraphicsProfileService(string gameDir)
        {
            _gameDir = gameDir;
            _modsDir = Path.Combine(gameDir, "mods");
            _disabledModsDir = AppConstants.DisabledOptionalModsDir;
            _optionsPath = Path.Combine(gameDir, "options.txt");
        }

        public string ApplyPreset(string? requestedPreset)
        {
            string preset = NormalizePreset(requestedPreset);

            Directory.CreateDirectory(_modsDir);
            Directory.CreateDirectory(_disabledModsDir);

            int toggledMods = ApplyOptionalMods(preset);
            ApplyOptionsProfile(preset);

            return $"Preset {preset} aplicado. Mods ajustados: {toggledMods}.";
        }

        private int ApplyOptionalMods(string preset)
        {
            HashSet<string> disabledPrefixes = GetDisabledPrefixesForPreset(preset);
            Dictionary<string, string> activeMods = ListManagedMods(_modsDir);
            Dictionary<string, string> disabledMods = ListManagedMods(_disabledModsDir);
            int changes = 0;

            HashSet<string> allNames = new(activeMods.Keys, StringComparer.OrdinalIgnoreCase);
            allNames.UnionWith(disabledMods.Keys);

            foreach (string modName in allNames)
            {
                bool shouldDisable = disabledPrefixes.Any(prefix => modName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                string activePath = Path.Combine(_modsDir, modName);
                string disabledPath = Path.Combine(_disabledModsDir, modName);
                bool activeExists = File.Exists(activePath);
                bool disabledExists = File.Exists(disabledPath);

                if (shouldDisable)
                {
                    if (activeExists)
                    {
                        if (disabledExists)
                        {
                            File.Delete(disabledPath);
                        }

                        File.Move(activePath, disabledPath);
                        changes++;
                    }
                }
                else
                {
                    if (!activeExists && disabledExists)
                    {
                        File.Move(disabledPath, activePath);
                        changes++;
                    }
                    else if (activeExists && disabledExists)
                    {
                        File.Delete(disabledPath);
                    }
                }
            }

            return changes;
        }

        private Dictionary<string, string> ListManagedMods(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return Directory.GetFiles(folder, "*.jar", SearchOption.TopDirectoryOnly)
                .ToDictionary(path => Path.GetFileName(path)!, path => path, StringComparer.OrdinalIgnoreCase);
        }

        private HashSet<string> GetDisabledPrefixesForPreset(string preset)
        {
            // Por seguridad de compatibilidad con el server, los presets no desactivan mods
            // hasta que tengamos una whitelist verificada de mods 100% cliente.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private void ApplyOptionsProfile(string preset)
        {
            Dictionary<string, string> currentOptions = LoadOptions();
            Dictionary<string, string> profile = BuildOptionsForPreset(preset);

            foreach ((string key, string value) in profile)
            {
                currentOptions[key] = value;
            }

            SaveOptions(currentOptions);
        }

        private Dictionary<string, string> LoadOptions()
        {
            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(_optionsPath))
            {
                return map;
            }

            foreach (string line in File.ReadAllLines(_optionsPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line[..separator];
                string value = line[(separator + 1)..];
                map[key] = value;
            }

            return map;
        }

        private void SaveOptions(Dictionary<string, string> options)
        {
            string[] preferredOrder =
            {
                "graphicsMode",
                "renderDistance",
                "simulationDistance",
                "entityDistanceScaling",
                "particles",
                "mipmapLevels",
                "renderClouds",
                "biomeBlendRadius",
                "entityShadows",
                "ao",
                "maxFps",
                "enableVsync",
                "prioritizeChunkUpdates",
                "modelPart_cape",
                "modelPart_jacket",
                "modelPart_left_sleeve",
                "modelPart_right_sleeve",
                "modelPart_left_pants_leg",
                "modelPart_right_pants_leg",
                "modelPart_hat"
            };

            List<string> output = new();
            HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);

            foreach (string key in preferredOrder)
            {
                if (options.TryGetValue(key, out string? value))
                {
                    output.Add($"{key}:{value}");
                    written.Add(key);
                }
            }

            foreach (string key in options.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (written.Contains(key))
                {
                    continue;
                }

                output.Add($"{key}:{options[key]}");
            }

            File.WriteAllLines(_optionsPath, output, Encoding.UTF8);
        }

        private Dictionary<string, string> BuildOptionsForPreset(string preset)
        {
            Dictionary<string, string> profile = new(StringComparer.OrdinalIgnoreCase)
            {
                ["enableVsync"] = "false",
                ["modelPart_cape"] = "true",
                ["modelPart_jacket"] = "true",
                ["modelPart_left_sleeve"] = "true",
                ["modelPart_right_sleeve"] = "true",
                ["modelPart_left_pants_leg"] = "true",
                ["modelPart_right_pants_leg"] = "true",
                ["modelPart_hat"] = "true"
            };

            switch (preset)
            {
                case "Low":
                    profile["graphicsMode"] = "0";
                    profile["renderDistance"] = "10";
                    profile["simulationDistance"] = "6";
                    profile["entityDistanceScaling"] = "0.75";
                    profile["particles"] = "2";
                    profile["mipmapLevels"] = "2";
                    profile["renderClouds"] = "\"false\"";
                    profile["biomeBlendRadius"] = "0";
                    profile["entityShadows"] = "false";
                    profile["ao"] = "false";
                    profile["maxFps"] = "120";
                    profile["prioritizeChunkUpdates"] = "1";
                    break;
                case "High":
                    profile["graphicsMode"] = "1";
                    profile["renderDistance"] = "20";
                    profile["simulationDistance"] = "10";
                    profile["entityDistanceScaling"] = "1.5";
                    profile["particles"] = "0";
                    profile["mipmapLevels"] = "4";
                    profile["renderClouds"] = "\"true\"";
                    profile["biomeBlendRadius"] = "3";
                    profile["entityShadows"] = "true";
                    profile["ao"] = "true";
                    profile["maxFps"] = "260";
                    profile["prioritizeChunkUpdates"] = "0";
                    break;
                default:
                    profile["graphicsMode"] = "1";
                    profile["renderDistance"] = "14";
                    profile["simulationDistance"] = "8";
                    profile["entityDistanceScaling"] = "1.0";
                    profile["particles"] = "1";
                    profile["mipmapLevels"] = "4";
                    profile["renderClouds"] = "\"fast\"";
                    profile["biomeBlendRadius"] = "1";
                    profile["entityShadows"] = "true";
                    profile["ao"] = "true";
                    profile["maxFps"] = "165";
                    profile["prioritizeChunkUpdates"] = "0";
                    break;
            }

            return profile;
        }

        private string NormalizePreset(string? preset)
        {
            if (string.Equals(preset, "Low", StringComparison.OrdinalIgnoreCase))
            {
                return "Low";
            }

            if (string.Equals(preset, "High", StringComparison.OrdinalIgnoreCase))
            {
                return "High";
            }

            return "Medium";
        }
    }
}
