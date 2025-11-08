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
using System.Diagnostics;
using System.IO;
using Config.Net;

namespace ConsoleToolkit.Configuration
{
    public static class AppConfig
    {
        static AppConfig()
        {
            var userPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ConsoleToolkit", "ct.config");
            var localPath = Path.Combine(Environment.CurrentDirectory, "ct.config");

            // Ensure user and global config directories exist
            Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);

            var builder = new ConfigurationBuilder<ISettings>();

            if (File.Exists(localPath))
            {
                builder.UseIniFile(localPath);
            }

            builder.UseIniFile(userPath)
            .UseInMemoryDictionary(new Dictionary<string, string>
            {
                // Default settings are defined here
                { "Connection.AddressBooksLocation",  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crestron", "ToolBox", "AddressBook") }
            })
            .Build();

            Settings = builder.Build();
        }

        public static ISettings Settings { get; }
    }
}