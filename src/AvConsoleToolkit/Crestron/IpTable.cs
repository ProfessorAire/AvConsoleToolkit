// <copyright file="IpTable.cs">
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
using System.Globalization;
using System.IO;
using IniParser;
using IniParser.Model;
using Spectre.Console;

namespace AvConsoleToolkit.Crestron
{
    /// <summary>
    /// Provides IP Table related functionality.
    /// </summary>
    internal static class IpTable
    {
        /// <summary>
        /// Parses a DIP file and returns a list of IP table entries.
        /// </summary>
        /// <param name="dipFilePath">Path to the .dip file.</param>
        /// <returns>List of IP table entries.</returns>
        /// <exception cref="FileNotFoundException">The DIP file was not found.</exception>
        /// <exception cref="InvalidOperationException">The DIP file is malformed or missing required sections.</exception>
        public static List<Entry> ParseDipFile(string dipFilePath)
        {
            if (!File.Exists(dipFilePath))
            {
                throw new FileNotFoundException($"DIP file not found: {dipFilePath}", dipFilePath);
            }

            try
            {
                using var fileStream = File.OpenRead(dipFilePath);
                return ParseDipStream(fileStream);
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                throw new InvalidOperationException($"Failed to parse DIP file: {dipFilePath}", ex);
            }
        }

        /// <summary>
        /// Parses a DIP file from a stream and returns a list of IP table entries.
        /// </summary>
        /// <param name="stream">Stream containing the DIP file content.</param>
        /// <returns>List of IP table entries.</returns>
        /// <exception cref="ArgumentNullException">The stream is null.</exception>
        /// <exception cref="InvalidOperationException">The DIP file is malformed or missing required sections.</exception>
        public static List<Entry> ParseDipStream(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            var entries = new List<Entry>();

            try
            {
                var parser = new StreamIniDataParser();
                IniData data;

                using (var reader = new StreamReader(stream, leaveOpen: true))
                {
                    data = parser.ReadData(reader);
                }

                if (!data.Sections.ContainsSection("IPTable"))
                {
                    // No IPTable section - return empty list
                    return entries;
                }

                var ipTableSection = data["IPTable"];

                // Group entries by index - store raw values first
                var entriesByIndex = new Dictionary<string, (byte? ipId, string? address, byte? deviceId, int? port, string? roomId)>();

                foreach (var keyData in ipTableSection)
                {
                    var key = keyData.KeyName;
                    var value = keyData.Value;

                    // Extract the index from keys like "id0", "addr0", "port0", "room0"
                    var index = ExtractIndex(key);
                    if (index == null)
                    {
                        continue;
                    }

                    if (!entriesByIndex.ContainsKey(index))
                    {
                        entriesByIndex[index] = (null, null, null, null, null);
                    }

                    var current = entriesByIndex[index];

                    if (key.StartsWith("id", StringComparison.OrdinalIgnoreCase))
                    {
                        if (byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ipId))
                        {
                            current.ipId = ipId;
                        }
                    }
                    else if (key.StartsWith("addr", StringComparison.OrdinalIgnoreCase))
                    {
                        current.address = value;
                    }
                    else if (key.StartsWith("device", StringComparison.OrdinalIgnoreCase))
                    {
                        if (byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var deviceId))
                        {
                            current.deviceId = deviceId;
                        }
                    }
                    else if (key.StartsWith("room", StringComparison.OrdinalIgnoreCase))
                    {
                        current.roomId = value;
                    }
                    else if (key.StartsWith("port", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, out var port))
                        {
                            current.port = port;
                        }
                    }

                    entriesByIndex[index] = current;
                }

                foreach (var kvp in entriesByIndex)
                {
                    var (ipId, address, deviceId, port, roomId) = kvp.Value;

                    if (!ipId.HasValue || string.IsNullOrWhiteSpace(address))
                    {
                        continue;
                    }

                    try
                    {
                        var entry = new Entry(ipId.Value, address, deviceId, port, roomId);
                        entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        // Skip invalid entries but log the error
                        AnsiConsole.MarkupLine($"[red]Warning:[/] Skipping invalid IP table entry at index {kvp.Key}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse DIP stream", ex);
            }

            return entries;
        }

