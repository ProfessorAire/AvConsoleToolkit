// <copyright file="ProgramUploadCommand.cs" company="AVI-SPL Global LLC.">
// Copyright (C) AVI-SPL Global LLC. All Rights Reserved.
// The intellectual and technical concepts contained herein are proprietary to AVI-SPL Global LLC. and subject to AVI-SPL's standard software license agreement.
// These materials may not be copied, reproduced, distributed or disclosed, in whole or in part, in any way without the written permission of an authorized
// representative of AVI-SPL. All references to AVI-SPL Global LLC. shall also be references to AVI-SPL Global LLC's affiliates.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ConsoleToolkit.Crestron;
using ConsoleToolkit.Ssh;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands.Program
{
    public sealed class ProgramUploadCommand : AsyncCommand<ProgramUploadSettings>
    {
        private static readonly string[] SupportedExtensions = { ".cpz", ".clz", ".lpz" };

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

                if (settings.Slot < 1 || settings.Slot > 10)
                {
                    AnsiConsole.MarkupLine("[red]Error: Slot must be between 1 and 10.[/]");
                    return 1;
                }

                var remotePath = $"program{settings.Slot:D2}";

                int result = -1;
                if (settings.ChangedOnly)
                {
                    result = await this.UploadChangedFilesAsync(settings, remotePath, extension, cancellationToken);
                }
                else
                {
                    result = await this.UploadProgramFileAsync(settings, remotePath, cancellationToken);
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
                AnsiConsole.MarkupLine($"\r\n[red]Error:[/] {ex.Message}");
                return 1;
            }
        }

        private void EnsureRemoteDirectoryExists(ISftpClient sftpClient, string remotePath)
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

        private async Task<Dictionary<string, ISftpFile>> GetRemoteFileMetadataAsync(
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

                       this.GetRemoteFilesRecursive(sftpClient, remotePath, remotePath, files, verbose);
                       return files;
                   });
        }

        private void GetRemoteFilesRecursive(
            ISftpClient sftpClient,
            string currentPath,
            string basePath,
            Dictionary<string, ISftpFile> files,
            bool verbose = false)
        {
            var items = sftpClient.ListDirectory(currentPath);

            foreach (var item in items)
            {
                if (item.Name == "." || item.Name == "..")
                {
                    continue;
                }

                if (item.IsDirectory)
                {
                    this.GetRemoteFilesRecursive(sftpClient, item.FullName, basePath, files, verbose);
                }
                else
                {
                    // Calculate relative path correctly - ensure we handle the leading slash
                    var fullPath = item.FullName;
                    var relativeStart = basePath.StartsWith('/') ? basePath.Length : basePath.Length + 1;
                    var relativePath = fullPath.Length > relativeStart ? fullPath.Substring(relativeStart).TrimStart('/') : string.Empty;

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Adding remote file: '{relativePath.EscapeMarkup()}' (full: '{item.FullName.EscapeMarkup()}', base: '{basePath.EscapeMarkup()}', startIndex: {relativeStart})[/]");
                    }

                    files[relativePath] = item;
                }
            }
        }

        private bool IsFileChanged(
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

        private async Task<int> RegisterProgram(IShellStream shellStream, int slot, string extension, string tempDirectory, CancellationToken cancellationToken)
        {
            var success = false;
            if (extension == ".lpz")
            {
                success = await CommandHandlers.RegisterProgramAsync(shellStream, slot, cancellationToken);
            }
            else if (extension == ".cpz")
            {
                AnsiConsole.MarkupLine("[cyan]Registering .cpz program...[/]");

                var result = await this.TryGetMainAssemblyNameAsync(tempDirectory);
                if (!result.Success)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Could not determine main assembly name from manifest.info or ProgramInfo.config.");
                    return 1;
                }

                success = await CommandHandlers.RegisterProgramAsync(shellStream, slot, result.MainAssemblyName, cancellationToken);
            }

            return success ? 0 : 1;
        }

        private async Task<(bool Success, string? MainAssemblyName)> TryGetMainAssemblyNameAsync(string tempDirectory)
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
                            assemblyPart = assemblyPart.Substring(0, colonIndex);
                        }
                        if (assemblyPart.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            mainAssemblyName = assemblyPart.Substring(0, assemblyPart.Length - 4);
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

        private async Task<int> UploadChangedFilesAsync(
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
                        var remoteFiles = await this.GetRemoteFileMetadataAsync(sftpClient, remotePath, settings.Verbose);
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
                   if (remoteFiles.Count > 0 && remoteFiles.Count < 10)
                   {
                       AnsiConsole.MarkupLine($"[dim]  Remote keys: {string.Join(", ", remoteFiles.Keys.Select(k => k.EscapeMarkup()))}[/]");
                   }
               }

               var isChanged = !isNew && this.IsFileChanged(localFile, tempDir, remoteFiles, settings.Verbose);
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

                using var shellStream = new ShellStreamWrapper(sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024));

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
                await Task.Delay(2000);

                // Upload changed files with individual progress indicators
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .StartAsync(async ctx =>
                    {
                        var initialMaxConcurrency = 4; // You can choose a reasonable default
                        int sessionMaxConcurrency = initialMaxConcurrency;
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
                            string displayName = settings.Verbose ? remoteFilePath : Path.GetFileName(remoteFilePath);
                            var uploadTask = ctx.AddTask($"{status} {displayName}");

                            // Ensure remote directory exists
                            var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace('\\', '/');
                            if (!string.IsNullOrEmpty(remoteDir))
                            {
                                this.EnsureRemoteDirectoryExists(sftpClient, remoteDir);
                            }

                            await semaphore.WaitAsync();
                            var task = Task.Run(async () =>
                            {
                                bool success = await UploadFileWithRetry(fileChange.LocalPath, remoteFilePath, uploadTask);
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

                // Output after all progress indicators are complete
                // TODO: Register program (different for .lpz vs .cpz, .clz assumed already registered)
                if (extension == ".lpz" || extension == ".cpz")
                {
                    var registrationResult = await this.RegisterProgram(shellStream, settings.Slot, extension, tempDirectory, cancellationToken);
                    if (registrationResult != 0)
                    {
                        AnsiConsole.MarkupLine("[red]Program upload failed due to registration error.[/]");
                        return 1;
                    }
                }

                // Execute progres command
                var progresSuccess = await CommandHandlers.RestartProgramAsync(shellStream, settings.Slot, cancellationToken);

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

        private async Task<int> UploadProgramFileAsync(
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

            using var shellStream = sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024);

            // Kill program if requested
            if (settings.KillProgram)
            {
                var killCommand = $"killprog -P:{settings.Slot}";
                AnsiConsole.MarkupLine($"[yellow]Executing:[/] {killCommand}");

                shellStream.WriteLine(killCommand);
                AnsiConsole.MarkupLine("[cyan]Waiting for program to be killed...[/]");

                var (killSuccess, killOutput) = await this.WaitForCommandCompletionAsync(
           shellStream,
      [$"Specified program {settings.Slot} successfully deleted"],
    [], 10000
      );

                if (!killSuccess)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Failed to kill program before uploading.");
                    return 1;
                }

                AnsiConsole.MarkupLine("[green]Program killed successfully.[/]");

                // Wait 2 seconds before uploading
                await Task.Delay(2000);
            }

            var result = await AnsiConsole.Progress()
                .AutoClear(false)
                .StartAsync(async ctx =>
                {
                    var fileName = Path.GetFileName(settings.ProgramFile);
                    var remoteFilePath = $"{remotePath}/{fileName}";
                    string displayName = settings.Verbose ? remoteFilePath : fileName;
                    var uploadTask = ctx.AddTask($"[green]Uploading to {displayName}[/]");

                    // Ensure remote directory exists
                    this.EnsureRemoteDirectoryExists(sftpClient, remotePath);

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

            // Execute progload command
            var progloadCommand = $"progload -p:{settings.Slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {progloadCommand}");

            shellStream.WriteLine(progloadCommand);
            var (success, output) = await this.WaitForCommandCompletionAsync(shellStream, ["Program Start successfully sent for App"], null);

            return result;
        }

        private async Task<(bool Success, string Output)> WaitForCommandCompletionAsync(
            ShellStream shellStream,
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

    public sealed class ProgramUploadSettings : CommandSettings
    {
        [CommandOption("-c|--changed-only")]
        public bool ChangedOnly { get; set; }

        [CommandOption("-h|--host")]
        public string Host { get; set; } = string.Empty;

        [CommandOption("-k|--kill")]
        public bool KillProgram { get; set; }

        [CommandOption("-p|--password")]
        public string Password { get; set; } = string.Empty;

        [CommandArgument(0, "<PROGRAM>")]
        public string ProgramFile { get; set; } = string.Empty;

        [CommandOption("-s|--slot")]
        public int Slot { get; set; }

        [CommandOption("-u|--username")]
        public string Username { get; set; } = string.Empty;

        [CommandOption("-v|--verbose")]
        public bool Verbose { get; set; }
    }
}