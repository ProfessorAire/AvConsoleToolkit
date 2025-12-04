// <copyright file="AppConfig.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using Config.Net;
using Spectre.Console;

namespace AvConsoleToolkit.Configuration
{
    /// <summary>
    /// Provides application-wide configuration management.
    /// The static initializer constructs an <see cref="ISettings"/> instance using a combination of
    /// a local config file (if present), a per-user config file under the user's AppData folder,
    /// and an in-memory dictionary of default values.
    /// </summary>
    public static class AppConfig
    {
        /// <summary>
        /// Static constructor that initializes the application's configuration provider.
        /// It ensures the user config directory exists, applies a local config file when present,
        /// and seeds default settings via an in-memory dictionary before building the final
        /// <see cref="ISettings"/> instance stored in <see cref="Settings"/>.
        /// Migrates existing ct.config files to act.config if found.
        /// </summary>
        static AppConfig()
        {
            var userPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AvConsoleToolkit", "act.config");
            var localPath = Path.Combine(Environment.CurrentDirectory, "act.config");

            // Legacy paths for migration
            var legacyUserPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ConsoleToolkit", "ct.config");
            var legacyLocalPath = Path.Combine(Environment.CurrentDirectory, "ct.config");

            // Ensure user and global config directories exist
            Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);

            // Migrate legacy config files if they exist
            MigrateLegacyConfig(legacyUserPath, userPath);
            MigrateLegacyConfig(legacyLocalPath, localPath);

            var builder = new ConfigurationBuilder<ISettings>();

            if (File.Exists(localPath))
            {
                builder.UseIniFile(localPath);
            }

            builder.UseIniFile(userPath)
            .UseInMemoryDictionary(new Dictionary<string, string>
            {
            // Default settings that cannot be defined by attributes are defined here
            {
                "Connection.AddressBooksLocation",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crestron", "ToolBox", "AddressBook")
            },
            })
            .Build();

            Settings = builder.Build();
        }

        /// <summary>
        /// Gets the full file system path to the legacy configuration file used by the application.
        /// </summary>
        /// <remarks>This property provides the location of the legacy configuration file named
        /// "ct.config" in the application's current working directory. It is intended for compatibility with older
        /// versions or migration scenarios. Use this property when access to the legacy configuration is
        /// required.</remarks>
        public static string LegacyLocalPath => Path.Combine(Environment.CurrentDirectory, "ct.config");

        /// <summary>
        /// Gets the full file system path to the legacy user configuration file for ConsoleToolkit.
        /// </summary>
        /// <remarks>This property provides the location of the legacy configuration file used by previous
        /// versions of ConsoleToolkit. The path is constructed using the user's application data folder and is intended
        /// for backward compatibility. New applications should prefer updated configuration mechanisms if
        /// available.</remarks>
        public static string LegacyUserPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConsoleToolkit",
            "ct.config");

        /// <summary>
        /// Gets the full file system path to the local configuration file used by the application.
        /// </summary>
        /// <remarks>The path is constructed by combining the application's current working directory with
        /// the file name "act.config". This property is static and read-only.</remarks>
        public static string LocalPath => Path.Combine(Environment.CurrentDirectory, "act.config");

        /// <summary>
        /// Gets the application settings instance used by the application.
        /// This property is initialized during type construction and remains available for the lifetime
        /// of the application.
        /// </summary>
        public static ISettings Settings { get; }

        /// <summary>
        /// Gets the full file path to the user's configuration file for the AvConsoleToolkit application.
        /// </summary>
        /// <remarks>The path is located within the user's application data folder and is suitable for
        /// storing user-specific settings. The file may not exist until created by the application.</remarks>
        public static string UserPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AvConsoleToolkit",
            "act.config");

        /// <summary>
        /// Migrates a legacy config file to the new location if it exists.
        /// </summary>
        /// <param name="legacyPath">The path to the legacy config file (ct.config).</param>
        /// <param name="newPath">The path to the new config file (act.config).</param>
        private static void MigrateLegacyConfig(string legacyPath, string newPath)
        {
            // Only migrate if the legacy file exists and the new file doesn't
            if (File.Exists(legacyPath))
            {
                if (File.Exists(newPath))
                {
                    AnsiConsole.MarkupLine($"[red]A legacy config file exists at '{legacyPath}', alongside a new 'act.config' file and will be ignored.[/]");
                    if (AnsiConsole.Prompt(new ConfirmationPrompt("Would you like to delete the legacy config file?")))
                    {
                        File.Delete(legacyPath);
                    }
                }
                else
                {
                    try
                    {
                        AnsiConsole.MarkupLine($"[yellow]Migrating legacy config file from '{legacyPath}' to '{newPath}'...[/]");
                        File.Move(legacyPath, newPath);
                    }
                    catch
                    {
                        // If move fails, try to copy instead
                        try
                        {
                            AnsiConsole.MarkupLine($"[yellow]Move failed, moving legacy config file from '{legacyPath}' to '{newPath}'...[/]");
                            File.Copy(legacyPath, newPath);
                            File.Delete(legacyPath);
                        }
                        catch
                        {
                            // If migration fails completely, just continue
                            // The application will work with default settings
                            AnsiConsole.MarkupLine($"[red]Unable to migrate the legacy config from '{legacyPath}' to '{newPath}'. Continuing with default settings.[/]");
                        }
                    }
                }

                if (File.Exists(newPath) && !File.Exists(legacyPath))
                {
                    AnsiConsole.MarkupLine($"[green]Migration successful.[/]");
                }
            }
        }
    }
}