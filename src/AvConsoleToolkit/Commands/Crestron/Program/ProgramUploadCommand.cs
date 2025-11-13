// <copyright file="ProgramUploadCommand.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”),
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
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
                        AnsiConsole.MarkupLine("[fuschia]No username/password provided, looking up values from address books.[/]");
                    }

                    var entry = await AvConsoleToolkit.Crestron.ToolboxAddressBook.LookupEntryAsync(settings.Host);
                    if (entry is null)
                    {
                        if (settings.Verbose)
                        {
                            AnsiConsole.MarkupLine("[red]Error: Could not find device in address books and no username/password provided.[/]");
                        }

                        return 101;
                    }

                    if (entry.Username is null || entry.Password is null)
                    {
                        if (settings.Verbose)
                        {
                            AnsiConsole.MarkupLine("[red]Error: Address book entry is missing username or password.[/]");
                        }

                        return 102;
                    }

                    settings.Username = entry.Username;
                    settings.Password = entry.Password;
                }
                ;

                var remotePath = $"program{settings.Slot:D2}";

                AnsiConsole.MarkupLineInterpolated($"[teal]Uploading program to slot {settings.Slot} on device '{settings.Host}'...[/]");

                var result = -1;
                if (settings.ChangedOnly)
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
                AnsiConsole.MarkupLine($"\r\n[red]Error: {ex.Message}[/]");
                return 1;
            }
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
        /// Determines whether a local file differs from the corresponding remote file based on last-write timestamps.
        /// </summary>
        /// <param name="localFile">Full path to the local file.</param>
        /// <param name="localBasePath">Local base directory used to compute the relative path.</param>
        /// <param name="remoteFiles">Dictionary of remote file metadata keyed by relative path.</param>
        /// <param name="verbose">Whether to emit verbose diagnostic output.</param>
        /// <returns>True when the file is new or its timestamp indicates it has changed relative to remote.</returns>
        private static bool IsFileChanged(
            string localFile,
            string localBasePath,
            Dictionary<string, ISftpFile> remoteFiles,
            bool verbose = false)
        {
            var relativePath = Path.GetRelativePath(localBasePath, localFile).Replace('\\', '/');

            if (!remoteFiles.TryGetValue(relativePath, out var remoteFile))
            {
                // File doesn't exist remotely, so it's new/changed
                return true;
            }

            var localLastWriteTime = File.GetLastWriteTimeUtc(localFile);
            var remoteLastWriteTime = remoteFile.LastWriteTimeUtc;

            var timeDifference = Math.Abs((localLastWriteTime - remoteLastWriteTime).TotalSeconds);

            if (verbose)
            {
                // Debug output
                AnsiConsole.MarkupLine($"[dim]Comparing {relativePath.EscapeMarkup()}:[/]");
                AnsiConsole.MarkupLine($"[dim]  Local:  {localLastWriteTime:yyyy-MM-dd HH:mm:ss.fff} UTC[/]");
                AnsiConsole.MarkupLine($"[dim]  Remote: {remoteLastWriteTime:yyyy-MM-dd HH:mm:ss.fff} UTC[/]");
                AnsiConsole.MarkupLine($"[dim]  Diff:   {timeDifference:F3} seconds[/]");
            }

            // Compare timestamps (allowing for small differences due to file system precision)
            var hasChanged = timeDifference > 2;

            if (verbose)
            {
                AnsiConsole.MarkupLine($"[dim]  Changed: {hasChanged}[/]");
            }

            return hasChanged;
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
                success = await AvConsoleToolkit.Crestron.ConsoleCommands.RegisterProgramAsync(shellStream, slot, cancellationToken);
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

                success = await AvConsoleToolkit.Crestron.ConsoleCommands.RegisterProgramAsync(shellStream, slot, mainAssemblyName, cancellationToken);
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
                            mainAssemblyName = assemblyPart[..(assemblyPart.Length - 4)];
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
            using var sftpClient = new SftpClient(settings.Host, settings.Username, settings.Password);

            await AnsiConsole.Status()
       .StartAsync("Connecting to device...", async ctx =>
       {
           ctx.Status("Connecting to SFTP client for file analysis.");
           await sftpClient.ConnectAsync(cancellationToken);
           ctx.Status("Connected");
       });

            var analysisResult = await AnsiConsole.Progress()
                .AutoClear(false)
                .StartAsync(async ctx =>
                {
                    var analysisTask = ctx.AddTask("[green]Analyzing files[/]");

                    // Extract zip to temporary location
                    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        // Extract files manually to preserve timestamps
                        using (var archive = ZipFile.OpenRead(settings.ProgramFile))
                        {
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
                            }
                        }

                        analysisTask.Value = 50;

                        // Get remote file metadata
                        var remoteFiles = await GetRemoteFileMetadataAsync(sftpClient, remotePath, settings.Verbose);
                        analysisTask.Value = 75;

                        // Compare files and determine which are new vs updated
                        var localFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

                        if (settings.Verbose)
                        {
                            AnsiConsole.MarkupLine($"[dim]Found {localFiles.Length} local files and {remoteFiles.Count} remote files[/]");
                        }

                        var fileChanges = localFiles
                           .Select(localFile =>
                           {
                               var relativePath = Path.GetRelativePath(tempDir, localFile).Replace('\\', '/');
                               var isNew = !remoteFiles.ContainsKey(relativePath);

                               // Debug: Show path comparison
                               if (isNew && settings.Verbose)
                               {
                                   AnsiConsole.MarkupLine($"[dim]New file: {relativePath.EscapeMarkup()}[/]");

                                   // Show what keys are available in remoteFiles
                                   if (remoteFiles.Count is > 0 and < 10)
                                   {
                                       AnsiConsole.MarkupLine($"[dim]  Remote keys: {string.Join(", ", remoteFiles.Keys.Select(k => k.EscapeMarkup()))}[/]");
                                   }
                               }

                               var isChanged = !isNew && IsFileChanged(localFile, tempDir, remoteFiles, settings.Verbose);
                               return new
                               {
                                   LocalPath = localFile,
                                   RelativePath = relativePath,
                                   IsNew = isNew,
                                   IsChanged = isChanged
                               };
                           })
                          .Where(f => f.IsNew || f.IsChanged)
                          .ToList();

                        analysisTask.Value = 100;
                        analysisTask.StopTask();

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
                if (changes.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]No files have changed. Upload skipped.[/]");
                    sftpClient.Disconnect();
                    return 0;
                }

                AnsiConsole.MarkupLine($"[yellow]{changes.Count} file(s) have changed.[/]");

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

                // Stop or kill program based on flag
                string stopCommand;
                string[] successPatterns;

                if (settings.KillProgram)
                {
                    stopCommand = $"killprog -P:{settings.Slot}";
                    successPatterns = [$"Specified program {settings.Slot} successfully deleted"];
                    AnsiConsole.MarkupLine($"[yellow]Executing:[/] {stopCommand}");
                    shellStream.WriteLine(stopCommand);
                    AnsiConsole.MarkupLine("[cyan]Waiting for program to be killed...[/]");
                }
                else
                {
                    stopCommand = $"stopprog -p:{settings.Slot}";
                    successPatterns = ["Program Stopped", "** Specified App does not exist **"];
                    AnsiConsole.MarkupLine($"[yellow]Executing:[/] {stopCommand}");
                    shellStream.WriteLine(stopCommand);
                    AnsiConsole.MarkupLine("[cyan]Waiting for program to stop...[/]");
                }

                var success = await shellStream.WaitForCommandCompletionAsync(successPatterns, [], cancellationToken);

                if (!success)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Failed to {(settings.KillProgram ? "kill" : "stop")} program before uploading files.");
                    return 1;
                }

                AnsiConsole.MarkupLine($"[green]Program {(settings.KillProgram ? "killed" : "stopped")} successfully.[/]");

                // Wait 2 seconds before starting uploads
                await Task.Delay(2000, cancellationToken);

                // Upload changed files with individual progress indicators
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .StartAsync(async ctx =>
                    {
                        var initialMaxConcurrency = 4; // You can choose a reasonable default
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
                            var status = fileChange.IsNew ? "[blue](new)[/]" : "[yellow](updated)[/]";
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

                if (extension is ".lpz" or ".cpz")
                {
                    var registrationResult = await RegisterProgram(shellStream, settings.Slot, extension, tempDirectory, cancellationToken);
                    if (registrationResult != 0)
                    {
                        AnsiConsole.MarkupLine("[red]Program upload failed due to registration error.[/]");
                        return 1;
                    }
                }

                // Execute progres command
                var progresSuccess = await AvConsoleToolkit.Crestron.ConsoleCommands.RestartProgramAsync(shellStream, settings.Slot, cancellationToken);

                if (progresSuccess)
                {
                    AnsiConsole.MarkupLine("[green]Program updated successfully.[/]");
                    return 0;
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Program failed to restart.");
                    return 1;
                }
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
            using var sshClient = new SshClient(settings.Host, settings.Username, settings.Password);
            using var sftpClient = new SftpClient(settings.Host, settings.Username, settings.Password);

            await AnsiConsole.Status()
               .StartAsync("Connecting to device...", async ctx =>
               {
                   ctx.Status("Connecting to SSH client.");
                   await sshClient.ConnectAsync(cancellationToken);
                   ctx.Status("Connecting to SFTP client.");
                   await sftpClient.ConnectAsync(cancellationToken);
                   ctx.Status("Connected");
               });

            using var shellStream = new Ssh.ShellStreamWrapper(sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024));

            // Kill program if requested
            if (settings.KillProgram)
            {
                var killCommand = $"killprog -P:{settings.Slot}";
                AnsiConsole.MarkupLine($"[yellow]Executing:[/] {killCommand}");

                shellStream.WriteLine(killCommand);
                AnsiConsole.MarkupLine("[cyan]Waiting for program to be killed...[/]");

                var killSuccess = await shellStream.WaitForCommandCompletionAsync([$"Specified program {settings.Slot} successfully deleted"], [], cancellationToken, 10000);

                if (!killSuccess)
                {
                    AnsiConsole.MarkupLine("[red]Error: Failed to kill program before uploading.[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine("[green]Program killed successfully.[/]");

                // Wait 2 seconds before uploading
                await Task.Delay(2000, cancellationToken);
            }

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

                    return 0;
                });


            // Output after progress indicators are complete
            AnsiConsole.MarkupLine("[green]Upload complete![/]");

            result = await AnsiConsole.Status().Spinner(Spinner.Known.BouncingBall).Start("Loading program...", async ctx =>
            {
                // Execute progload command.
                if (!await AvConsoleToolkit.Crestron.ConsoleCommands.ProgramLoadAsync(shellStream, settings.Slot, cancellationToken))
                {
                    AnsiConsole.MarkupLine("[red]Error: Failed to load program after upload.[/]");
                    return 1;
                }

                return 0;
            });

            return result;
        }

        /// <summary>
        /// Waits for a console command to complete by reading output from the provided <see cref="ShellStream"/>.
        /// Scans output for success or failure patterns and returns once one is matched or when the timeout elapses.
        /// </summary>
        /// <param name="shellStream">The shell stream used to read console output.</param>
        /// <param name="successPatterns">Patterns that indicate success.</param>
        /// <param name="failurePatterns">Patterns that indicate failure.</param>
        /// <param name="timeoutMs">Timeout in milliseconds to wait for a matching pattern. Defaults to 30000ms.</param>
        /// <returns>A tuple containing a boolean success flag and the captured output string.</returns>
        private static async Task<(bool Success, string Output)> WaitForCommandCompletionAsync(
            ShellStreamWrapper shellStream,
            IEnumerable<string>? successPatterns,
            IEnumerable<string>? failurePatterns,
            int timeoutMs = 30000)
        {
            var output = new StringBuilder();
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                if (shellStream.DataAvailable)
                {
                    var data = shellStream.Read();
                    output.Append(data);

                    // Print output as it's received
                    AnsiConsole.Write(data);

                    var currentOutput = output.ToString();

                    // Check for failure patterns first
                    if (failurePatterns != null)
                    {
                        foreach (var pattern in failurePatterns)
                        {
                            if (currentOutput.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                return (false, currentOutput);
                            }
                        }
                    }

                    // Check for success patterns
                    if (successPatterns != null)
                    {
                        foreach (var pattern in successPatterns)
                        {
                            if (currentOutput.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                return (true, currentOutput);
                            }
                        }
                    }
                }

                await Task.Delay(100);
            }

            // Timeout - that's a failure
            return (false, output.ToString());
        }
    }
}