// <copyright file="Device.cs">
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleToolkit.Config
{
    public enum ConfigScope
    {
        Global,
        User,
        Local
    }

    public class ConfigManager
    {
        private readonly Dictionary<string, object> defaults;

        private readonly string globalPath;

        private readonly string localPath;

        private readonly string userPath;

        public ConfigManager(Dictionary<string, object> defaults)
        {
            this.defaults = defaults;
            this.globalPath = GetGlobalConfigPath();
            this.userPath = GetUserConfigPath();
            this.localPath = GetLocalConfigPath();
        }

        public object? GetConfigValue(string key)
        {
            var config = this.LoadMergedConfig();
            return config.TryGetValue(key, out var value) ? value : null;
        }

        public Dictionary<string, object> LoadConfig(string path)
        {
            var dict = new Dictionary<string, object>();
            if (!File.Exists(path))
            {
                return dict;
            }

            string? section = null;
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                {
                    continue;
                }

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    section = trimmed[1..^1];
                    continue;
                }
                var eq = trimmed.IndexOf('=');
                if (eq > 0)
                {
                    var key = trimmed[..eq].Trim();
                    var value = trimmed[eq + 1..].Trim();
                    var fullKey = section != null ? $"{section}.{key}" : key;
                    if (key.EndsWith("[]"))
                    {
                        var arrKey = fullKey[..^2];
                        if (!dict.TryGetValue(arrKey, out var arrObj) || arrObj is not List<string> arr)
                        {
                            arr = new List<string>();
                            dict[arrKey] = arr;
                        }
                        arr.Add(value);
                    }
                    else
                    {
                        dict[fullKey] = value;
                    }
                }
            }
            return dict;
        }

        public Dictionary<string, object> LoadMergedConfig()
        {
            var config = new Dictionary<string, object>(this.defaults);
            this.MergeConfig(config, this.LoadConfig(this.globalPath));
            this.MergeConfig(config, this.LoadConfig(this.userPath));
            this.MergeConfig(config, this.LoadConfig(this.localPath));
            return config;
        }

        public async Task SaveConfigAsync(string path, Dictionary<string, object> config)
        {
            var sb = new StringBuilder();
            var grouped = config.GroupBy(kv => kv.Key.Contains('.') ? kv.Key[..kv.Key.IndexOf('.')] : string.Empty);
            foreach (var group in grouped)
            {
                if (!string.IsNullOrEmpty(group.Key))
                {
                    sb.AppendLine($"[{group.Key}]");
                }

                foreach (var kv in group)
                {
                    var key = kv.Key.Contains('.') ? kv.Key[kv.Key.IndexOf('.') + 1..] : kv.Key;
                    if (kv.Value is List<string> arr)
                    {
                        foreach (var v in arr)
                        {
                            sb.AppendLine($"{key}[] = {v}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"{key} = {kv.Value}");
                    }
                }
            }

            await File.WriteAllTextAsync(path, sb.ToString());
        }

        public async Task SetConfigValueAsync(ConfigScope scope, string key, object value)
        {
            var path = scope switch
             {
             ConfigScope.Global => this.globalPath,
             ConfigScope.User => this.userPath,
             ConfigScope.Local => this.localPath,
             _ => throw new ArgumentOutOfRangeException()
             };

            var config = this.LoadConfig(path);
            config[key] = value;
            await this.SaveConfigAsync(path, config);
        }

        private static string GetGlobalConfigPath()
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var path = Path.Combine(dir, "ConsoleToolkit", "config");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        private static string GetLocalConfigPath()
        {
            var path = Path.Combine(Environment.CurrentDirectory, ".consoletoolkit", "config");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        private static string GetUserConfigPath()
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = Path.Combine(dir, ".consoletoolkit", "config");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        private void MergeConfig(Dictionary<string, object> baseConfig, Dictionary<string, object> overlay)
        {
            foreach (var kv in overlay)
            {
                if (kv.Value is List<string> arr)
                {
                    if (!baseConfig.TryGetValue(kv.Key, out var baseArrObj) || baseArrObj is not List<string> baseArr)
                    {
                        baseConfig[kv.Key] = new List<string>(arr);
                    }
                    else
                    {
                        baseArr.AddRange(arr);
                    }
                }
                else
                {
                    baseConfig[kv.Key] = kv.Value;
                }
            }
        }
    }
}
