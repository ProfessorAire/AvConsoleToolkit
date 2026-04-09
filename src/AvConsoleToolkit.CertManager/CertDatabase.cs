// <copyright file="CertDatabase.cs">
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
using System.Data.Common;
using System.Globalization;
using System.IO;
using DecentDB.AdoNet;

namespace AvConsoleToolkit.CertManager
{
    /// <summary>
    /// Provides CRUD operations for certificate storage using a local DecentDB database.
    /// Certificates are stored in the database rather than as loose files on disk.
    /// </summary>
    public sealed class CertDatabase : IDisposable
    {
        /// <summary>
        /// The default database file name used when searching in the working directory.
        /// </summary>
        public const string DefaultFileName = "certmanager.ddb";

        private readonly DecentDBConnection _connection;

        /// <summary>
        /// Gets the path to the database file.
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertDatabase"/> class, opening or creating the database at the specified path.
        /// </summary>
        /// <param name="databasePath">Full path to the DecentDB database file.</param>
        public CertDatabase(string databasePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

            DatabasePath = databasePath;

            var dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var builder = new DecentDBConnectionStringBuilder
            {
                DataSource = databasePath,
            };

            _connection = new DecentDBConnection(builder.ConnectionString);
            _connection.Open();
            EnsureSchema();
        }

        /// <summary>
        /// Resolves the database path to use.
        /// Checks: explicit path > working directory > global config path.
        /// Returns <see langword="null"/> if no database can be resolved.
        /// </summary>
        /// <param name="explicitPath">An explicit path provided by the user, or <see langword="null"/>.</param>
        /// <param name="globalConfigPath">An optional global config path to check, or <see langword="null"/>.</param>
        /// <returns>The resolved database path, or <see langword="null"/> if none found.</returns>
        public static string? ResolveDatabasePath(string? explicitPath, string? globalConfigPath = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return explicitPath;
            }

            // Check working directory
            var localPath = Path.Combine(Environment.CurrentDirectory, DefaultFileName);
            if (File.Exists(localPath))
            {
                return localPath;
            }

            // Check global config
            if (!string.IsNullOrWhiteSpace(globalConfigPath) && File.Exists(globalConfigPath))
            {
                return globalConfigPath;
            }

            return null;
        }

