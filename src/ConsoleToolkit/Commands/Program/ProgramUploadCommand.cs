using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleToolkit.Commands.Program;

public sealed class ProgramUploadSettings : CommandSettings
{
    [CommandArgument(0, "<PROGRAM>")]
    public string ProgramFile { get; set; } = string.Empty;

    [CommandOption("-s|--slot")]
    public int Slot { get; set; }

    [CommandOption("-h|--host")]
    public string Host { get; set; } = string.Empty;

    [CommandOption("-u|--username")]
    public string Username { get; set; } = string.Empty;

    [CommandOption("-p|--password")]
    public string Password { get; set; } = string.Empty;

    [CommandOption("-c|--changed-only")]
    public bool ChangedOnly { get; set; }
}

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
                AnsiConsole.MarkupLine($"[red]Error:[/] Unsupported file extension. Supported: {string.Join(", ", SupportedExtensions)}");
                return 1;
            }

            if (settings.Slot < 1 || settings.Slot > 10)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Slot must be between 1 and 10.");
                return 1;
            }

            var remotePath = $"program{settings.Slot:D2}";

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

            int result = -1;
            if (settings.ChangedOnly)
            {
                result = await this.UploadChangedFilesAsync(shellStream, sftpClient, settings, remotePath, extension);
            }
            else
            {
                result = await this.UploadProgramFileAsync(sftpClient, shellStream, settings, remotePath);
            }

            if (result == 0)
            {
                AnsiConsole.MarkupLine("[green]The command: 'program upload' completed.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]The command: 'program upload' failed.[/]");
            }

            return result;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private async Task<int> UploadProgramFileAsync(
        ISftpClient sftpClient,
        ShellStream shellStream,
        ProgramUploadSettings settings,
        string remotePath)
    {
        var result = await AnsiConsole.Progress()
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                var fileName = Path.GetFileName(settings.ProgramFile);
                var remoteFilePath = $"{remotePath}/{fileName}";
                
                var uploadTask = ctx.AddTask($"[green]Uploading to {remoteFilePath}[/]");

                // Ensure remote directory exists
                EnsureRemoteDirectoryExists(sftpClient, remotePath);

                using var fileStream = File.OpenRead(settings.ProgramFile);
                var fileSize = fileStream.Length;

                await Task.Run(() =>
                {
                    sftpClient.UploadFile(fileStream, remoteFilePath, uploaded =>
                    {
                        var percentage = (double)uploaded / fileSize * 100;
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

    private async Task<int> UploadChangedFilesAsync(
        ShellStream shellStream,
        ISftpClient sftpClient,
        ProgramUploadSettings settings,
        string remotePath,
        string extension)
    {
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
                    ZipFile.ExtractToDirectory(settings.ProgramFile, tempDir);
                    analysisTask.Value = 50;

                    // Get remote file metadata
                    var remoteFiles = await GetRemoteFileMetadataAsync(sftpClient, remotePath);
                    analysisTask.Value = 75;

                    // Compare files and determine which are new vs updated
                    var localFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                    var fileChanges = localFiles
                        .Select(localFile =>
                        {
                            var relativePath = Path.GetRelativePath(tempDir, localFile).Replace('\\', '/');
                            var isNew = !remoteFiles.ContainsKey(relativePath);
                            var isChanged = !isNew && IsFileChanged(localFile, tempDir, remoteFiles);
                            return new { LocalPath = localFile, RelativePath = relativePath, IsNew = isNew, IsChanged = isChanged };
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
                return 0;
            }

            AnsiConsole.MarkupLine($"[yellow]{changes.Count} file(s) have changed.[/]");

            // Stop program
            var stopCommand = $"stopprog -p:{settings.Slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {stopCommand}");
            
            shellStream.WriteLine(stopCommand);
            AnsiConsole.MarkupLine("[cyan]Waiting for program to stop...[/]");
            
            var (success, stopOutput) = await this.WaitForCommandCompletionAsync(
                shellStream, 
                ["Program Stopped"], 
                ["Failed to stop program"]
            );
            
            if (!success)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Failed to stop program before uploading files.");
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Program stopped successfully.[/]");

            // Upload changed files with individual progress indicators
            await AnsiConsole.Progress()
                .AutoClear(false)
                .StartAsync(async ctx =>
                {
                    foreach (var fileChange in changes)
                    {
                        var remoteFilePath = $"{remotePath}/{fileChange.RelativePath}";
                        var status = fileChange.IsNew ? "[blue](new)[/]" : "[yellow](updated)[/]";
                        var uploadTask = ctx.AddTask($"{status} {remoteFilePath}");

                        // Ensure remote directory exists
                        var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(remoteDir))
                        {
                            EnsureRemoteDirectoryExists(sftpClient, remoteDir);
                        }

                        using var fileStream = File.OpenRead(fileChange.LocalPath);
                        var fileSize = fileStream.Length;

                        await Task.Run(() => 
                        {
                            sftpClient.UploadFile(fileStream, remoteFilePath, true, uploaded =>
                            {
                                var percentage = (double)uploaded / fileSize * 100;
                                uploadTask.Value = percentage;
                            });
                        });

                        uploadTask.Value = 100;
                        uploadTask.StopTask();
                    }
                });

            // Output after all progress indicators are complete
            // TODO: Register program (different for .lpz vs .cpz, .clz assumed already registered)
            if (extension == ".lpz" || extension == ".cpz")
            {
                RegisterProgram(shellStream, settings.Slot, extension);
            }

            // Execute progres command
            var progresCommand = $"progres -P:{settings.Slot}";
            AnsiConsole.MarkupLine($"[yellow]Executing:[/] {progresCommand}");
            
            shellStream.WriteLine(progresCommand);
            var (progresSuccess, progresOutput) = await WaitForCommandCompletionAsync(shellStream, null, null);

            AnsiConsole.MarkupLine("[green]Program updated successfully![/]");
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

    private async Task<Dictionary<string, ISftpFile>> GetRemoteFileMetadataAsync(
        ISftpClient sftpClient,
        string remotePath)
    {
        return await Task.Run(() =>
        {
            var files = new Dictionary<string, ISftpFile>();

            if (!sftpClient.Exists(remotePath))
            {
                return files;
            }

            this.GetRemoteFilesRecursive(sftpClient, remotePath, remotePath, files);
            return files;
        });
    }

    private void GetRemoteFilesRecursive(
        ISftpClient sftpClient,
        string currentPath,
        string basePath,
        Dictionary<string, ISftpFile> files)
    {
        var items = sftpClient.ListDirectory(currentPath);

        foreach (var item in items)
        {
            if (item.Name == "." || item.Name == "..")
                continue;

            if (item.IsDirectory)
            {
                this.GetRemoteFilesRecursive(sftpClient, item.FullName, basePath, files);
            }
            else
            {
                var relativePath = item.FullName.Substring(basePath.Length).TrimStart('/');
                files[relativePath] = item;
            }
        }
    }

    private bool IsFileChanged(
        string localFile,
        string localBasePath,
        Dictionary<string, ISftpFile> remoteFiles)
    {
        var relativePath = Path.GetRelativePath(localBasePath, localFile).Replace('\\', '/');

        if (!remoteFiles.TryGetValue(relativePath, out var remoteFile))
        {
            // File doesn't exist remotely, so it's new/changed
            return true;
        }

        var localLastWriteTime = File.GetLastWriteTimeUtc(localFile);
        var remoteLastWriteTime = remoteFile.LastWriteTimeUtc;

        // Compare timestamps (allowing for small differences due to file system precision)
        return Math.Abs((localLastWriteTime - remoteLastWriteTime).TotalSeconds) > 2;
    }

    private void EnsureRemoteDirectoryExists(ISftpClient sftpClient, string remotePath)
    {
        var parts = remotePath.Split('/');
        var currentPath = string.Empty;

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

            if (!sftpClient.Exists(currentPath))
            {
                sftpClient.CreateDirectory(currentPath);
            }
        }
    }

    private void RegisterProgram(ShellStream shellStream, int slot, string extension)
    {
        // TODO: Implement program registration
        // Different commands for .lpz vs .cpz
        // .clz files are assumed to already be registered
        if (extension == ".lpz")
        {
            // TODO: Add .lpz registration command
            AnsiConsole.MarkupLine("[cyan]Registering .lpz program...[/]");
            AnsiConsole.MarkupLine("[yellow]TODO:[/] Register .lpz program");
        }
        else if (extension == ".cpz")
        {
            // TODO: Add .cpz registration command
            AnsiConsole.MarkupLine("[yellow]TODO:[/] Register .cpz program");
        }
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