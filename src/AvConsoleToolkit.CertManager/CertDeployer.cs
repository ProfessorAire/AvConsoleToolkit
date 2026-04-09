// <copyright file="CertDeployer.cs">
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
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Spectre.Console;

namespace AvConsoleToolkit.CertManager
{
    /// <summary>
    /// Handles deploying certificates to remote devices based on the configured <see cref="DeployType"/>.
    /// </summary>
    public static class CertDeployer
    {
        /// <summary>
        /// Deploys a certificate to a target device.
        /// </summary>
        /// <param name="target">The deployment target configuration.</param>
        /// <param name="cert">The device certificate to deploy.</param>
        /// <param name="caCertPem">The CA certificate PEM bytes (for root CA deployment).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><see langword="true"/> if deployment succeeded.</returns>
        public static async Task<bool> DeployAsync(DeploymentTargetRecord target, DeviceCertificateRecord cert, byte[] caCertPem, CancellationToken cancellationToken)
        {
            return target.DeployType switch
            {
                DeployType.Crestron3 => await DeployCrestron3Async(target, cert, cancellationToken),
                DeployType.Crestron4 => await DeployCrestron4Async(target, cert, cancellationToken),
                DeployType.CrestronTP60Series => await DeployCrestronTouchpanelAsync(target, cert, "60", cancellationToken),
                DeployType.CrestronTP70Series => await DeployCrestronTouchpanelAsync(target, cert, "70", cancellationToken),
                DeployType.TrueNas => await DeployTrueNasAsync(target, cert, cancellationToken),
                DeployType.UniFi => await DeployUniFiAsync(target, cert, caCertPem, cancellationToken),
                DeployType.Scp => await DeployScpAsync(target, cert, caCertPem, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported deploy type: {target.DeployType}"),
            };
        }

        private static ConnectionInfo CreateConnectionInfo(DeploymentTargetRecord target)
        {
            var authMethods = new List<AuthenticationMethod>();

            if (!string.IsNullOrWhiteSpace(target.SshKeyPath))
            {
                var keyFile = new PrivateKeyFile(target.SshKeyPath);
                authMethods.Add(new PrivateKeyAuthenticationMethod(target.Username, keyFile));
            }

            if (!string.IsNullOrWhiteSpace(target.Password))
            {
                authMethods.Add(new PasswordAuthenticationMethod(target.Username, target.Password));
            }

            if (authMethods.Count == 0)
            {
                authMethods.Add(new NoneAuthenticationMethod(target.Username));
            }

            return new ConnectionInfo(target.ConnectionAddress, target.Port, target.Username, [.. authMethods]);
        }

        private static async Task<bool> DeployCrestron3Async(DeploymentTargetRecord target, DeviceCertificateRecord cert, CancellationToken cancellationToken)
        {
            try
            {
                var connInfo = CreateConnectionInfo(target);

                using var sftp = new SftpClient(connInfo);
                sftp.Connect();

                // Crestron 3-Series uses /sys directory for certs
                const string certDir = "/sys";
                var certName = cert.DnsNames.Split(',')[0].Trim();

                UploadBytes(sftp, $"{certDir}/cert.cer", cert.CertificatePem);
                UploadBytes(sftp, $"{certDir}/cert.key", cert.PrivateKeyPem);

                sftp.Disconnect();

                // Issue SSL command via SSH
                using var ssh = new SshClient(connInfo);
                ssh.Connect();
                var result = ssh.RunCommand("ssl on");
                ssh.Disconnect();

                AnsiConsole.MarkupLine($"  [dim]SSL command result: {result.Result.EscapeMarkup()}[/]");

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]Deployment failed:[/] {ex.Message.EscapeMarkup()}");
                return false;
            }
        }

        private static async Task<bool> DeployCrestron4Async(DeploymentTargetRecord target, DeviceCertificateRecord cert, CancellationToken cancellationToken)
        {
            try
            {
                var connInfo = CreateConnectionInfo(target);

                using var sftp = new SftpClient(connInfo);
                sftp.Connect();

                // Crestron 4-Series uses /opt/crestron/config/certificates
                const string certDir = "/opt/crestron/config/certificates";
                var certName = cert.DnsNames.Split(',')[0].Trim();

                UploadBytes(sftp, $"{certDir}/device.crt", cert.CertificatePem);
                UploadBytes(sftp, $"{certDir}/device.key", cert.PrivateKeyPem);
                UploadBytes(sftp, $"{certDir}/device.pfx", cert.Pfx);

                sftp.Disconnect();

                using var ssh = new SshClient(connInfo);
                ssh.Connect();
                var result = ssh.RunCommand("certificate apply webserver");
                ssh.Disconnect();

                AnsiConsole.MarkupLine($"  [dim]Certificate apply result: {result.Result.EscapeMarkup()}[/]");

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]Deployment failed:[/] {ex.Message.EscapeMarkup()}");
                return false;
            }
        }