        /// <summary>
        /// Extracts the numeric index from a key like "id0", "addr12", "port3".
        /// </summary>
        /// <param name="key">The key to extract from.</param>
        /// <returns>The index as a string, or null if no valid index found.</returns>
        private static string? ExtractIndex(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            // Find where the digits start
            var digitStart = -1;
            for (var i = 0; i < key.Length; i++)
            {
                if (char.IsDigit(key[i]))
                {
                    digitStart = i;
                    break;
                }
            }

            if (digitStart == -1)
            {
                return null;
            }

            return key.Substring(digitStart);
        }

        /// <summary>
        /// Represents an IP table entry from a DIP file.
        /// </summary>
        public record Entry
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Entry"/> record.
            /// </summary>
            /// <param name="ipId">The IPID (hex format, must be between 0x03 and 0xFE).</param>
            /// <param name="address">The IP address or hostname (must be valid).</param>
            /// <param name="deviceId">Optional device ID (hex format, must be between 0x03 and 0xFE if provided).</param>
            /// <param name="port">Optional port number (must be between 257 and 65535 if provided).</param>
            /// <param name="roomId">Optional room ID (must not be null or whitespace if provided).</param>
            /// <exception cref="ArgumentOutOfRangeException">Thrown when ipId, deviceId, or port are out of valid range.</exception>
            /// <exception cref="ArgumentException">Thrown when address or roomId are invalid.</exception>
            public Entry(byte ipId, string address, byte? deviceId = null, int? port = null, string? roomId = null)
            {
                // Validate IPID
                if (ipId < 0x03 || ipId > 0xFE)
                {
                    throw new ArgumentOutOfRangeException(nameof(ipId), ipId, "IPID must be between 0x03 and 0xFE.");
                }

                // Validate address (IP or hostname)
                if (string.IsNullOrWhiteSpace(address))
                {
                    throw new ArgumentException("Address cannot be null or whitespace.", nameof(address));
                }

                // Basic validation - check if it's a valid IP address or hostname
                if (!System.Net.IPAddress.TryParse(address, out _) && !IsValidHostname(address))
                {
                    throw new ArgumentException($"Address '{address}' is not a valid IP address or hostname.", nameof(address));
                }

                // Validate device ID if provided
                if (deviceId.HasValue && (deviceId.Value < 0x03 || deviceId.Value > 0xFE))
                {
                    throw new ArgumentOutOfRangeException(nameof(deviceId), deviceId, "Device ID must be between 0x03 and 0xFE.");
                }

                // Validate port if provided
                if (port.HasValue && (port.Value < 257 || port.Value > 65535))
                {
                    throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 257 and 65535.");
                }

                // Validate room ID if provided (must not be null or whitespace)
                if (roomId != null && string.IsNullOrWhiteSpace(roomId))
                {
                    throw new ArgumentException("Room ID cannot be whitespace.", nameof(roomId));
                }

                this.IpId = ipId;
                this.Address = address;
                this.DeviceId = deviceId;
                this.Port = port;
                this.RoomId = roomId;
            }

            /// <summary>
            /// Gets the IP address or hostname.
            /// </summary>
            public string Address { get; }

            /// <summary>
            /// Gets the optional device ID (hex format).
            /// </summary>
            public byte? DeviceId { get; }

            /// <summary>
            /// Gets the IPID (hex format, e.g., 0x03, 0x0F).
            /// </summary>
            public byte IpId { get; }

            /// <summary>
            /// Gets the optional port number.
            /// </summary>
            public int? Port { get; }

            /// <summary>
            /// Gets the optional room ID.
            /// </summary>
            public string? RoomId { get; }

            /// <summary>
            /// Validates if a string is a valid hostname.
            /// </summary>
            /// <param name="hostname">The hostname to validate.</param>
            /// <returns>True if valid hostname, false otherwise.</returns>
            private static bool IsValidHostname(string hostname)
            {
                if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 253)
                {
                    return false;
                }

                // Check if hostname contains only valid characters
                // Valid characters: alphanumeric, dash, and dot
                foreach (var c in hostname)
                {
                    if (!char.IsLetterOrDigit(c) && c != '-' && c != '.')
                    {
                        return false;
                    }
                }

                // Split by dot and validate each label
                var labels = hostname.Split('.');
                foreach (var label in labels)
                {
                    if (string.IsNullOrEmpty(label) || label.Length > 63)
                    {
                        return false;
                    }

                    // Label cannot start or end with dash
                    if (label.StartsWith('-') || label.EndsWith('-'))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