        /// <summary>
        /// Inserts a new Certificate Authority record.
        /// </summary>
        /// <param name="record">The CA record to insert.</param>
        /// <returns>The inserted record ID.</returns>
        public int InsertCa(CertificateAuthorityRecord record)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CertificateAuthorities (Name, Country, Organization, CertificatePem, PrivateKeyPem, CreatedUtc, ExpiresUtc)
                VALUES (@name, @country, @org, @cert, @key, @created, @expires);
                SELECT last_insert_rowid();";
            cmd.Parameters.Add(new DecentDBParameter("@name", record.Name));
            cmd.Parameters.Add(new DecentDBParameter("@country", record.Country));
            cmd.Parameters.Add(new DecentDBParameter("@org", record.Organization));
            cmd.Parameters.Add(new DecentDBParameter("@cert", record.CertificatePem));
            cmd.Parameters.Add(new DecentDBParameter("@key", record.PrivateKeyPem));
            cmd.Parameters.Add(new DecentDBParameter("@created", record.CreatedUtc.ToString("O")));
            cmd.Parameters.Add(new DecentDBParameter("@expires", record.ExpiresUtc.ToString("O")));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Returns all Certificate Authority records.
        /// </summary>
        /// <returns>A list of CA records.</returns>
        public List<CertificateAuthorityRecord> ListCas()
        {
            var results = new List<CertificateAuthorityRecord>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Country, Organization, CertificatePem, PrivateKeyPem, CreatedUtc, ExpiresUtc FROM CertificateAuthorities ORDER BY Name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadCa(reader));
            }

            return results;
        }

        /// <summary>
        /// Gets a Certificate Authority by its ID.
        /// </summary>
        /// <param name="id">The CA ID.</param>
        /// <returns>The CA record, or <see langword="null"/> if not found.</returns>
        public CertificateAuthorityRecord? GetCa(int id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Country, Organization, CertificatePem, PrivateKeyPem, CreatedUtc, ExpiresUtc FROM CertificateAuthorities WHERE Id = @id;";
            cmd.Parameters.Add(new DecentDBParameter("@id", id));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadCa(reader) : null;
        }

        /// <summary>
        /// Gets a Certificate Authority by its name.
        /// </summary>
        /// <param name="name">The CA name.</param>
        /// <returns>The CA record, or <see langword="null"/> if not found.</returns>
        public CertificateAuthorityRecord? GetCaByName(string name)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Country, Organization, CertificatePem, PrivateKeyPem, CreatedUtc, ExpiresUtc FROM CertificateAuthorities WHERE Name = @name COLLATE NOCASE;";
            cmd.Parameters.Add(new DecentDBParameter("@name", name));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadCa(reader) : null;
        }

        /// <summary>
        /// Deletes a Certificate Authority and all its device certificates.
        /// </summary>
        /// <param name="id">The CA ID to delete.</param>
        /// <returns><see langword="true"/> if the CA was deleted.</returns>
        public bool DeleteCa(int id)
        {
            using var transaction = _connection.BeginTransaction();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM DeviceCertificates WHERE CaId = @id;";
                cmd.Parameters.Add(new DecentDBParameter("@id", id));
                cmd.ExecuteNonQuery();
            }

            int rows;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM CertificateAuthorities WHERE Id = @id;";
                cmd.Parameters.Add(new DecentDBParameter("@id", id));
                rows = cmd.ExecuteNonQuery();
            }

            transaction.Commit();
            return rows > 0;
        }

        /// <summary>
        /// Inserts a new device certificate record.
        /// </summary>
        /// <param name="record">The device certificate record to insert.</param>
        /// <returns>The inserted record ID.</returns>
        public int InsertCert(DeviceCertificateRecord record)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO DeviceCertificates (CaId, Name, DnsNames, IpAddresses, CertificatePem, PrivateKeyPem, Pfx, FullChainPem, CreatedUtc, ExpiresUtc)
                VALUES (@caId, @name, @dns, @ips, @cert, @key, @pfx, @chain, @created, @expires);
                SELECT last_insert_rowid();";
            cmd.Parameters.Add(new DecentDBParameter("@caId", record.CaId));
            cmd.Parameters.Add(new DecentDBParameter("@name", record.Name));
            cmd.Parameters.Add(new DecentDBParameter("@dns", record.DnsNames));
            cmd.Parameters.Add(new DecentDBParameter("@ips", record.IpAddresses));
            cmd.Parameters.Add(new DecentDBParameter("@cert", record.CertificatePem));
            cmd.Parameters.Add(new DecentDBParameter("@key", record.PrivateKeyPem));
            cmd.Parameters.Add(new DecentDBParameter("@pfx", record.Pfx));
            cmd.Parameters.Add(new DecentDBParameter("@chain", record.FullChainPem));
            cmd.Parameters.Add(new DecentDBParameter("@created", record.CreatedUtc.ToString("O")));
            cmd.Parameters.Add(new DecentDBParameter("@expires", record.ExpiresUtc.ToString("O")));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Returns all device certificates, optionally filtered by CA ID.
        /// </summary>
        /// <param name="caId">Optional CA ID to filter by.</param>
        /// <returns>A list of device certificate records.</returns>
        public List<DeviceCertificateRecord> ListCerts(int? caId = null)
        {
            var results = new List<DeviceCertificateRecord>();
            using var cmd = _connection.CreateCommand();
            if (caId.HasValue)
            {
                cmd.CommandText = "SELECT Id, CaId, Name, DnsNames, IpAddresses, CertificatePem, PrivateKeyPem, Pfx, FullChainPem, CreatedUtc, ExpiresUtc FROM DeviceCertificates WHERE CaId = @caId ORDER BY Name;";
                cmd.Parameters.Add(new DecentDBParameter("@caId", caId.Value));
            }
            else
            {
                cmd.CommandText = "SELECT Id, CaId, Name, DnsNames, IpAddresses, CertificatePem, PrivateKeyPem, Pfx, FullChainPem, CreatedUtc, ExpiresUtc FROM DeviceCertificates ORDER BY Name;";
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadCert(reader));
            }

            return results;
        }

        /// <summary>
        /// Gets a device certificate by its ID.
        /// </summary>
        /// <param name="id">The certificate ID.</param>
        /// <returns>The device certificate record, or <see langword="null"/> if not found.</returns>
        public DeviceCertificateRecord? GetCert(int id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, CaId, Name, DnsNames, IpAddresses, CertificatePem, PrivateKeyPem, Pfx, FullChainPem, CreatedUtc, ExpiresUtc FROM DeviceCertificates WHERE Id = @id;";
            cmd.Parameters.Add(new DecentDBParameter("@id", id));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadCert(reader) : null;
        }

        /// <summary>
        /// Gets all device certificates matching the given name (case-insensitive).
        /// </summary>
        /// <param name="name">The certificate name.</param>
        /// <returns>A list of matching records.</returns>
        public List<DeviceCertificateRecord> GetCertsByName(string name)
        {
            var results = new List<DeviceCertificateRecord>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, CaId, Name, DnsNames, IpAddresses, CertificatePem, PrivateKeyPem, Pfx, FullChainPem, CreatedUtc, ExpiresUtc FROM DeviceCertificates WHERE Name = @name COLLATE NOCASE;";
            cmd.Parameters.Add(new DecentDBParameter("@name", name));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadCert(reader));
            }

            return results;
        }

        /// <summary>
        /// Deletes a device certificate.
        /// </summary>
        /// <param name="id">The certificate ID to delete.</param>
        /// <returns><see langword="true"/> if the certificate was deleted.</returns>
        public bool DeleteCert(int id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM DeviceCertificates WHERE Id = @id;";
            cmd.Parameters.Add(new DecentDBParameter("@id", id));
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Inserts a new deployment target record.
        /// </summary>
        /// <param name="record">The deployment target record to insert.</param>
        /// <returns>The inserted record ID.</returns>
        public int InsertTarget(DeploymentTargetRecord record)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO DeploymentTargets (CertId, ConnectionAddress, Port, Username, Password, DeployType, SshKeyPath, ApiKey, FileMappings, LastDeployedUtc)
                VALUES (@certId, @addr, @port, @user, @pass, @type, @sshKey, @apiKey, @fileMappings, @lastDeployed);
                SELECT last_insert_rowid();";
            cmd.Parameters.Add(new DecentDBParameter("@certId", record.CertId));
            cmd.Parameters.Add(new DecentDBParameter("@addr", record.ConnectionAddress));
            cmd.Parameters.Add(new DecentDBParameter("@port", record.Port));
            cmd.Parameters.Add(new DecentDBParameter("@user", record.Username));
            cmd.Parameters.Add(new DecentDBParameter("@pass", record.Password));
            cmd.Parameters.Add(new DecentDBParameter("@type", record.DeployType.ToString()));
            cmd.Parameters.Add(new DecentDBParameter("@sshKey", record.SshKeyPath));
            cmd.Parameters.Add(new DecentDBParameter("@apiKey", record.ApiKey));
            cmd.Parameters.Add(new DecentDBParameter("@fileMappings", record.FileMappings));
            cmd.Parameters.Add(new DecentDBParameter("@lastDeployed", record.LastDeployedUtc?.ToString("O") ?? string.Empty));
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Gets a deployment target by its ID.
        /// </summary>
        /// <param name="id">The target ID.</param>
        /// <returns>The deployment target record, or <see langword="null"/> if not found.</returns>
        public DeploymentTargetRecord? GetTarget(int id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, CertId, ConnectionAddress, Port, Username, Password, DeployType, SshKeyPath, ApiKey, FileMappings, LastDeployedUtc FROM DeploymentTargets WHERE Id = @id;";
            cmd.Parameters.Add(new DecentDBParameter("@id", id));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadTarget(reader) : null;
        }

        /// <summary>
        /// Returns all deployment targets, optionally filtered by certificate ID.
        /// </summary>
        /// <param name="certId">Optional certificate ID to filter by.</param>
        /// <returns>A list of deployment target records.</returns>
        public List<DeploymentTargetRecord> ListTargets(int? certId = null)
        {
            var results = new List<DeploymentTargetRecord>();
            using var cmd = _connection.CreateCommand();
            if (certId.HasValue)
            {
                cmd.CommandText = "SELECT Id, CertId, ConnectionAddress, Port, Username, Password, DeployType, SshKeyPath, ApiKey, FileMappings, LastDeployedUtc FROM DeploymentTargets WHERE CertId = @certId ORDER BY ConnectionAddress;";
                cmd.Parameters.Add(new DecentDBParameter("@certId", certId.Value));
            }
            else
            {
                cmd.CommandText = "SELECT Id, CertId, ConnectionAddress, Port, Username, Password, DeployType, SshKeyPath, ApiKey, FileMappings, LastDeployedUtc FROM DeploymentTargets ORDER BY ConnectionAddress;";
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadTarget(reader));
            }

            return results;
        }

        /// <summary>
        /// Deletes a deployment target.
        /// </summary>
        /// <param name="id">The target ID to delete.</param>
        /// <returns><see langword="true"/> if the target was deleted.</returns>
        public bool DeleteTarget(int id)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM DeploymentTargets WHERE Id = @id;";
            cmd.Parameters.Add(new DecentDBParameter("@id", id));
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Updates the last deployed timestamp for a deployment target.
        /// </summary>
        /// <param name="id">The target ID.</param>
        /// <param name="deployedUtc">The deployment timestamp.</param>
        public void UpdateTargetLastDeployed(int id, DateTime deployedUtc)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE DeploymentTargets SET LastDeployedUtc = @deployed WHERE Id = @id;";
            cmd.Parameters.Add(new DecentDBParameter("@deployed", deployedUtc.ToString("O")));
            cmd.Parameters.Add(new DecentDBParameter("@id", id));
            cmd.ExecuteNonQuery();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _connection.Dispose();
        }

        private static CertificateAuthorityRecord ReadCa(DbDataReader reader)
        {
            return new CertificateAuthorityRecord
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Country = reader.GetString(2),
                Organization = reader.GetString(3),
                CertificatePem = (byte[])reader[4],
                PrivateKeyPem = (byte[])reader[5],
                CreatedUtc = DateTime.ParseExact(reader.GetString(6), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ExpiresUtc = DateTime.ParseExact(reader.GetString(7), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            };
        }

        private static DeviceCertificateRecord ReadCert(DbDataReader reader)
        {
            return new DeviceCertificateRecord
            {
                Id = reader.GetInt32(0),
                CaId = reader.GetInt32(1),
                Name = reader.GetString(2),
                DnsNames = reader.GetString(3),
                IpAddresses = reader.GetString(4),
                CertificatePem = (byte[])reader[5],
                PrivateKeyPem = (byte[])reader[6],
                Pfx = (byte[])reader[7],
                FullChainPem = (byte[])reader[8],
                CreatedUtc = DateTime.ParseExact(reader.GetString(9), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ExpiresUtc = DateTime.ParseExact(reader.GetString(10), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            };
        }

        private static DeploymentTargetRecord ReadTarget(DbDataReader reader)
        {
            var lastDeployedStr = reader.IsDBNull(10) ? null : reader.GetString(10);
            DateTime? lastDeployed = null;
            if (!string.IsNullOrEmpty(lastDeployedStr))
            {
                lastDeployed = DateTime.ParseExact(lastDeployedStr, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            return new DeploymentTargetRecord
            {
                Id = reader.GetInt32(0),
                CertId = reader.GetInt32(1),
                ConnectionAddress = reader.GetString(2),
                Port = reader.GetInt32(3),
                Username = reader.GetString(4),
                Password = reader.GetString(5),
                DeployType = Enum.TryParse<DeployType>(reader.GetString(6), true, out var dt) ? dt : DeployType.Scp,
                SshKeyPath = reader.GetString(7),
                ApiKey = reader.GetString(8),
                FileMappings = reader.GetString(9),
                LastDeployedUtc = lastDeployed,
            };
        }

        private void EnsureSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS CertificateAuthorities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    Country TEXT NOT NULL,
                    Organization TEXT NOT NULL,
                    CertificatePem BLOB NOT NULL,
                    PrivateKeyPem BLOB NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    ExpiresUtc TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS DeviceCertificates (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CaId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    DnsNames TEXT NOT NULL,
                    IpAddresses TEXT NOT NULL,
                    CertificatePem BLOB NOT NULL,
                    PrivateKeyPem BLOB NOT NULL,
                    Pfx BLOB NOT NULL,
                    FullChainPem BLOB NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    ExpiresUtc TEXT NOT NULL,
                    FOREIGN KEY (CaId) REFERENCES CertificateAuthorities(Id)
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS DeploymentTargets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CertId INTEGER NOT NULL,
                    ConnectionAddress TEXT NOT NULL,
                    Port INTEGER NOT NULL DEFAULT 22,
                    Username TEXT NOT NULL DEFAULT '',
                    Password TEXT NOT NULL DEFAULT '',
                    DeployType TEXT NOT NULL DEFAULT 'Scp',
                    SshKeyPath TEXT NOT NULL DEFAULT '',
                    ApiKey TEXT NOT NULL DEFAULT '',
                    FileMappings TEXT NOT NULL DEFAULT '',
                    LastDeployedUtc TEXT NOT NULL DEFAULT '',
                    FOREIGN KEY (CertId) REFERENCES DeviceCertificates(Id)
                );";
            cmd.ExecuteNonQuery();
        }
    }
}
