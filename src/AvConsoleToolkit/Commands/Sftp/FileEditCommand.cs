// <copyright file="FileEditCommand.cs">
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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.Configuration;
using AvConsoleToolkit.Crestron;
using AvConsoleToolkit.Editors;
using AvConsoleToolkit.Ssh;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AvConsoleToolkit.Commands.Sftp
{
    /// <summary>
    /// Command that allows in-line editing of files on remote Crestron devices.
    /// Downloads the file locally, opens an editor, and uploads changes back to the device.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class FileEditCommand : AsyncCommand<FileEditSettings>
    {
        /// <inheritdoc/>
        public override async Task<int> ExecuteAsync(CommandContext context, FileEditSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(settings.RemoteFilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Remote file path is required.");
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.Host))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Host address is required.");
                    return 1;
                }

                // Resolve credentials if needed
                if (string.IsNullOrEmpty(settings.Username) || string.IsNullOrEmpty(settings.Password))
                {
                    if (settings.Verbose)
                    {
                        AnsiConsole.MarkupLine("[fuchsia]No username/password provided, looking up values from address books.[/]");
                    }

                    var entry = await ToolboxAddressBook.LookupEntryAsync(settings.Host);
                    if (entry is null)
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Could not find device in address books and no username/password provided.");
                        return 101;
                    }

                    if (entry.Username is null || entry.Password is null)
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Address book entry is missing username or password.");
                        return 102;
                    }

                    settings.Username = entry.Username;
                    settings.Password = entry.Password;
                }

                // Get SSH connection
                var connection = ConnectionFactory.Instance.GetSshConnection(settings.Host, 22, settings.Username, settings.Password);

                // Check for cached file or download
                var cache = TempFileCache.Instance;
                var localPath = cache.GetCachedFilePath(settings.Host, settings.RemoteFilePath);

                if (localPath == null || settings.ForceDownload || !System.IO.File.Exists(localPath))
                {
                    localPath = cache.GetOrCreateCachePath(settings.Host, settings.RemoteFilePath);
                    await this.DownloadFileAsync(connection, settings, localPath, cancellationToken);
                }
                else if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Using cached file at '{localPath}'[/]");
                }

                // Determine which editor to use
                var editorPath = this.GetEditorForFile(settings.RemoteFilePath, settings);

                if (string.IsNullOrEmpty(editorPath))
                {
                    // Use built-in editor
                    await this.EditWithBuiltinEditorAsync(connection, settings, localPath, cancellationToken);
                }
                else
                {
                    // Use external editor with file monitoring
                    await this.EditWithExternalEditorAsync(connection, settings, localPath, editorPath, cancellationToken);
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (settings.Verbose)
                {
                    AnsiConsole.MarkupLine($"[red]Error:\r\n{ex}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                }

                return 1;
            }
        }

        private async Task DownloadFileAsync(ISshConnection connection, FileEditSettings settings, string localPath, CancellationToken cancellationToken)
        {
            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns([
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new TransferSpeedColumn(),
                    new RemainingTimeColumn()
                ])
                .StartAsync(async ctx =>
                {
                    var displayName = settings.Verbose ? settings.RemoteFilePath : Path.GetFileName(settings.RemoteFilePath);
                    var downloadTask = ctx.AddTask($"[cyan]Downloading {displayName}[/]");

                    await connection.ConnectFileTransferAsync(cancellationToken);

                    // Check if file exists remotely
                    if (!await connection.ExistsAsync(settings.RemoteFilePath, cancellationToken))
                    {
                        downloadTask.StopTask();
                        throw new FileNotFoundException($"Remote file not found: {settings.RemoteFilePath}");
                    }

                    // Ensure local directory exists
                    var localDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(localDir))
                    {
                        Directory.CreateDirectory(localDir);
                    }

                    // Download to temporary file.
                    using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                    {
                        await connection.DownloadFileAsync(settings.RemoteFilePath, fileStream, cancellationToken);
                    }

                    downloadTask.Value = 100;
                    downloadTask.StopTask();
                });

            AnsiConsole.MarkupLine("[green]Download complete.[/]");
        }

        private async Task UploadFileAsync(ISshConnection connection, FileEditSettings settings, string localPath, CancellationToken cancellationToken, Action<double>? progressCallback = null)
        {
            await connection.ConnectFileTransferAsync(cancellationToken);

            using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read);
            var fileSize = fileStream.Length;

            await connection.UploadFileAsync(
                fileStream,
                settings.RemoteFilePath,
                true,
                uploaded =>
                {
                    var percentage = ((double)uploaded / fileSize) * 100;
                    progressCallback?.Invoke(percentage);
                },
                cancellationToken);

            // Preserve timestamp
            var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(localPath);
            await connection.SetLastWriteTimeUtcAsync(settings.RemoteFilePath, lastWriteTime, cancellationToken);
        }

        private async Task EditWithBuiltinEditorAsync(ISshConnection connection, FileEditSettings settings, string localPath, CancellationToken cancellationToken)
        {
            var displayName = Path.GetFileName(settings.RemoteFilePath);
            FileTextEditor? editor = null;

            async Task OnSaveAsync()
            {
                try
                {
                    // Show upload progress in the editor
                    if (editor != null)
                    {
                        editor.UploadProgress = 0;
                    }

                    await this.UploadFileAsync(
                        connection,
                        settings,
                        localPath,
                        cancellationToken,
                        progress =>
                        {
                            if (editor != null)
                            {
                                editor.UploadProgress = (int)progress;
                            }
                        });

                    if (editor != null)
                    {
                        editor.UploadProgress = -1;
                    }
                }
                catch
                {
                    if (editor != null)
                    {
                        editor.UploadProgress = -1;
                    }

                    throw;
                }
            }

            editor = new FileTextEditor(localPath, displayName, OnSaveAsync);
            await editor.RunAsync(cancellationToken);
        }

        private async Task EditWithExternalEditorAsync(ISshConnection connection, FileEditSettings settings, string localPath, string editorPath, CancellationToken cancellationToken)
        {
            var displayName = Path.GetFileName(settings.RemoteFilePath);
            AnsiConsole.MarkupLine($"[cyan]Opening '{displayName}' in external editor: {editorPath}[/]");
            AnsiConsole.MarkupLine("[dim]File will be uploaded automatically when saved. Press Ctrl+K to kill watcher.[/]");

            // Start file system watcher
            var fileDir = Path.GetDirectoryName(localPath) ?? ".";
            var fileName = Path.GetFileName(localPath);
            var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(localPath);
            var uploading = false;

            using var watcher = new FileSystemWatcher(fileDir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            void OnFileChanged(object sender, FileSystemEventArgs e)
            {
                // Run async work in a fire-and-forget task with proper exception handling
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (uploading)
                        {
                            return;
                        }

                        // Debounce - wait a moment to ensure the file is fully written
                        await Task.Delay(500, cancellationToken);

                        var newWriteTime = System.IO.File.GetLastWriteTimeUtc(localPath);
                        if (newWriteTime <= lastWriteTime)
                        {
                            return;
                        }

                        lastWriteTime = newWriteTime;
                        uploading = true;

                        try
                        {
                            await AnsiConsole.Progress()
                                .AutoClear(true)
                                .Columns([
                                    new TaskDescriptionColumn(),
                                    new ProgressBarColumn(),
                                    new PercentageColumn()
                                ])
                                .StartAsync(async ctx =>
                                {
                                    var uploadTask = ctx.AddTask($"[green]Uploading {displayName}[/]");
                                    await this.UploadFileAsync(
                                        connection,
                                        settings,
                                        localPath,
                                        cancellationToken,
                                        progress => uploadTask.Value = progress);

                                    uploadTask.Value = 100;
                                    uploadTask.StopTask();
                                });

                            AnsiConsole.MarkupLine($"[green]'{displayName}' uploaded successfully at {DateTime.Now:HH:mm:ss}[/]");
                        }
                        finally
                        {
                            uploading = false;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Upload failed: {ex.Message}[/]");
                    }
                }, cancellationToken);
            }

            watcher.Changed += OnFileChanged;
            var waitFlags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { 
                ["code"] = "--wait",
                ["subl"] = "--wait",
                ["atom"] = "--wait",
                ["codium"] = "--wait",
                ["kate"] = "--block",
                ["gedit"] = "--wait",
                ["geany"] = "--new-instance --wait",
                ["notepad++"] = "-multiInst -notabbar -nosession -noPlugin"
            };

            var waitFlag = waitFlags.TryGetValue(editorPath, out var flag) ? $" {flag}" : string.Empty;

            // Start the external editor
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = editorPath,
                    Arguments = $"\"{localPath}\"{waitFlag}",
                    UseShellExecute = true
                }
            };

            var startTime = DateTime.Now;
            process.Start();
            // Wait for user to cancel or editor to close
            try
            {
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    if (System.Console.KeyAvailable)
                    {
                        var key = System.Console.ReadKey();
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.K)
                        {
                            watcher.Dispose();
                            throw new OperationCanceledException();
                        }
                    }

                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }

            watcher.Changed -= OnFileChanged;
            watcher.EnableRaisingEvents = false;

            if (!process.HasExited)
            {
                AnsiConsole.MarkupLine("[yellow]Stopped watching file. Editor process is still running.[/]");
            }
            else
            {
                if (DateTime.Now - startTime < TimeSpan.FromSeconds(5))
                {
                    AnsiConsole.MarkupLine("[red]Editor closed within 5 seconds of opening. If this was not intended, your selected editor may not block properly and may require an additional flag set to function properly, such as 'code --wait'.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[dim]Editor closed.[/]");
                }
            }
        }

        private string? GetEditorForFile(string filePath, FileEditSettings settings)
        {
            if (settings.UseBuiltinEditor)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(settings.ExternalEditor))
            {
                return settings.ExternalEditor;
            }

            var extension = Path.GetExtension(filePath)?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
            var editorSettings = AppConfig.Settings.Editor;

            // Check for extension-specific mapping in the dictionary
            if (!string.IsNullOrWhiteSpace(editorSettings?.Mappings))
            {
                var mappings = editorSettings.Mappings.Split(';', StringSplitOptions.RemoveEmptyEntries).ToDictionary(s => s.Split('=')[0], s => s.Split('=')[1]);
                if(mappings.TryGetValue(extension, out var editorPath) && !string.IsNullOrWhiteSpace(editorPath))
                {
                    return editorPath;
                }
            }
            
            // Check if a default editor other than the built-in editor is configured.
            if (!string.IsNullOrWhiteSpace(editorSettings?.DefaultEditor))
            {
                return editorSettings.DefaultEditor;
            }

            // No editor configured - use built-in
            return null;
        }
    }
}
