// <copyright file="DeviceCertViewModel.cs">
// The MIT License
// Copyright © Christopher McNeely
// </copyright>

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using AvConsoleToolkit.CertManager;
using ReactiveUI;

namespace AvConsoleToolkit.Web.ViewModels
{
    /// <summary>
    /// ViewModel for managing device certificates using ReactiveUI.
    /// </summary>
    public class DeviceCertViewModel : ReactiveObject
    {
        private string _newName = string.Empty;
        private string _selectedCaName = string.Empty;
        private string _dnsNames = string.Empty;
        private string _ipAddresses = string.Empty;
        private int _validityDays = 397;
        private string _statusMessage = string.Empty;
        private bool _isError;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceCertViewModel"/> class.
        /// </summary>
        /// <param name="databasePath">Path to the certificate database.</param>
        public DeviceCertViewModel(string databasePath)
        {
            DatabasePath = databasePath;
            DeviceCertificates = new ObservableCollection<DeviceCertificateRecord>();
            AvailableCas = new ObservableCollection<string>();

            var canCreate = this.WhenAnyValue(
                x => x.NewName,
                x => x.SelectedCaName,
                x => x.DnsNames,
                (name, ca, dns) =>
                    !string.IsNullOrWhiteSpace(name) &&
                    !string.IsNullOrWhiteSpace(ca) &&
                    !string.IsNullOrWhiteSpace(dns));

            CreateCertCommand = ReactiveCommand.Create(CreateCert, canCreate);
            DeleteCertCommand = ReactiveCommand.Create<int>(DeleteCert);
            RefreshCommand = ReactiveCommand.Create(Refresh);

            Refresh();
        }

        /// <summary>
        /// Gets the database path.
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Gets the observable collection of device certificates.
        /// </summary>
        public ObservableCollection<DeviceCertificateRecord> DeviceCertificates { get; }

        /// <summary>
        /// Gets the available CA names for the dropdown.
        /// </summary>
        public ObservableCollection<string> AvailableCas { get; }

        /// <summary>
        /// Gets or sets the friendly name for a new device cert.
        /// </summary>
        public string NewName
        {
            get => _newName;
            set => this.RaiseAndSetIfChanged(ref _newName, value);
        }

        /// <summary>
        /// Gets or sets the selected CA name.
        /// </summary>
        public string SelectedCaName
        {
            get => _selectedCaName;
            set => this.RaiseAndSetIfChanged(ref _selectedCaName, value);
        }

        /// <summary>
        /// Gets or sets the comma-separated DNS hostnames.
        /// </summary>
        public string DnsNames
        {
            get => _dnsNames;
            set => this.RaiseAndSetIfChanged(ref _dnsNames, value);
        }

        /// <summary>
        /// Gets or sets the comma-separated IP addresses.
        /// </summary>
        public string IpAddresses
        {
            get => _ipAddresses;
            set => this.RaiseAndSetIfChanged(ref _ipAddresses, value);
        }

        /// <summary>
        /// Gets or sets the certificate validity days.
        /// </summary>
        public int ValidityDays
        {
            get => _validityDays;
            set => this.RaiseAndSetIfChanged(ref _validityDays, value);
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
        /// Gets the command to create a new device certificate.
        /// </summary>
        public ReactiveCommand<Unit, Unit> CreateCertCommand { get; }

        /// <summary>
        /// Gets the command to delete a device certificate.
        /// </summary>
        public ReactiveCommand<int, Unit> DeleteCertCommand { get; }

        /// <summary>
        /// Gets the command to refresh the certificate list.
        /// </summary>
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        private void CreateCert()
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                var ca = db.GetCaByName(SelectedCaName);
                if (ca == null)
                {
                    StatusMessage = $"CA '{SelectedCaName}' not found.";
                    IsError = true;
                    return;
                }

                var dnsArray = DnsNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var ipArray = string.IsNullOrWhiteSpace(IpAddresses)
                    ? Array.Empty<string>()
                    : IpAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var caCert = CertificateGenerator.LoadCaCertificate(ca.CertificatePem, ca.PrivateKeyPem);
                var deviceCert = CertificateGenerator.CreateDeviceCertificate(caCert, dnsArray, ipArray, ca.Country, ca.Organization, ValidityDays);

                var certPem = CertificateGenerator.ExportCertificatePem(deviceCert);
                var keyPem = CertificateGenerator.ExportPrivateKeyPem(deviceCert);
                var pfx = CertificateGenerator.ExportPfx(deviceCert);
                var fullChain = CertificateGenerator.BuildFullChainPem(certPem, ca.CertificatePem);

                var record = new DeviceCertificateRecord
                {
                    CaId = ca.Id,
                    Name = NewName,
                    DnsNames = string.Join(",", dnsArray),
                    IpAddresses = string.Join(",", ipArray),
                    CertificatePem = certPem,
                    PrivateKeyPem = keyPem,
                    Pfx = pfx,
                    FullChainPem = fullChain,
                    CreatedUtc = DateTime.UtcNow,
                    ExpiresUtc = deviceCert.NotAfter.ToUniversalTime(),
                };

                db.InsertCert(record);

                StatusMessage = $"Device certificate '{NewName}' created.";
                IsError = false;

                NewName = string.Empty;
                DnsNames = string.Empty;
                IpAddresses = string.Empty;

                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsError = true;
            }
        }

        private void DeleteCert(int id)
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                db.DeleteCert(id);
                StatusMessage = $"Certificate (ID {id}) deleted.";
                IsError = false;
                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsError = true;
            }
        }

        private void Refresh()
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                var certs = db.ListCerts();
                DeviceCertificates.Clear();
                foreach (var cert in certs)
                {
                    DeviceCertificates.Add(cert);
                }

                var cas = db.ListCas();
                AvailableCas.Clear();
                foreach (var ca in cas)
                {
                    AvailableCas.Add(ca.Name);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading data: {ex.Message}";
                IsError = true;
            }
        }
    }
}