        private static async Task<bool> DeployCrestronTouchpanelAsync(DeploymentTargetRecord target, DeviceCertificateRecord cert, string series, CancellationToken cancellationToken)
        {
            try
            {
                var connInfo = CreateConnectionInfo(target);

                using var sftp = new SftpClient(connInfo);
                sftp.Connect();

                // Touchpanels use /opt/crestron/config/certificates for both 60 and 70 series
                const string certDir = "/opt/crestron/config/certificates";

                UploadBytes(sftp, $"{certDir}/device.crt", cert.CertificatePem);
                UploadBytes(sftp, $"{certDir}/device.key", cert.PrivateKeyPem);
                UploadBytes(sftp, $"{certDir}/device.pfx", cert.Pfx);

                sftp.Disconnect();

                using var ssh = new SshClient(connInfo);
                ssh.Connect();
                var result = ssh.RunCommand("certificate apply webserver");
                ssh.Disconnect();

                AnsiConsole.MarkupLine($"  [dim]Certificate apply ({series}-Series TP) result: {result.Result.EscapeMarkup()}[/]");

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]Deployment failed:[/] {ex.Message.EscapeMarkup()}");
                return false;
            }
        }

        private static async Task<bool> DeployTrueNasAsync(DeploymentTargetRecord target, DeviceCertificateRecord cert, CancellationToken cancellationToken)
        {
            try
            {
                var baseUrl = $"https://{target.ConnectionAddress}:{target.Port}/api/v2.0";

                using var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

                using var http = new HttpClient(handler);
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {target.ApiKey}");

                var certLabel = $"cert-{cert.Name}-{DateTime.UtcNow:yyyyMMdd}";
                var certPem = Encoding.UTF8.GetString(cert.CertificatePem);
                var keyPem = Encoding.UTF8.GetString(cert.PrivateKeyPem);

                var payload = new
                {
                    name = certLabel,
                    certificate = certPem,
                    privatekey = keyPem,
                    type = 2, // CERTIFICATE_CREATE_IMPORTED
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await http.PostAsync($"{baseUrl}/certificate", content, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"  [red]TrueNAS API error ({response.StatusCode}):[/] {responseBody.EscapeMarkup()}");
                    return false;
                }

                // Get the new cert ID and set it as the UI certificate
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("id", out var idProp))
                {
                    var certId = idProp.GetInt32();
                    var uiPayload = JsonSerializer.Serialize(new { ui_certificate = certId });
                    var uiContent = new StringContent(uiPayload, Encoding.UTF8, "application/json");
                    await http.PutAsync($"{baseUrl}/system/general", uiContent, cancellationToken);
                    AnsiConsole.MarkupLine($"  [dim]TrueNAS UI certificate updated to '{certLabel.EscapeMarkup()}'[/]");
                }

                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]Deployment failed:[/] {ex.Message.EscapeMarkup()}");
                return false;
            }
        }

        private static async Task<bool> DeployUniFiAsync(DeploymentTargetRecord target, DeviceCertificateRecord cert, byte[] caCertPem, CancellationToken cancellationToken)
        {
            try
            {
                var connInfo = CreateConnectionInfo(target);

                using var sftp = new SftpClient(connInfo);
                sftp.Connect();

                // UniFi devices store certs in /etc/ssl/private
                UploadBytes(sftp, "/etc/ssl/private/cloudkey.crt", cert.FullChainPem);
                UploadBytes(sftp, "/etc/ssl/private/cloudkey.key", cert.PrivateKeyPem);

                sftp.Disconnect();

                // Restart nginx on UniFi
                using var ssh = new SshClient(connInfo);
                ssh.Connect();
                ssh.RunCommand("systemctl restart nginx");
                ssh.RunCommand("systemctl restart unifi-core");
                ssh.Disconnect();

                AnsiConsole.MarkupLine("  [dim]UniFi certificates deployed and services restarted.[/]");

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]Deployment failed:[/] {ex.Message.EscapeMarkup()}");
                return false;
            }
        }

        private static async Task<bool> DeployScpAsync(DeploymentTargetRecord target, DeviceCertificateRecord cert, byte[] caCertPem, CancellationToken cancellationToken)
        {
            try
            {
                var connInfo = CreateConnectionInfo(target);
                var mappings = ParseFileMappings(target.FileMappings);

                using var sftp = new SftpClient(connInfo);
                sftp.Connect();

                foreach (var (fileType, remotePath) in mappings)
                {
                    var data = fileType.ToUpperInvariant() switch
                    {
                        "ROOTCA" => caCertPem,
                        "PFX" => cert.Pfx,
                        "PEM" or "CRT" => cert.CertificatePem,
                        "KEY" => cert.PrivateKeyPem,
                        "FULLCHAIN" => cert.FullChainPem,
                        _ => null,
                    };

                    if (data != null && data.Length > 0)
                    {
                        UploadBytes(sftp, remotePath, data);
                        AnsiConsole.MarkupLine($"  [dim]Uploaded {fileType} → {remotePath.EscapeMarkup()}[/]");
                    }
                }

                sftp.Disconnect();

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]Deployment failed:[/] {ex.Message.EscapeMarkup()}");
                return false;
            }
        }

        private static void UploadBytes(SftpClient sftp, string remotePath, byte[] data)
        {
            using var stream = new MemoryStream(data);
            sftp.UploadFile(stream, remotePath, true);
        }

        private static Dictionary<string, string> ParseFileMappings(string fileMappingsJson)
        {
            if (string.IsNullOrWhiteSpace(fileMappingsJson))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(fileMappingsJson)
                    ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
