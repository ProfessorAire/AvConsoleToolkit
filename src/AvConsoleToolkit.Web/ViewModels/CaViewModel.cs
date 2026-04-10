// <copyright file="CaViewModel.cs">
// The MIT License
// Copyright © Christopher McNeely
// </copyright>

using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using AvConsoleToolkit.CertManager;
using ReactiveUI;

namespace AvConsoleToolkit.Web.ViewModels
{
    /// <summary>
    /// ViewModel for managing Certificate Authorities using ReactiveUI.
    /// </summary>
    public class CaViewModel : ReactiveObject
    {
        private string _newCaName = string.Empty;
        private string _newCaOrg = string.Empty;
        private string _newCaCountry = string.Empty;
        private string _newCaOu = string.Empty;
        private string _newCaState = string.Empty;
        private string _newCaLocality = string.Empty;
        private int _newCaValidityDays = 3650;
        private string _statusMessage = string.Empty;
        private bool _isError;

        /// <summary>
        /// Initializes a new instance of the <see cref="CaViewModel"/> class.
        /// </summary>
        /// <param name="databasePath">Path to the certificate database.</param>
        public CaViewModel(string databasePath)
        {
            DatabasePath = databasePath;
            CertificateAuthorities = new ObservableCollection<CertificateAuthorityRecord>();

            var canCreate = this.WhenAnyValue(
                x => x.NewCaName,
                x => x.NewCaOrg,
                x => x.NewCaCountry,
                (name, org, country) =>
                    !string.IsNullOrWhiteSpace(name) &&
                    !string.IsNullOrWhiteSpace(org) &&
                    !string.IsNullOrWhiteSpace(country));

            CreateCaCommand = ReactiveCommand.Create(CreateCa, canCreate);
            DeleteCaCommand = ReactiveCommand.Create<int>(DeleteCa);
            RefreshCommand = ReactiveCommand.Create(Refresh);

            Refresh();
        }

        /// <summary>
        /// Gets the database path.
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Gets the observable collection of Certificate Authorities.
        /// </summary>
        public ObservableCollection<CertificateAuthorityRecord> CertificateAuthorities { get; }

        /// <summary>
        /// Gets or sets the name for a new CA.
        /// </summary>
        public string NewCaName
        {
            get => _newCaName;
            set => this.RaiseAndSetIfChanged(ref _newCaName, value);
        }

        /// <summary>
        /// Gets or sets the organization for a new CA.
        /// </summary>
        public string NewCaOrg
        {
            get => _newCaOrg;
            set => this.RaiseAndSetIfChanged(ref _newCaOrg, value);
        }

        /// <summary>
        /// Gets or sets the country code for a new CA.
        /// </summary>
        public string NewCaCountry
        {
            get => _newCaCountry;
            set => this.RaiseAndSetIfChanged(ref _newCaCountry, value);
        }

        /// <summary>
        /// Gets or sets the organizational unit for a new CA.
        /// </summary>
        public string NewCaOu
        {
            get => _newCaOu;
            set => this.RaiseAndSetIfChanged(ref _newCaOu, value);
        }

        /// <summary>
        /// Gets or sets the state/province for a new CA.
        /// </summary>
        public string NewCaState
        {
            get => _newCaState;
            set => this.RaiseAndSetIfChanged(ref _newCaState, value);
        }

        /// <summary>
        /// Gets or sets the locality/city for a new CA.
        /// </summary>
        public string NewCaLocality
        {
            get => _newCaLocality;
            set => this.RaiseAndSetIfChanged(ref _newCaLocality, value);
        }

        /// <summary>
        /// Gets or sets the validity days for a new CA certificate.
        /// </summary>
        public int NewCaValidityDays
        {
            get => _newCaValidityDays;
            set => this.RaiseAndSetIfChanged(ref _newCaValidityDays, value);
        }

        /// <summary>
        /// Gets or sets the status message to display.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the status message is an error.
        /// </summary>
        public bool IsError
        {
            get => _isError;
            set => this.RaiseAndSetIfChanged(ref _isError, value);
        }

        /// <summary>
        /// Gets the command to create a new CA.
        /// </summary>
        public ReactiveCommand<Unit, Unit> CreateCaCommand { get; }

        /// <summary>
        /// Gets the command to delete a CA by ID.
        /// </summary>
        public ReactiveCommand<int, Unit> DeleteCaCommand { get; }

        /// <summary>
        /// Gets the command to refresh the CA list.
        /// </summary>
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        private void CreateCa()
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                var cert = CertificateGenerator.CreateCaCertificate(
                    NewCaCountry,
                    NewCaOrg,
                    NewCaName,
                    NewCaValidityDays,
                    string.IsNullOrWhiteSpace(NewCaOu) ? null : NewCaOu,
                    string.IsNullOrWhiteSpace(NewCaState) ? null : NewCaState,
                    string.IsNullOrWhiteSpace(NewCaLocality) ? null : NewCaLocality);

                var certPem = CertificateGenerator.ExportCertificatePem(cert);
                var keyPem = CertificateGenerator.ExportPrivateKeyPem(cert);

                var record = new CertificateAuthorityRecord
                {
                    Name = NewCaName,
                    Country = NewCaCountry,
                    Organization = NewCaOrg,
                    CertificatePem = certPem,
                    PrivateKeyPem = keyPem,
                    CreatedUtc = DateTime.UtcNow,
                    ExpiresUtc = cert.NotAfter.ToUniversalTime(),
                };

                db.InsertCa(record);

                StatusMessage = $"CA '{NewCaName}' created successfully.";
                IsError = false;

                NewCaName = string.Empty;
                NewCaOrg = string.Empty;
                NewCaCountry = string.Empty;
                NewCaOu = string.Empty;
                NewCaState = string.Empty;
                NewCaLocality = string.Empty;

                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating CA: {ex.Message}";
                IsError = true;
            }
        }

        private void DeleteCa(int id)
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                db.DeleteCa(id);
                StatusMessage = $"CA (ID {id}) deleted.";
                IsError = false;
                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error deleting CA: {ex.Message}";
                IsError = true;
            }
        }

        private void Refresh()
        {
            try
            {
                using var db = new CertDatabase(DatabasePath);
                var cas = db.ListCas();
                CertificateAuthorities.Clear();
                foreach (var ca in cas)
                {
                    CertificateAuthorities.Add(ca);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading CAs: {ex.Message}";
                IsError = true;
            }
        }
    }
}
