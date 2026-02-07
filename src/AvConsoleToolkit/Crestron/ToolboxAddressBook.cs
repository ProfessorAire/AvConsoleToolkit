// <copyright file="ToolboxAddressBook.cs">
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
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IniParser;
using Spectre.Console;

namespace AvConsoleToolkit.Crestron
{
    /// <summary>
    /// Utility for looking up Crestron device information from Toolbox address books.
    /// </summary>
    public static class ToolboxAddressBook
    {
        /// <summary>
        /// Looks up an address book entry by IP address or device name.
        /// </summary>
        /// <param name="ipAddressOrDeviceName">The IP address or device name to search for.</param>
        /// <returns>The first matching address book entry, or <see langword="null"/> if not found.</returns>
        public static async Task<Entry?> LookupEntryAsync(string ipAddressOrDeviceName)
        {
            if (string.IsNullOrWhiteSpace(ipAddressOrDeviceName))
            {
                return null;
            }

            return await Task.Run(
                () =>
                {
                    // Determine if identifier is an IP address
                    bool isIpAddress = IPAddress.TryParse(ipAddressOrDeviceName, out _);

                    // Get address book locations from config
                    var config = Configuration.AppConfig.Settings;
                    var addressBookLocations = config.Connection.AddressBooksLocation;

                    if (string.IsNullOrWhiteSpace(addressBookLocations))
                    {
                        return null;
                    }

                    // Split multiple locations if needed (semicolon or comma separated)
                    var locations = addressBookLocations
                        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                        .Select(loc => loc.Trim())
                        .Where(loc => !string.IsNullOrWhiteSpace(loc));

                    foreach (var location in locations)
                    {
                        // Check if location is a directory
                        if (Directory.Exists(location))
                        {
                            // Search for .xadr files (XML format)
                            var xadrFiles = Directory.GetFiles(location, "*.xadr", SearchOption.AllDirectories);
                            foreach (var file in xadrFiles)
                            {
                                var entry = isIpAddress
                                    ? SearchCrestronAddressBookByIp(file, ipAddressOrDeviceName)
                                    : SearchCrestronAddressBookByName(file, ipAddressOrDeviceName);
                                if (entry != null)
                                {
                                    return entry;
                                }
                            }
                        }
                        else if (File.Exists(location))
                        {
                            if (location.EndsWith(".xadr", StringComparison.OrdinalIgnoreCase))
                            {
                                var entry = isIpAddress
                                    ? SearchCrestronAddressBookByIp(location, ipAddressOrDeviceName)
                                    : SearchCrestronAddressBookByName(location, ipAddressOrDeviceName);
                                if (entry != null)
                                {
                                    return entry;
                                }
                            }
                        }
                    }

                    return null;
                });
        }

