// <copyright file="DeployViewModel.cs">
// The MIT License
// Copyright © Christopher McNeely
// </copyright>

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvConsoleToolkit.CertManager;
using ReactiveUI;

namespace AvConsoleToolkit.Web.ViewModels
{
    /// <summary>
    /// ViewModel for managing deployment targets and executing deployments using ReactiveUI.
    /// </summary>
    public class DeployViewModel : ReactiveObject
    {
        private string _selectedCertName = string.Empty;
        private string _connectionAddress = string.Empty;
        private int _port = 22;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private DeployType _deployType = DeployType.Scp;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private bool _isDeploying;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeployViewModel"/> class.
        /// </summary>
        /// <param name="databasePath">Path to the certificate database.</param>
        public DeployViewModel(string databasePath)
        {
            DatabasePath = databasePath;
            Targets = new ObservableCollection<DeploymentTargetRecord>();
            AvailableCerts = new ObservableCollection<DeviceCertificateRecord>();
            DeployResults = new ObservableCollection<string>();

            var canAddTarget = this.WhenAnyValue(
                x => x.SelectedCertName,
                x => x.ConnectionAddress,
                (cert, addr) =>
                    !string.IsNullOrWhiteSpace(cert) &&
                    !string.IsNullOrWhiteSpace(addr));

            AddTargetCommand = ReactiveCommand.Create(AddTarget, canAddTarget);
            RemoveTargetCommand = ReactiveCommand.Create<int>(RemoveTarget);
            DeployAllCommand = ReactiveCommand.CreateFromTask(DeployAll);
            DeploySingleCommand = ReactiveCommand.CreateFromTask<int>(DeploySingle);
            RefreshCommand = ReactiveCommand.Create(Refresh);

            Refresh();
        }

        /// <summary>
        /// Gets the database path.
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Gets the collection of deployment targets.
        /// </summary>
        public ObservableCollection<DeploymentTargetRecord> Targets { get; }

        /// <summary>
        /// Gets the available device certificates.
        /// </summary>
        public ObservableCollection<DeviceCertificateRecord> AvailableCerts { get; }

        /// <summary>
        /// Gets the deployment result messages.
        /// </summary>
        public ObservableCollection<string> DeployResults { get; }

        /// <summary>
        /// Gets or sets the selected certificate name for adding a target.
        /// </summary>
        public string SelectedCertName
        {
            get => _selectedCertName;
            set => this.RaiseAndSetIfChanged(ref _selectedCertName, value);
        }

        /// <summary>
        /// Gets or sets the connection address.
        /// </summary>
        public string ConnectionAddress
        {
            get => _connectionAddress;
            set => this.RaiseAndSetIfChanged(ref _connectionAddress, value);
        }

        /// <summary>
        /// Gets or sets the connection port.
        /// </summary>
        public int Port
        {
            get => _port;
            set => this.RaiseAndSetIfChanged(ref _port, value);
        }

        /// <summary>
        /// Gets or sets the username.
        /// </summary>
        public string Username
        {
            get => _username;
            set => this.RaiseAndSetIfChanged(ref _username, value);
        }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        public string Password
        {
            get => _password;
            set => this.RaiseAndSetIfChanged(ref _password, value);
        }

        /// <summary>
        /// Gets or sets the deployment type.
        /// </summary>
        public DeployType DeployType
        {
            get => _deployType;
            set => this.RaiseAndSetIfChanged(ref _deployType, value);
        }

