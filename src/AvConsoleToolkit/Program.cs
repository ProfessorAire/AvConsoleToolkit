// <copyright file="Program.cs">
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
using AvConsoleToolkit.Commands.Sftp;
using Spectre.Console.Cli;

namespace AvConsoleToolkit
{
    /// <summary>
    /// Entry point for the ConsoleToolkit application.
    /// Configures and runs the Spectre.Console.Cli command application.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Gets the <see cref="CommandApp"/> instance for the application.
        /// </summary>
        public static CommandApp? App { get; private set; }

        /// <summary>
        /// Main method. Configures the command-line interface and runs the application.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code.</returns>
        public static int Main(string[] args)
        {
            App = new CommandApp();

            App.Configure(config =>
            {
                config.SetApplicationName("act")
                .PropagateExceptions()
                .UseAssemblyInformationalVersion()
                .ValidateExamples();

                if (OperatingSystem.IsWindows())
                {
                    config.AddCommand<Commands.UpdateCommand>("update")
                        .WithDescription("Check for and install updates for this application")
                        .WithExample(["update"])
                        .WithExample(["update", "--yes"]);
                }

                config.AddCommand<Commands.AboutCommand>("about")
                    .WithDescription("Display information about the application")
                    .WithExample(["about"])
                    .WithExample(["about", "--license"])
                    .WithExample(["about", "--licenses"]);

                config.AddBranch("crestron", branch =>
                {
                    branch.SetDescription("Commands for Crestron hardware management");

                    branch.AddCommand<Commands.Crestron.CrestronPassThroughCommand>("connect")
                        .WithAlias("c")
                        .WithDescription("Connect to a Crestron device via interactive SSH session")
                        .WithExample(["crestron", "connect", "192.168.1.100"])
                        .WithExample(["crestron", "connect", "192.168.1.100", "-u", "admin", "-p", "password"])
                        .WithExample(["crestron", "c", "192.168.1.100", "-v"]);

                    branch.AddBranch("program", program =>
                    {
                        program.AddCommand<Commands.Crestron.Program.ProgramUploadCommand>("upload")
                            .WithAlias("u")
                            .WithAlias("load")
                            .WithAlias("l")
                            .WithDescription("Upload a program to Crestron hardware (supports glob patterns)")
                            .WithExample(["crestron", "program", "upload", "myprogram.cpz", "-s", "1", "--address", "192.168.1.100", "-u", "admin", "-p", "password"])
                            .WithExample(["crestron", "program", "upload", "*.lpz", "-s", "1", "-a", "192.168.1.100"])
                            .WithExample(["crestron", "program", "upload", "programs/test_*.cpz", "-s", "2", "-a", "192.168.1.100", "-c"]);
                    })
                    .WithAlias("p");
                })
                .WithAlias("c");

                config.AddBranch("sftp", sftp =>
                {
                    sftp.SetDescription("Commands performed using SFTP.");
                    sftp.AddCommand<FileEditCommand>("edit")
                        .WithAlias("e")
                        .WithDescription("Edit a file on a remote device via SFTP and a built-in text editor, or specified local application. Applications can be configured on a per-extension basis via the editor settings.")
                        .WithExample(["sftp", "edit", "program01/config.xml", "-a", "192.168.1.100"])
                        .WithExample(["sftp", "edit", "user/settings.json", "-a", "192.168.1.100", "-f"])
                        .WithExample(["sftp", "edit", "program01/data.txt", "-a", "192.168.1.100", "-b"])
                        .WithExample(["sftp", "edit", "user/appSettings.jsonc", "-a", "192.168.1.100", "-e", "notepad"]);
                });

                config.AddBranch("config", cfg =>
                {
                    cfg.SetDescription("Configuration management, such as setting or reading configuration values.");

                    cfg.AddCommand<Commands.Config.ListConfigCommand>("list")
                        .WithAlias("l")
                        .WithDescription("Lists all configuration keys and values from merged configuration.")
                        .WithExample(["config", "list"])
                        .WithExample(["config", "l", "--show-sources"]);

                    cfg.AddCommand<Commands.Config.SetConfigCommand>("set")
                        .WithAlias("s")
                        .WithDescription("Sets a single configuration key.")
                        .WithExample(["config", "set", "-s", "Connection", "AddressBooksLocation", "C:/addressBooks"])
                        .WithExample(["config", "set", "--section", "Connection", "AddressBooksLocation", "C:/addressBooks", "--local"]);

                    cfg.AddCommand<Commands.Config.RemoveConfigCommand>("remove")
                        .WithAlias("r")
                        .WithDescription("Removes a single configuration key.")
                        .WithExample(["config", "remove", "Connection", "AddressBooksLocation"])
                        .WithExample(["config", "r", "Connection", "AddressBooksLocation", "--local"]);
                });

                config.AddBranch("addressbook", ab =>
                {
                    ab.SetDescription("Utilities for looking up device information from supported address books.");

                    ab.AddCommand<Commands.AddressBook.AddressBookListCommand>("list")
                        .WithAlias("ls")
                        .WithDescription("List all entries from configured address books")
                        .WithExample(["addressbook", "list"])
                        .WithExample(["ab", "ls", "--detailed"]);

                    ab.AddCommand<Commands.AddressBook.AddressBookLookupCommand>("lookup")
                        .WithAlias("l")
                        .WithDescription("Look up a specific address book entry by name or IP address")
                        .WithExample(["addressbook", "lookup", "SomeEntryName"])
                        .WithExample(["ab", "l", "10.10.120.12"]);
                })
                .WithAlias("ab");

                config.AddBranch("cert", cert =>
                {
                    cert.SetDescription("Certificate management commands for creating CAs, device certificates, and more.");

                    cert.AddBranch("ca", ca =>
                    {
                        ca.SetDescription("Manage Certificate Authorities.");

                        ca.AddCommand<Commands.Cert.Ca.CaCreateCommand>("create")
                            .WithDescription("Create a new Certificate Authority")
                            .WithExample(["cert", "ca", "create", "my-ca", "-o", "MyOrg", "-c", "US"]);

                        ca.AddCommand<Commands.Cert.Ca.CaListCommand>("list")
                            .WithAlias("ls")
                            .WithDescription("List all Certificate Authorities")
                            .WithExample(["cert", "ca", "list"]);

                        ca.AddCommand<Commands.Cert.Ca.CaDeleteCommand>("delete")
                            .WithAlias("rm")
                            .WithDescription("Delete a Certificate Authority and all its certificates")
                            .WithExample(["cert", "ca", "delete", "my-ca"])
                            .WithExample(["cert", "ca", "rm", "my-ca", "-y"]);

                        ca.AddCommand<Commands.Cert.Ca.CaImportCommand>("import")
                            .WithDescription("Import an existing root CA certificate from PEM or PFX files")
                            .WithExample(["cert", "ca", "import", "my-ca", "--cert", "ca.crt", "--key", "ca.key"])
                            .WithExample(["cert", "ca", "import", "my-ca", "--pfx", "ca.pfx", "--password", "secret"]);
                    });

                    cert.AddCommand<Commands.Cert.CertCreateCommand>("create")
                        .WithDescription("Create a new device certificate signed by a CA")
                        .WithExample(["cert", "create", "server.example.com", "--ca", "my-ca", "-i", "192.168.1.100"]);

                    cert.AddCommand<Commands.Cert.CertListCommand>("list")
                        .WithAlias("ls")
                        .WithDescription("List all device certificates with expiration status")
                        .WithExample(["cert", "list"])
                        .WithExample(["cert", "ls", "--ca", "my-ca"]);

                    cert.AddCommand<Commands.Cert.CertExportCommand>("export")
                        .WithDescription("Export a device certificate from the database to disk")
                        .WithExample(["cert", "export", "1"])
                        .WithExample(["cert", "export", "1", "-o", "/path/to/output"]);

                    cert.AddCommand<Commands.Cert.CertDeleteCommand>("delete")
                        .WithAlias("rm")
                        .WithDescription("Delete a device certificate from the database")
                        .WithExample(["cert", "delete", "1"])
                        .WithExample(["cert", "rm", "1", "-y"]);

                    cert.AddCommand<Commands.Cert.CertImportCommand>("import")
                        .WithDescription("Import an existing device certificate from PEM or PFX files")
                        .WithExample(["cert", "import", "--ca", "my-ca", "--cert", "server.crt", "--key", "server.key"])
                        .WithExample(["cert", "import", "--ca", "my-ca", "--pfx", "server.pfx", "--password", "secret"]);

                    cert.AddCommand<Commands.Cert.CertInstallRootCommand>("install-root")
                        .WithDescription("Install a root CA certificate on the local machine (Windows/Linux)")
                        .WithExample(["cert", "install-root", "my-ca"]);
                });
            });

            var result = App.Run(args);
            Connections.ConnectionFactory.Instance.ReleaseAll();
            return result;
        }
    }
}