        /// <summary>
        /// Retrieves all address book entries from the configured address book locations.
        /// </summary>
        /// <remarks>Address book locations are read from the application configuration and may include
        /// multiple directories or files, separated by semicolons or commas. Only entries from files with the ".xadr"
        /// extension are parsed. Encrypted address books are not supported and will be skipped.</remarks>
        /// <returns>An immutable list of <see cref="Entry"/> objects representing all parsed address book entries. The list will
        /// be empty if no entries are found or if no address book locations are configured.</returns>
        public static IReadOnlyList<Entry> ListAllEntries(string addressBookLocations)
        {
            var entries = new List<Entry>();
            if (string.IsNullOrWhiteSpace(addressBookLocations))
            {
                return entries;
            }

            // Split multiple locations if needed (semicolon or comma separated)
            var locations = addressBookLocations.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var location in locations)
            {
                var entry = new Entry();
                // Check if location is a directory
                if (Directory.Exists(location))
                {
                    // Search for .xadr files (XML format)
                    var xadrFiles = Directory.GetFiles(location, "*.xadr", SearchOption.AllDirectories);
                    foreach (var file in xadrFiles)
                    {
                        try
                        {
                            var parser = new FileIniDataParser();
                            var data = parser.ReadFile(file);
                            if (!data.Sections.ContainsSection("ComSpecs"))
                            {
                                continue;
                            }
                            foreach (var iniEntry in data["ComSpecs"])
                            {
                                var parsed = ParseComSpecEntry(iniEntry.KeyName, iniEntry.Value);
                                if (parsed != null)
                                {
                                    // Try to get the comment from the Notes section
                                    if (data.Sections.ContainsSection("Notes") && parsed.DeviceName != null)
                                    {
                                        parsed.Comment = data["Notes"][parsed.DeviceName];
                                    }
                                    entries.Add(parsed);
                                }
                            }
                        }
                        catch
                        {
                            AnsiConsole.MarkupLineInterpolated($"[red]Error: Failed to parse address book file: [/][fucshia]'{file}[/][red]'. The address book may be encrypted; encrypted address books are not supported.");
                        }
                    }
                }
                else if (File.Exists(location) && location.EndsWith(".xadr", StringComparison.OrdinalIgnoreCase))
                {
                    var parser = new FileIniDataParser();
                    var data = parser.ReadFile(location);

                    if (!data.Sections.ContainsSection("ComSpecs"))
                    {
                        continue;
                    }

                    foreach (var cs in data["ComSpecs"])
                    {
                        var parsed = ParseComSpecEntry(cs.KeyName, cs.Value);
                        if (parsed != null)
                        {
                            // Try to get the comment from the Notes section
                            if (data.Sections.ContainsSection("Notes") && parsed.DeviceName != null)
                            {
                                parsed.Comment = data["Notes"][parsed.DeviceName];
                            }

                            parsed.SourceFile = location;

                            entries.Add(parsed);
                        }
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// Parses a ComSpec entry value.
        /// Format: "auto 10.20.0.22;username user;password pass;console secondary"
        /// </summary>
        private static Entry? ParseComSpecEntry(string deviceName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var entry = new Entry { DeviceName = deviceName };

            // Split by semicolon
            var parts = value.Split(';');

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                if (trimmed.StartsWith("auto ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.HostAddress = trimmed[5..].Trim();
                }
                else if (trimmed.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.HostAddress = trimmed[4..].Trim();
                }
                else if (trimmed.StartsWith("username ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Username = trimmed[9..].Trim();
                }
                else if (trimmed.StartsWith("password ", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Password = trimmed[9..].Trim();
                }
            }

            return entry;
        }

        /// <summary>
        /// Searches a Crestron Toolbox INI address book file by IP address.
        /// </summary>
        private static Entry? SearchCrestronAddressBookByIp(string filePath, string ipAddress)
        {
            try
            {
                var parser = new FileIniDataParser();
                var data = parser.ReadFile(filePath);

                if (!data.Sections.ContainsSection("ComSpecs"))
                {
                    return null;
                }

                foreach (var entry in data["ComSpecs"])
                {
                    var parsed = ParseComSpecEntry(entry.KeyName, entry.Value);
                    if (parsed != null && string.Equals(parsed.HostAddress, ipAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to get the comment from the Notes section
                        if (data.Sections.ContainsSection("Notes") && parsed.DeviceName != null)
                        {
                            parsed.Comment = data["Notes"][parsed.DeviceName];
                        }

                        return parsed;
                    }
                }
            }
            catch
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error: Failed to parse address book file: [/][fucshia]'{filePath}[/][red]'. The address book may be encrypted; encrypted address books are not supported.");
            }

            return null;
        }

        /// <summary>
        /// Searches a Crestron Toolbox address book file by device name.
        /// </summary>
        private static Entry? SearchCrestronAddressBookByName(string filePath, string deviceName)
        {
            try
            {
                var parser = new FileIniDataParser();
                var data = parser.ReadFile(filePath);

                if (!data.Sections.ContainsSection("ComSpecs"))
                {
                    return null;
                }

                foreach (var entry in data["ComSpecs"])
                {
                    if (string.Equals(entry.KeyName, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        var parsed = ParseComSpecEntry(entry.KeyName, entry.Value);

                        // Try to get the comment from the Notes section
                        if (parsed != null && data.Sections.ContainsSection("Notes"))
                        {
                            parsed.Comment = data["Notes"][entry.KeyName];
                        }

                        return parsed;
                    }
                }
            }
            catch
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error: Failed to parse address book file: [/][fucshia]'{filePath}[/][red]'. The address book may be encrypted; encrypted address books are not supported.");
            }

            return null;
        }

        /// <summary>
        /// Represents an address book entry with connection information for a Crestron device.
        /// </summary>
        public class Entry
        {
            /// <summary>
            /// Gets or sets the comment or note associated with this entry.
            /// </summary>
            public string? Comment { get; set; }

            /// <summary>
            /// Gets or sets the device name associated with this entry.
            /// </summary>
            public string? DeviceName { get; set; }

            /// <summary>
            /// Gets or sets the host address (IP or hostname) for the device.
            /// </summary>
            public string? HostAddress { get; set; }

            /// <summary>
            /// Gets or sets the password used to connect to the device.
            /// </summary>
            public string? Password { get; set; }

            /// <summary>
            /// Gets or sets the username used to connect to the device.
            /// </summary>
            public string? Username { get; set; }

            /// <summary>
            /// Gets the file the entry was loaded from.
            /// </summary>
            public string? SourceFile { get; set; }
        }
    }
}
