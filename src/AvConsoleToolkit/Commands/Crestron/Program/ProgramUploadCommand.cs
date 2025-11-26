// <copyright file="ProgramUploadCommand.cs">
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using AvConsoleToolkit.Crestron;
using AvConsoleToolkit.Ssh;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Crestron.Program
{
    /// <summary>
    /// Command that uploads Crestron program packages to a target device using SSH/SFTP.
    /// Supports uploading full program package files or only changed files (delta upload),
    /// preserving timestamps, registering the program on the device, and restarting the program.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class ProgramUploadCommand : AsyncCommand<ProgramUploadSettings>
    {
        /// <summary>
        /// Name of the hash manifest file stored on the remote device.
        /// </summary>
        private const string HashManifestFileName = ".act.hash";

        /// <summary>
        /// The supported program package file extensions.
        /// </summary>
        private static readonly string[] SupportedExtensions = [".cpz", ".clz", ".lpz"];

        /// <summary>
        /// Executes the upload operation.
        /// Validates inputs, optionally looks up credentials from address books, connects to the device via SSH/SFTP,
        /// uploads files (either entire package or changed files only), registers the program if required, and restarts it.
        /// </summary>
        /// <param name="context">The command execution context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">Options controlling upload behavior such as host, credentials, slot, and flags.</param>
        /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
        /// <returns>Exit code indicating success (0) or failure (non-zero).</returns>
        /// <exception cref="System.IO.FileNotFoundException">When the specified program file does not exist.</exception>
        /// <exception cref="ArgumentException">When invalid arguments are provided (for example unsupported extension or invalid slot).</exception>
        /// <exception cref="System.IO.IOException">I/O errors during file extraction or file system operations.</exception>
        /// <exception cref="Renci.SshNet.Common.SshException">SSH/SFTP connection or operation failures.</exception>
        public override async Task<int> ExecuteAsync(CommandContext context, ProgramUploadSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                // Validate inputs
                if (!File.Exists(settings.ProgramFile))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Program file not found.");
                    return 1;
                }

                var extension = Path.GetExtension(settings.ProgramFile).ToLowerInvariant();
                if (!SupportedExtensions.Contains(extension))
                {
                    AnsiConsole.MarkupLine($"[red]Error: Unsupported file extension. Supported: {string.Join(", ", SupportedExtensions)}[/]");
                    return 1;
                }

                if (settings.Slot is < 1 or > 10)
                {
                    AnsiConsole.MarkupLine("[red]Error: Slot must be between 1 and 10.[/]");
                    return 1;
                }

                if (string.IsNullOrEmpty(settings.Username) && string.IsNullOrEmpty(settings.Password))
                {
                    if (settings.Verbose)
                    {
                        AnsiConsole.MarkupLine("[fuchsia]No username/password provided, looking up values from address books.[/]");
                    }

                    var entry = await ToolboxAddressBook.LookupEntryAsync(settings.Host);
                    if (entry is null)
                    {
                        if (settings.Verbose)
                        {
                            AnsiConsole.MarkupLine("[red]Could not find device in address books and no username/password provided.[/]");
                        }

                        return 101;
                    }

                    if (entry.Username is null || entry.Password is null)
                    {
                        if (settings.Verbose)
                        {
                            AnsiConsole.MarkupLine("[red]Address book entry is missing username or password.[/]");
                        }

                        return 102;
                    }

                    settings.Username = entry.Username;
                    settings.Password = entry.Password;
                }

                var remotePath = $"program{settings.Slot:D2}";

                AnsiConsole.MarkupLineInterpolated($"[teal]Uploading {Path.GetFileName(settings.ProgramFile)} to slot {settings.Slot} on device '{settings.Host}'...[/]");

                // .clz files must always be extracted and uploaded as individual files
                // because they are not full program packages that can be loaded directly
                var isClz = extension == ".clz";

                var result = -1;
                if (settings.ChangedOnly || isClz)
                {
                    result = await UploadChangedFilesAsync(settings, remotePath, extension, cancellationToken);
                }
                else
                {
                    result = await UploadProgramFileAsync(settings, remotePath, cancellationToken);
                }

                if (result == 0)
                {
                    AnsiConsole.MarkupLine("\r\n[green]Program upload completed.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("\r\n[red]Program upload failed.[/]");
                }

                return result;
            }
            catch (Exception ex)
            {
                if (!settings.Verbose)
                {
                    AnsiConsole.MarkupLine($"\r\n[red]Error: {ex.Message}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"\r\n[red]Error:\r\n{ex}[/]");
                }

                return 1;
            }
        }

        /// <summary>
        /// Computes the SHA256 hash of a file.
        /// </summary>
        /// <param name="filePath">Path to the file to hash.</param>
        /// <returns>Hex-encoded SHA256 hash string.</returns>
        private static async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var fileStream = File.OpenRead(filePath);
            var hashBytes = await sha256.ComputeHashAsync(fileStream);
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Configures the IP table from the provided IP table entries.
        /// </summary>
        /// <param name="shellStream">Shell stream to communicate with the device.</param>
        /// <param name="entries">List of IP table entries to configure.</param>
        /// <param name="slot">Program slot number.</param>
        /// <param name="verbose">Whether to emit verbose diagnostic output.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if IP table was configured successfully; false on error.</returns>
        private static async Task<bool> ConfigureIpTableAsync(
            Ssh.IShellStream shellStream,
            List<IpTable.Entry> entries,
            int slot,
            bool verbose,
            CancellationToken cancellationToken)
        {
            if (entries.Count == 0)
            {
                if (verbose)
                {
                    AnsiConsole.MarkupLine("[dim]No IP table entries to configure.[/]");
                }
                return true;
            }

            AnsiConsole.MarkupLine($"[cyan]Configuring IP table with {entries.Count} entries...[/]");

            // Clear existing IP table
            var clearResult = await ConsoleCommands.ClearIpTableAsync(shellStream, slot, cancellationToken);
            if (!clearResult)
            {
                AnsiConsole.MarkupLine("[yellow]Warning: Failed to clear IP table, continuing anyway...[/]");
            }

            // Add each entry
            var successCount = 0;
            foreach (var entry in entries)
            {
                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Adding IP table entry: IPID={entry.IpId}, Address={entry.Address.EscapeMarkup()}[/]");
                }

                var addResult = await ConsoleCommands.AddIpTableEntryAsync(
                    shellStream,
                    slot,
                    entry,
                    cancellationToken);

                if (addResult)
                {
                    successCount++;
                }
                else if (verbose)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Failed to add IP table entry for IPID {entry.IpId}[/]");
                }
            }

            AnsiConsole.MarkupLine($"[green]IP table configured: {successCount} of {entries.Count} entries added successfully.[/]");
            return successCount > 0;
        }

        /// <summary>
        /// Creates a .zig file from a .sig file by wrapping it in a zip archive.
        /// </summary>
        /// <param name="sigFilePath">Path to the .sig file.</param>
        /// <param name="outputZigPath">Path where the .zig file should be created.</param>
        private static void CreateZigFromSig(string sigFilePath, string outputZigPath)
        {
            using var archive = ZipFile.Open(outputZigPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(sigFilePath, Path.GetFileName(sigFilePath));
        }

        /// <summary>
        /// Downloads and parses the hash manifest file from the remote device.
        /// </summary>
        /// <param name="sftpClient">Connected SFTP client.</param>
        /// <param name="remotePath">Remote program directory path.</param>
        /// <param name="verbose">Whether to emit verbose diagnostic output.</param>
        /// <returns>Dictionary mapping relative file paths to their stored hashes, or null if manifest doesn't exist.</returns>
        private static async Task<Dictionary<string, string>?> DownloadHashManifestAsync(
            ISftpClient sftpClient,
            string remotePath,
            bool verbose = false)
        {
            var remoteHashPath = $"{remotePath}/{HashManifestFileName}";

            if (!sftpClient.Exists(remoteHashPath))
            {
                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]No hash manifest found at '{remoteHashPath.EscapeMarkup()}'[/]");
                }
                return null;
            }

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Downloading hash manifest from '{remoteHashPath.EscapeMarkup()}'[/]");
            }

            return await Task.Run(() =>
                   {
                       using var memoryStream = new MemoryStream();
                       sftpClient.DownloadFile(remoteHashPath, memoryStream);
                       memoryStream.Position = 0;

                       var hashes = new Dictionary<string, string>();
                       using var reader = new StreamReader(memoryStream, Encoding.UTF8);

                       while (!reader.EndOfStream)
                       {
                           var line = reader.ReadLine();
                           if (string.IsNullOrWhiteSpace(line))
                           {
                               continue;
                           }

                           var parts = line.Split('=', 2);
                           if (parts.Length == 2)
                           {
                               hashes[parts[0].Trim()] = parts[1].Trim();
                           }
                       }

                       if (verbose)
                       {
                           AnsiConsole.MarkupLine($"[dim]Loaded {hashes.Count} file hashes from manifest[/]");
                       }

                       return hashes;
                   });
        }

        /// <summary>
        /// Ensures that the specified remote directory and its ancestors exist on the SFTP server.
        /// Creates missing directories as needed.
        /// </summary>
        /// <param name="sftpClient">Connected SFTP client.</param>
        /// <param name="remotePath">Remote directory path to ensure exists.</param>
        private static void EnsureRemoteDirectoryExists(ISftpClient sftpClient, string remotePath)
        {
            var parts = remotePath.Split('/');
            var currentPath = string.Empty;

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                if (!sftpClient.Exists(currentPath))
                {
                    sftpClient.CreateDirectory(currentPath);
                }
            }
        }

        /// <summary>
        /// Retrieves metadata for all remote files under the specified remote path.
        /// </summary>
        /// <param name="sftpClient">Connected SFTP client.</param>
        /// <param name="remotePath">Base remote path to enumerate.</param>
        /// <param name="verbose">Whether to emit verbose diagnostic output.</param>
        /// <returns>A dictionary mapping relative remote paths to <see cref="ISftpFile"/> metadata.</returns>
        private static async Task<Dictionary<string, ISftpFile>> GetRemoteFileMetadataAsync(
            ISftpClient sftpClient,
            string remotePath,
            bool verbose = false)
        {
            return await Task.Run(() =>
                   {
                       var files = new Dictionary<string, ISftpFile>();

                       if (!sftpClient.Exists(remotePath))
                       {
                           return files;
                       }

                       GetRemoteFilesRecursive(sftpClient, remotePath, remotePath, files, verbose);
                       return files;
                   });
        }

        /// <summary>
        /// Recursively enumerates remote files and populates the provided dictionary with relative paths.
        /// </summary>
        /// <param name="sftpClient">Connected SFTP client.</param>
        /// <param name="currentPath">Current directory being enumerated.</param>
        /// <param name="basePath">Base path used to calculate relative paths.</param>
        /// <param name="files">Dictionary to populate with relative path => file metadata.</param>
        /// <param name="verbose">Whether to emit verbose diagnostic output.</param>
        private static void GetRemoteFilesRecursive(
            ISftpClient sftpClient,
            string currentPath,
            string basePath,
            Dictionary<string, ISftpFile> files,
            bool verbose = false)
        {
            var items = sftpClient.ListDirectory(currentPath);

            foreach (var item in items)
            {
                if (item.Name is "." or "..")
                {
                    continue;
                }

                if (item.IsDirectory)
                {
                    GetRemoteFilesRecursive(sftpClient, item.FullName, basePath, files, verbose);
                }
                else
                {
                    // Calculate relative path correctly - ensure we handle the leading slash
                    var fullPath = item.FullName;
                    var relativeStart = basePath.StartsWith('/') ? basePath.Length : basePath.Length + 1;
                    var relativePath = fullPath.Length > relativeStart ? fullPath[relativeStart..].TrimStart('/') : string.Empty;

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Adding remote file: '{relativePath.EscapeMarkup()}' (full: '{item.FullName.EscapeMarkup()}', base: '{basePath.EscapeMarkup()}', startIndex: {relativeStart})[/]");
                    }

                    files[relativePath] = item;
                }
            }
        }

        /// <summary>
        /// Determines whether a local file differs from the corresponding remote file.
        /// Uses hash comparison when available, otherwise falls back to timestamp comparison.
        /// </summary>
        /// <param name="localFile">Full path to the local file.</param>
        /// <param name="localBasePath">Local base directory used to compute the relative path.</param>
        /// <param name="remoteFiles">Dictionary of remote file metadata keyed by relative path.</param>
        /// <param name="remoteHashes">Optional dictionary of remote file hashes keyed by relative path.</param>
        /// <param name="verbose">Whether to emit verbose diagnostic output.</param>
        /// <returns>True when the file is new or has changed relative to remote.</returns>
        private static async Task<bool> IsFileChangedAsync(
            string localFile,
            string localBasePath,
            Dictionary<string, ISftpFile> remoteFiles,
            Dictionary<string, string>? remoteHashes,
            bool verbose = false)
        {
            var relativePath = Path.GetRelativePath(localBasePath, localFile).Replace('\\', '/');

            // Check if file exists remotely
            var fileExistsRemotely = remoteFiles.TryGetValue(relativePath, out var remoteFile);

            if (!fileExistsRemotely)
            {
                // File doesn't exist remotely, so it's new/changed
                return true;
            }

            // If we have a hash for this file, use hash comparison
            if (remoteHashes != null && remoteHashes.TryGetValue(relativePath, out var remoteHash))
            {
                var localHash = await ComputeFileHashAsync(localFile);

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Comparing hashes for {relativePath.EscapeMarkup()}:[/]");
                    AnsiConsole.MarkupLine($"[dim]  Local:  {localHash}[/]");
                    AnsiConsole.MarkupLine($"[dim]  Remote: {remoteHash}[/]");
                }

                var changed = !string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase);

                if (verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]  Changed: {changed}[/]");
                }

                return changed;
            }

            // No hash available, fall back to timestamp comparison
            var localLastWriteTime = File.GetLastWriteTimeUtc(localFile);
            var remoteLastWriteTime = remoteFile!.LastWriteTimeUtc;
            var timeDifference = Math.Abs((localLastWriteTime - remoteLastWriteTime).TotalSeconds);

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]No hash available for {relativePath.EscapeMarkup()}, using timestamp comparison:[/]");
                AnsiConsole.MarkupLine($"[dim]  Local:  {localLastWriteTime:yyyy-MM-dd HH:mm:ss.fff} UTC[/]");
                AnsiConsole.MarkupLine($"[dim]  Remote: {remoteLastWriteTime:yyyy-MM-dd HH:mm:ss.fff} UTC[/]");
                AnsiConsole.MarkupLine($"[dim]  Diff:   {timeDifference:F3} seconds[/]");
            }

            var timestampChanged = timeDifference > 2;

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]  Changed: {timestampChanged}[/]");
            }

            return timestampChanged;
        }

        /// <summary>
        /// Checks for a .sig file alongside the .lpz program file and creates a .zig file if found.
        /// </summary>
        /// <param name="lpzFilePath">Path to the .lpz program file.</param>
        /// <param name="settings">Upload settings to check the NoZig flag.</param>
        /// <returns>Path to the created .zig file, or null if no .sig file exists or NoZig is set.</returns>
        private static string? PrepareSignatureFile(string lpzFilePath, ProgramUploadSettings settings)
        {
            if (settings.NoZig)
            {
                if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine("[dim]--nozig flag set, skipping signature file upload.[/]");
                }
                return null;
            }

            var sigFilePath = Path.ChangeExtension(lpzFilePath, ".sig");
            if (!File.Exists(sigFilePath))
            {
                if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]No signature file found at '{sigFilePath.EscapeMarkup()}'[/]");
                }
                return null;
            }

            var zigFileName = Path.ChangeExtension(Path.GetFileName(lpzFilePath), ".zig");
            var zigFilePath = Path.Combine(Path.GetTempPath(), zigFileName);

            if (settings.Verbose)
            {
                AnsiConsole.MarkupLine($"[cyan]Creating .zig file from signature: '{Path.GetFileName(sigFilePath).EscapeMarkup()}'[/]");
            }

            CreateZigFromSig(sigFilePath, zigFilePath);
            return zigFilePath;
        }

        /// <summary>
        /// Registers the uploaded program on the device using console commands.
        /// Handles .lpz and .cpz package types and optionally extracts the main assembly name.
        /// </summary>
        /// <param name="shellStream">Shell stream to communicate with the device console.</param>
        /// <param name="slot">Program slot number.</param>
        /// <param name="extension">File extension of the program package (e.g., .cpz or .lpz).</param>
        /// <param name="tempDirectory">Temporary directory where package contents were extracted.</param>
        /// <param name="cancellationToken">Cancellation token to observe.</param>
        /// <returns>0 on success, non-zero on failure.</returns>
        private static async Task<int> RegisterProgram(Ssh.IShellStream shellStream, int slot, string extension, string tempDirectory, CancellationToken cancellationToken)
        {
            var success = false;
            if (extension == ".lpz")
            {
                success = await ConsoleCommands.RegisterProgramAsync(shellStream, slot, cancellationToken);
            }
            else if (extension == ".cpz")
            {
                AnsiConsole.MarkupLine("[cyan]Registering .cpz program...[/]");

                var (success2, mainAssemblyName) = await TryGetMainAssemblyNameAsync(tempDirectory);
                if (!success2)
                {
                    AnsiConsole.MarkupLine("[red]Error: Could not determine main assembly name from manifest.info or ProgramInfo.config.[/]");
                    return 1;
                }

                success = await ConsoleCommands.RegisterProgramAsync(shellStream, slot, mainAssemblyName, cancellationToken);
            }

            return success ? 0 : 1;
        }

        /// <summary>
        /// Attempts to determine the main assembly name from package manifest files.
        /// Looks for "manifest.info" and "ProgramInfo.config" within the extracted package directory.
        /// </summary>
        /// <param name="tempDirectory">Temporary directory where package contents are extracted.</param>
        /// <returns>A tuple indicating success and the discovered main assembly name (or null if not found).</returns>
        private static async Task<(bool Success, string? MainAssemblyName)> TryGetMainAssemblyNameAsync(string tempDirectory)
        {
            var manifestPath = Path.Combine(tempDirectory, "manifest.info");
            string? mainAssemblyName = null;
            if (File.Exists(manifestPath))
            {
                var manifestLines = await File.ReadAllLinesAsync(manifestPath);
                foreach (var line in manifestLines)
                {
                    if (line.StartsWith("MainAssembly=", StringComparison.OrdinalIgnoreCase))
                    {
                        var assemblyPart = line[13..];
                        var colonIndex = assemblyPart.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            assemblyPart = assemblyPart[..colonIndex];
                        }
                        if (assemblyPart.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            var count = assemblyPart.Length - 4;
                            mainAssemblyName = assemblyPart[..count];
                        }
                        else
                        {
                            mainAssemblyName = assemblyPart;
                        }

                        break;
                    }
                }
            }
            else
            {
                // Try ProgramInfo.config
                var configPath = Path.Combine(tempDirectory, "ProgramInfo.config");
                if (File.Exists(configPath))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(configPath);
                        using var reader = new XmlTextReader(new StringReader(xml));
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Name == "EntryPoint")
                            {
                                mainAssemblyName = reader.ReadString();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error:[/] Failed to parse ProgramInfo.config: {ex.Message}");
                    }
                }
            }

            return (!string.IsNullOrEmpty(mainAssemblyName), mainAssemblyName);
        }

        /// <summary>
        /// Performs an analysis of the package contents and uploads only changed files to the remote device via SFTP.
        /// This method extracts the package to a temporary directory, compares timestamps against remote files, and
        /// uploads changed/new files with per-file progress and retry logic.
        /// </summary>
        /// <param name="settings">Upload settings containing host, credentials, and flags.</param>
        /// <param name="remotePath">Remote program directory path on the device.</param>
        /// <param name="extension">Program package extension.</param>
        /// <param name="cancellationToken">Cancellation token to observe during network and file operations.</param>
        /// <returns>Exit code 0 on success; non-zero on failure.</returns>
        private static async Task<int> UploadChangedFilesAsync(
            ProgramUploadSettings settings,
            string remotePath,
            string extension,
            CancellationToken cancellationToken)
        {
            // First, analyze files without SSH connection - only need SFTP for listing
            var sftpClient = await SshManager.GetSftpClientAsync(settings.Host, settings.Username, settings.Password);

            if (!sftpClient.IsConnected)
            {
                await AnsiConsole.Status()
                   .StartAsync("Connecting to device...", async ctx =>
                   {
                       ctx.Status("Connecting to SFTP client for file analysis.");
                       await sftpClient.ConnectAsync(cancellationToken);
                       ctx.Status("Connected");
                   });
            }

            var analysisResult = await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns([
                    new TaskDescriptionColumn(),
                    new SpinnerColumn
                {
                    CompletedStyle = new Style(Color.Green),
                    PendingStyle = new Style(Color.DarkSlateGray2),
                    PendingText = "...",
                    Spinner = Spinner.Known.Dots8Bit,
                    CompletedText = "<completed>",
                    Style = new Style(Color.Teal)
                },
                    new ElapsedTimeColumn { Style = new Style(Color.Blue) },
                    ])
                .AutoRefresh(!settings.Verbose)
                .StartAsync(async ctx =>
                {
                    var extractTask = ctx.AddTask("[Yellow]Extract program archive[/]", autoStart: false);
                    var remoteTask = ctx.AddTask("[Yellow]Retrieving remote file metadata[/]", autoStart: false);
                    var analysisTask = ctx.AddTask("[Yellow]Analyze files[/]", autoStart: false);

                    // Extract zip to temporary location
                    extractTask.StartTask();
                    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);
                    var step = 0;

                    try
                    {
                        // Extract files manually to preserve timestamps
                        using (var archive = ZipFile.OpenRead(settings.ProgramFile))
                        {
                            extractTask.Value = 1;
                            var count = archive.Entries.Count;
                            step = 99 / count;
                            foreach (var entry in archive.Entries)
                            {
                                var destinationPath = Path.Combine(tempDir, entry.FullName);

                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    // This is a directory entry
                                    Directory.CreateDirectory(destinationPath);
                                }
                                else
                                {
                                    // Ensure directory exists
                                    var dirPath = Path.GetDirectoryName(destinationPath);
                                    if (!string.IsNullOrEmpty(dirPath))
                                    {
                                        Directory.CreateDirectory(dirPath);
                                    }

                                    // Extract file
                                    entry.ExtractToFile(destinationPath, overwrite: true);

                                    // Preserve the timestamp from the archive
                                    File.SetLastWriteTimeUtc(destinationPath, entry.LastWriteTime.UtcDateTime);
                                }

                                extractTask.Value += step;
                            }
                        }

                        extractTask.Value = 100;
                        extractTask.StopTask();

                        List<dynamic> fileChanges;

                        // Library packages (.clz) must always have all files uploaded unless -c flag is explicitly set
                        // Also, if KillProgram is set, skip file comparison and upload everything
                        var isLibraryPackage = extension == ".clz";
                        var uploadAllFiles = settings.KillProgram || isLibraryPackage && !settings.ChangedOnly;

                        if (uploadAllFiles)
                        {
                            if (settings.Verbose)
                            {
                                var reason = settings.KillProgram 
                                    ? "Kill program flag set" 
                                    : "File is a [cyan].clz[/][dim] without --changed-only flag";
                                AnsiConsole.MarkupLine($"[dim]{reason}, skipping file comparison - uploading all files.[/]");
                            }

                            analysisTask.StartTask();
                            analysisTask.Description = "[Yellow]Preparing all files for upload[/]";

                            var localFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

                            fileChanges = localFiles
                                .Select(localFile =>
                                {
                                    var relativePath = Path.GetRelativePath(tempDir, localFile).Replace('\\', '/');
                                    return new
                                    {
                                        LocalPath = localFile,
                                        RelativePath = relativePath,
                                        IsNew = true, // Treat all as new for display purposes
                                        IsChanged = false
                                    };
                                })
                                .Cast<dynamic>()
                                .ToList();

                            analysisTask.Value = 100;
                            analysisTask.StopTask();
                        }
                        else
                        {
                            // Normal file comparison logic
                            remoteTask.StartTask();

                            // Get remote file metadata
                            var remoteFiles = await GetRemoteFileMetadataAsync(sftpClient, remotePath, settings.Verbose);
                            remoteTask.Value = 50;

                            // Download hash manifest if it exists
                            var remoteHashes = await DownloadHashManifestAsync(sftpClient, remotePath, settings.Verbose);
                            remoteTask.Value = 100;
                            remoteTask.StopTask();

                            analysisTask.StartTask();

                            // Compare files and determine which are new vs updated
                            var localFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

                            if (settings.Verbose)
                            {
                                AnsiConsole.MarkupLine($"[dim]Found {localFiles.Length} local files and {remoteFiles.Count} remote files[/]");
                            }

                            step = 100 / localFiles.Length;
                            fileChanges = new List<dynamic>();
                            foreach (var localFile in localFiles)
                            {
                                var relativePath = Path.GetRelativePath(tempDir, localFile).Replace('\\', '/');
                                var isNew = !remoteFiles.ContainsKey(relativePath);

                                // Debug: Show path comparison
                                if (isNew && settings.Verbose)
                                {
                                    AnsiConsole.MarkupLine($"[dim]New file: {relativePath.EscapeMarkup()}[/]");

                                    // Show what keys are available in remoteFiles
                                    if (remoteFiles.Count is > 0)
                                    {
                                        AnsiConsole.MarkupLine($"[dim]  Remote keys: {string.Join(", ", remoteFiles.Keys.Select(k => k.EscapeMarkup()))}[/]");
                                    }
                                }

                                var isChanged = !isNew && await IsFileChangedAsync(localFile, tempDir, remoteFiles, remoteHashes, settings.Verbose);

                                if (isNew || isChanged)
                                {
                                    fileChanges.Add(new
                                    {
                                        LocalPath = localFile,
                                        RelativePath = relativePath,
                                        IsNew = isNew,
                                        IsChanged = isChanged
                                    });
                                }

                                analysisTask.Value += step;
                            }

                            analysisTask.Value = 100;
                            analysisTask.StopTask();
                        }

                        if (settings.Verbose)
                        {
                            ctx.Refresh();
                        }

                        return (tempDir, fileChanges);
                    }
                    catch
                    {
                        // Cleanup on error
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                        throw;
                    }
                });

            var (tempDirectory, changes) = analysisResult;

            try
            {
                if (changes.Count == 0 && !settings.KillProgram)
                {
                    AnsiConsole.MarkupLine("[green]No files have changed. Upload skipped.[/]");
                    sftpClient.Disconnect();
                    return 0;
                }

                var isClz = extension == ".clz";
                var uploadAllFiles = settings.KillProgram || isClz && !settings.ChangedOnly;
                AnsiConsole.MarkupLine($"[yellow]{changes.Count} file(s) {(uploadAllFiles ? "to upload" : "have changed")}.[/]");

                // Now we know we need to upload, establish SSH connection
                using var sshClient = new SshClient(settings.Host, settings.Username, settings.Password);

                await AnsiConsole.Status()
                         .StartAsync("Establishing SSH connection...", async ctx =>
                         {
                             ctx.Status("Connecting to SSH client.");
                             await sshClient.ConnectAsync(cancellationToken);
                             ctx.Status("SSH Connected");
                         });

                using var shellStream = new Ssh.ShellStreamWrapper(sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024));

                if (settings.KillProgram)
                {
                    // Kill program if requested
                    if (!await ConsoleCommands.KillProgramAsync(shellStream, settings.Slot, cancellationToken))
                    {
                        AnsiConsole.MarkupLine("[red]Failed to kill program before uploading files.[/]");
                        return 1;
                    }

                    AnsiConsole.MarkupLine("[green]Program killed successfully.[/]");
                }
                else
                {
                    // Stop program
                    if (!await ConsoleCommands.StopProgramAsync(shellStream, settings.Slot, cancellationToken))
                    {
                        AnsiConsole.MarkupLine("[red]Failed to stop program before uploading files.[/]");
                        return 1;
                    }

                    AnsiConsole.MarkupLine("[green]Program stopped successfully.[/]");
                }

                // Wait 2 seconds before starting uploads
                await Task.Delay(2000, cancellationToken);

                // Upload changed files with individual progress indicators
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .StartAsync(async ctx =>
                    {
                        var initialMaxConcurrency = 8; // You can choose a reasonable default
                        var sessionMaxConcurrency = initialMaxConcurrency;
                        var semaphore = new SemaphoreSlim(initialMaxConcurrency, initialMaxConcurrency);
                        var activeUploads = 0;
                        var failedUploads = new List<(string LocalPath, string RemoteFilePath)>();
                        var uploadTasks = new List<Task>();

                        async Task<bool> UploadFileWithRetry(string localPath, string remoteFilePath, ProgressTask uploadTask)
                        {
                            var retryCount = 0;
                            while (retryCount < 2)
                            {
                                try
                                {
                                    Interlocked.Increment(ref activeUploads);
                                    using var fileStream = File.OpenRead(localPath);
                                    var fileSize = fileStream.Length;

                                    await Task.Run(() =>
                                    {
                                        sftpClient.UploadFile(fileStream, remoteFilePath, true, uploaded =>
                                        {
                                            var percentage = (((double)uploaded) / fileSize) * 100;
                                            uploadTask.Value = percentage;
                                        });
                                    });

                                    var sourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(localPath);
                                    sftpClient.SetLastWriteTimeUtc(remoteFilePath, sourceLastWriteTimeUtc);

                                    uploadTask.Value = 100;
                                    uploadTask.StopTask();
                                    Interlocked.Decrement(ref activeUploads);
                                    return true;
                                }
                                catch
                                {
                                    Interlocked.Decrement(ref activeUploads);
                                    retryCount++;
                                    if (retryCount == 1)
                                    {
                                        // On first failure, set session max concurrency
                                        sessionMaxConcurrency = Math.Max(1, activeUploads);
                                        semaphore = new SemaphoreSlim(sessionMaxConcurrency, sessionMaxConcurrency);
                                    }
                                    if (retryCount >= 2)
                                    {
                                        uploadTask.StopTask();
                                        return false;
                                    }
                                }
                            }

                            return false;
                        }

                        foreach (var fileChange in changes)
                        {
                            var remoteFilePath = $"{remotePath}/{fileChange.RelativePath}";
                            var status = isClz && !settings.ChangedOnly ? string.Empty : fileChange.IsNew ? "[blue](new)[/]" : "[yellow](updated)[/]";
                            var displayName = settings.Verbose ? remoteFilePath : Path.GetFileName(remoteFilePath);
                            var uploadTask = ctx.AddTask($"{status} {displayName}");

                            // Ensure remote directory exists
                            var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace('\\', '/');
                            if (!string.IsNullOrEmpty(remoteDir))
                            {
                                EnsureRemoteDirectoryExists(sftpClient, remoteDir);
                            }

                            await semaphore.WaitAsync();
                            var task = Task.Run(async () =>
                            {
                                var success = await UploadFileWithRetry(fileChange.LocalPath, remoteFilePath, uploadTask);
                                if (!success)
                                {
                                    lock (failedUploads)
                                    {
                                        failedUploads.Add((fileChange.LocalPath, remoteFilePath));
                                    }
                                }
                                semaphore.Release();
                            });
                            uploadTasks.Add(task);

                            // Wait if active uploads >= sessionMaxConcurrency
                            while (activeUploads >= sessionMaxConcurrency)
                            {
                                await Task.Delay(50);
                            }
                        }

                        await Task.WhenAll(uploadTasks);

                        // Optionally, handle/report failed uploads
                        if (failedUploads.Count > 0)
                        {
                            AnsiConsole.MarkupLine($"[red]{failedUploads.Count} file(s) failed to upload after retry.[/]");
                        }
                    });

                // Compute and upload hash manifest for all files
                if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine("[dim]Computing file hashes for manifest...[/]");
                }

                var allLocalFiles = Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories);
                var newHashes = new Dictionary<string, string>();

                foreach (var file in allLocalFiles)
                {
                    var relativePath = Path.GetRelativePath(tempDirectory, file).Replace('\\', '/');
                    var hash = await ComputeFileHashAsync(file);
                    newHashes[relativePath] = hash;
                }

                await UploadHashManifestAsync(sftpClient, remotePath, newHashes, settings.Verbose);

                // Configure IP table BEFORE program registration if not disabled and this is an LPZ program
                List<IpTable.Entry>? ipTableEntries = null;
                if (!settings.NoIpTable && extension == ".lpz")
                {
                    // Look for .dip file in extracted directory
                    var dipFiles = Directory.GetFiles(tempDirectory, "*.dip", SearchOption.TopDirectoryOnly);
                    if (dipFiles.Length > 0)
                    {
                        try
                        {
                            if (settings.Verbose)
                            {
                                AnsiConsole.MarkupLine($"[dim]Found DIP file: {Path.GetFileName(dipFiles[0]).EscapeMarkup()}[/]");
                            }

                            ipTableEntries = IpTable.ParseDipFile(dipFiles[0]);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[yellow]Failed to parse DIP file: {ex.Message.EscapeMarkup()}[/]");
                        }
                    }
                    else if (settings.Verbose)
                    {
                        AnsiConsole.MarkupLine("[dim]No .dip file found in extracted program.[/]");
                    }

                    if (ipTableEntries != null && ipTableEntries.Count > 0)
                    {
                        var ipTableResult = await ConfigureIpTableAsync(shellStream, ipTableEntries, settings.Slot, settings.Verbose, cancellationToken);
                        if (!ipTableResult)
                        {
                            AnsiConsole.MarkupLine("[yellow]IP table configuration had errors, but continuing...[/]");
                        }
                    }
                }

                if (extension is ".lpz" or ".cpz")
                {
                    var registrationResult = await RegisterProgram(shellStream, settings.Slot, extension, tempDirectory, cancellationToken);
                    if (registrationResult != 0)
                    {
                        AnsiConsole.MarkupLine("[red]Program upload failed due to registration error.[/]");
                        return 1;
                    }
                }

                // Upload .zig file if this is an .lpz program and a .sig file exists
                if (extension == ".lpz")
                {
                    var zigFilePath = PrepareSignatureFile(settings.ProgramFile, settings);
                    if (zigFilePath != null)
                    {
                        try
                        {
                            await AnsiConsole.Progress()
                                .AutoClear(false)
                                .StartAsync(async ctx =>
                                {
                                    var zigFileName = Path.GetFileName(zigFilePath);
                                    var remoteZigPath = $"{remotePath}/{zigFileName}";
                                    var zigDisplayName = settings.Verbose ? remoteZigPath : zigFileName;
                                    var zigUploadTask = ctx.AddTask($"[blue]Uploading signature to {zigDisplayName}[/]");

                                    using var zigFileStream = File.OpenRead(zigFilePath);
                                    var zigFileSize = zigFileStream.Length;

                                    await Task.Run(() =>
                                    {
                                        sftpClient.UploadFile(zigFileStream, remoteZigPath, uploaded =>
                                        {
                                            var percentage = (((double)uploaded) / zigFileSize) * 100;
                                            zigUploadTask.Value = percentage;
                                        });
                                    });

                                    zigUploadTask.Value = 100;
                                    zigUploadTask.StopTask();
                                });
                        }
                        finally
                        {
                            // Clean up temporary .zig file
                            if (File.Exists(zigFilePath))
                            {
                                try
                                {
                                    File.Delete(zigFilePath);
                                }
                                catch
                                {
                                    // Ignore cleanup errors
                                }
                            }
                        }
                    }
                }

                if (!settings.DoNotStart)
                {
                    // Execute progres command
                    var progresSuccess = await ConsoleCommands.RestartProgramAsync(shellStream, settings.Slot, cancellationToken);

                    if (progresSuccess)
                    {
                        AnsiConsole.MarkupLine("[green]Program updated successfully.[/]");
                        return 0;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]Program failed to restart.[/]");
                        return 1;
                    }
                }

                return 0;
            }
            finally
            {
                // Cleanup temp directory
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        /// <summary>
        /// Uploads a hash manifest file to the remote device.
        /// </summary>
        /// <param name="sftpClient">Connected SFTP client.</param>
        /// <param name="remotePath">Remote program directory path.</param>
        /// <param name="hashes">Dictionary of file path to hash mappings.</param>
        /// <param name="verbose">Whether to emit verbose diagnostic output.</param>
        private static async Task UploadHashManifestAsync(
            ISftpClient sftpClient,
            string remotePath,
            Dictionary<string, string> hashes,
            bool verbose = false)
        {
            var remoteHashPath = $"{remotePath}/{HashManifestFileName}";

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Uploading hash manifest with {hashes.Count} entries to '{remoteHashPath.EscapeMarkup()}'[/]");
            }

            await Task.Run(() =>
            {
                using var memoryStream = new MemoryStream();
                using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
                {
                    foreach (var kvp in hashes.OrderBy(k => k.Key))
                    {
                        writer.WriteLine($"{kvp.Key}={kvp.Value}");
                    }
                    writer.Flush();
                }

                memoryStream.Position = 0;
                sftpClient.UploadFile(memoryStream, remoteHashPath, true);
            });

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Hash manifest uploaded successfully[/]");
            }
        }

        /// <summary>
        /// Uploads a full program package file to the remote device and executes the necessary console commands to load it.
        /// </summary>
        /// <param name="settings">Upload settings containing host, credentials and flags.</param>
        /// <param name="remotePath">Remote program directory path on the device.</param>
        /// <param name="cancellationToken">Cancellation token to observe during network and file operations.</param>
        /// <returns>Exit code 0 on success; non-zero on failure.</returns>
        private static async Task<int> UploadProgramFileAsync(
            ProgramUploadSettings settings,
            string remotePath,
            CancellationToken cancellationToken)
        {
            var sshClient = await SshManager.GetSshClientAsync(settings.Host, settings.Username, settings.Password, cancellationToken);
            var sftpClient = await SshManager.GetSftpClientAsync(settings.Host, settings.Username, settings.Password);

            if (!sshClient.IsConnected || !sftpClient.IsConnected)
            {
                await AnsiConsole.Status()
                   .StartAsync("Connecting to device...", async ctx =>
                   {
                       if (!sshClient.IsConnected)
                       {
                           ctx.Status("Connecting to SSH client.");
                           await sshClient.ConnectAsync(cancellationToken);
                       }

                       if (!sftpClient.IsConnected)
                       {
                           ctx.Status("Connecting to SFTP client.");
                           await sftpClient.ConnectAsync(cancellationToken);
                       }

                       ctx.Status("Connected");
                   });
            }

            var shellStream = await SshManager.GetShellStreamAsync(settings.Host, settings.Username, settings.Password, cancellationToken);

            // Kill program if requested
            if (settings.KillProgram)
            {
                if (!await ConsoleCommands.KillProgramAsync(shellStream, settings.Slot, cancellationToken))
                {
                    AnsiConsole.MarkupLine("[red]Error: Failed to kill program before uploading.[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine("[green]Program killed successfully.[/]");

                // Wait 2 seconds before uploading
                await Task.Delay(2000, cancellationToken);
            }

            // Check for signature file if this is an .lpz program
            string? zigFilePath = null;
            var extension = Path.GetExtension(settings.ProgramFile).ToLowerInvariant();
            if (extension == ".lpz")
            {
                zigFilePath = PrepareSignatureFile(settings.ProgramFile, settings);
            }

            // Embed hash manifest directly into the archive
            try
            {
                if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine("[dim]Computing file hashes and embedding manifest in archive...[/]");
                }

                // Compute hashes directly from archive entries
                var fileHashes = new Dictionary<string, string>();

                using (var archive = ZipFile.Open(settings.ProgramFile, ZipArchiveMode.Update))
                {
                    using var sha256 = SHA256.Create();

                    foreach (var entry in archive.Entries)
                    {
                        // Skip directory entries
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            continue;
                        }

                        // Compute hash from stream
                        using var entryStream = entry.Open();
                        var hashBytes = await sha256.ComputeHashAsync(entryStream, cancellationToken);
                        var hash = Convert.ToHexString(hashBytes);

                        var relativePath = entry.FullName.Replace('\\', '/');
                        fileHashes[relativePath] = hash;
                    }

                    if (settings.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Computed hashes for {fileHashes.Count} files[/]");
                    }

                    // Add hash manifest directly to the existing archive
                    // Remove existing hash manifest if present
                    var existingManifest = archive.GetEntry(HashManifestFileName);
                    if (existingManifest == null)
                    {
                        // Add new hash manifest
                        var hashManifestEntry = archive.CreateEntry(HashManifestFileName, CompressionLevel.Optimal);
                        hashManifestEntry.ExternalAttributes = 0;
                        using (var manifestStream = hashManifestEntry.Open())
                        {
                            using (var writer = new StreamWriter(manifestStream, Encoding.UTF8))
                            {
                                foreach (var kvp in fileHashes.OrderBy(k => k.Key))
                                {
                                    if (settings.Verbose)
                                    {
                                        AnsiConsole.MarkupLine($"[dim]Manifest entry: {kvp.Key.EscapeMarkup()} = {kvp.Value.EscapeMarkup()}[/]");
                                    }

                                    await writer.WriteLineAsync($"{kvp.Key}={kvp.Value}");
                                }

                                // Ensure the stream is flushed and has content
                                await writer.FlushAsync();
                            }
                        }

                        hashManifestEntry.LastWriteTime = DateTimeOffset.UtcNow;
                    }

                    if (settings.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Embedded hash manifest in archive[/]");
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Failed to embed hash manifest in archive: {ex.Message.EscapeMarkup()}[/]");
                AnsiConsole.MarkupLine("[yellow]Continuing with upload...[/]");
            }

            // Read DIP file for IP table configuration if this is an LPZ
            List<IpTable.Entry>? ipTableEntries = null;
            if (!settings.NoIpTable && extension == ".lpz")
            {
                try
                {
                    // Read DIP file directly from the archive
                    using (var archive = ZipFile.OpenRead(settings.ProgramFile))
                    {
                        var dipName = $"{Path.GetFileNameWithoutExtension(settings.ProgramFile)}.dip";
                        var dipEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals(dipName, StringComparison.OrdinalIgnoreCase));

                        if (dipEntry != null)
                        {
                            if (settings.Verbose)
                            {
                                AnsiConsole.MarkupLine($"[dim]Found DIP file in archive: {dipEntry.Name.EscapeMarkup()}[/]");
                            }

                            using var dipStream = dipEntry.Open();
                            ipTableEntries = IpTable.ParseDipStream(dipStream);
                        }
                        else if (settings.Verbose)
                        {
                            AnsiConsole.MarkupLine("[dim]No .dip file found in program archive.[/]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Failed to read/parse DIP file from archive: {ex.Message.EscapeMarkup()}[/]");
                }
            }

            try
            {
                var result = await AnsiConsole.Progress()
                    .AutoClear(false)
                    .StartAsync(async ctx =>
                    {
                        var fileName = Path.GetFileName(settings.ProgramFile);
                        var remoteFilePath = $"{remotePath}/{fileName}";
                        var displayName = settings.Verbose ? remoteFilePath : fileName;
                        var uploadTask = ctx.AddTask($"[green]Uploading to {displayName}[/]");

                        // Ensure remote directory exists
                        EnsureRemoteDirectoryExists(sftpClient, remotePath);

                        using var fileStream = File.OpenRead(settings.ProgramFile);
                        var fileSize = fileStream.Length;

                        await Task.Run(() =>
                        {
                            sftpClient.UploadFile(fileStream, remoteFilePath, uploaded =>
                            {
                                var percentage = (((double)uploaded) / fileSize) * 100;
                                uploadTask.Value = percentage;
                            });
                        });

                        uploadTask.Value = 100;
                        uploadTask.StopTask();

                        // Upload .zig file if it was created
                        if (zigFilePath != null)
                        {
                            var zigFileName = Path.GetFileName(zigFilePath);
                            var remoteZigPath = $"{remotePath}/{zigFileName}";
                            var zigDisplayName = settings.Verbose ? remoteZigPath : zigFileName;
                            var zigUploadTask = ctx.AddTask($"[blue]Uploading signature to {zigDisplayName}[/]");

                            using var zigFileStream = File.OpenRead(zigFilePath);
                            var zigFileSize = zigFileStream.Length;

                            await Task.Run(() =>
                            {
                                sftpClient.UploadFile(zigFileStream, remoteZigPath, uploaded =>
                                {
                                    var percentage = (((double)uploaded) / zigFileSize) * 100;
                                    zigUploadTask.Value = percentage;
                                });
                            });

                            zigUploadTask.Value = 100;
                            zigUploadTask.StopTask();
                        }

                        return 0;
                    });

                // Output after progress indicators are complete
                AnsiConsole.MarkupLine("[green]Upload complete![/]");

                // Configure IP table BEFORE loading the program
                if (!settings.NoIpTable && ipTableEntries != null && ipTableEntries.Count > 0 && extension == ".lpz")
                {
                    var ipTableResult = await ConfigureIpTableAsync(shellStream, ipTableEntries, settings.Slot, settings.Verbose, cancellationToken);
                    if (!ipTableResult && settings.Verbose)
                    {
                        AnsiConsole.MarkupLine("[yellow]Warning: IP table configuration had errors, but continuing...[/]");
                    }
                }

                result = await AnsiConsole.Status().Spinner(Spinner.Known.BouncingBall).Start("Loading program...", async ctx =>
                {
                    // Execute progload command.
                    if (!await ConsoleCommands.ProgramLoadAsync(shellStream, settings.Slot, settings.DoNotStart, cancellationToken))
                    {
                        AnsiConsole.MarkupLine("[red]Error: Failed to load program after upload.[/]");
                        return 1;
                    }

                    return 0;
                });

                return result;
            }
            finally
            {
                // Clean up temporary .zig file
                if (zigFilePath != null && File.Exists(zigFilePath))
                {
                    try
                    {
                        File.Delete(zigFilePath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
    }
}