        /// <summary>
        /// Gets or sets the status message.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the status is an error.
        /// </summary>
        public bool IsError
        {
            get => _isError;
            set => this.RaiseAndSetIfChanged(ref _isError, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether a deployment is in progress.
        /// </summary>
        public bool IsDeploying
        {
            get => _isDeploying;
            set => this.RaiseAndSetIfChanged(ref _isDeploying, value);
        }

        /// <summary>
        /// Gets the command to add a target.
        /// </summary>
        public ReactiveCommand<Unit, Unit> AddTargetCommand { get; }

        /// <summary>
        /// Gets the command to remove a target.
        /// </summary>
        public ReactiveCommand<int, Unit> RemoveTargetCommand { get; }

        /// <summary>
        /// Gets the command to deploy to all targets.
        /// </summary>
        public ReactiveCommand<Unit, Unit> DeployAllCommand { get; }

        /// <summary>
        /// Gets the command to deploy to a single target.
        /// </summary>
        public ReactiveCommand<int, Unit> DeploySingleCommand { get; }

        /// <summary>
        /// Gets the command to refresh.
        /// </summary>
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        private void AddTarget()
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                var certs = db.GetCertsByName(SelectedCertName);
                if (certs.Count == 0)
                {
                    StatusMessage = $"Certificate '{SelectedCertName}' not found.";
                    IsError = true;
                    return;
                }

                var cert = certs[0];
                var record = new DeploymentTargetRecord
                {
                    CertId = cert.Id,
                    ConnectionAddress = ConnectionAddress,
                    Port = Port,
                    Username = Username,
                    Password = Password,
                    DeployType = DeployType,
                };

                db.InsertTarget(record);

                StatusMessage = $"Target added for '{cert.Name}' → {ConnectionAddress}:{Port}.";
                IsError = false;
                ConnectionAddress = string.Empty;
                Username = string.Empty;
                Password = string.Empty;

                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsError = true;
            }
        }

        private void RemoveTarget(int id)
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                db.DeleteTarget(id);
                StatusMessage = $"Target (ID {id}) removed.";
                IsError = false;
                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsError = true;
            }
        }

        private async Task DeployAll()
        {
            IsDeploying = true;
            DeployResults.Clear();

            try
            {
                using var db = new CertDatabase(DatabasePath);
                var targets = db.ListTargets();

                foreach (var target in targets)
                {
                    var cert = db.GetCert(target.CertId);
                    if (cert == null)
                    {
                        DeployResults.Add($"✗ Target {target.Id}: Certificate not found.");
                        continue;
                    }

                    var ca = db.GetCa(cert.CaId);
                    var caCertPem = ca?.CertificatePem ?? [];

                    var success = await CertDeployer.DeployAsync(target, cert, caCertPem, CancellationToken.None);
                    if (success)
                    {
                        db.UpdateTargetLastDeployed(target.Id, DateTime.UtcNow);
                        DeployResults.Add($"✓ {cert.Name} → {target.ConnectionAddress} — Success");
                    }
                    else
                    {
                        DeployResults.Add($"✗ {cert.Name} → {target.ConnectionAddress} — Failed");
                    }
                }

                StatusMessage = $"Deployment complete. {targets.Count} target(s) processed.";
                IsError = false;
                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsError = true;
            }
            finally
            {
                IsDeploying = false;
            }
        }

        private async Task DeploySingle(int targetId)
        {
            IsDeploying = true;
            DeployResults.Clear();

            try
            {
                using var db = new CertDatabase(DatabasePath);
                var target = db.GetTarget(targetId);
                if (target == null)
                {
                    StatusMessage = $"Target {targetId} not found.";
                    IsError = true;
                    return;
                }

                var cert = db.GetCert(target.CertId);
                if (cert == null)
                {
                    StatusMessage = "Certificate not found.";
                    IsError = true;
                    return;
                }

                var ca = db.GetCa(cert.CaId);
                var caCertPem = ca?.CertificatePem ?? [];

                var success = await CertDeployer.DeployAsync(target, cert, caCertPem, CancellationToken.None);
                if (success)
                {
                    db.UpdateTargetLastDeployed(target.Id, DateTime.UtcNow);
                    DeployResults.Add($"✓ {cert.Name} → {target.ConnectionAddress} — Success");
                    StatusMessage = "Deployment succeeded.";
                    IsError = false;
                }
                else
                {
                    DeployResults.Add($"✗ {cert.Name} → {target.ConnectionAddress} — Failed");
                    StatusMessage = "Deployment failed.";
                    IsError = true;
                }

                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsError = true;
            }
            finally
            {
                IsDeploying = false;
            }
        }

        private void Refresh()
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                var targets = db.ListTargets();
                Targets.Clear();
                foreach (var t in targets)
                {
                    Targets.Add(t);
                }

                var certs = db.ListCerts();
                AvailableCerts.Clear();
                foreach (var c in certs)
                {
                    AvailableCerts.Add(c);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsError = true;
            }
        }
    }
}
