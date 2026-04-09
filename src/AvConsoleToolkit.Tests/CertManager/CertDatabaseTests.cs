using System;
using System.IO;
using System.Linq;
using AvConsoleToolkit.CertManager;
using NUnit.Framework;

namespace AvConsoleToolkit.Tests.CertManager
{
    [TestFixture]
    public sealed class CertDatabaseTests : IDisposable
    {
        private string _dbPath = string.Empty;
        private CertDatabase? _db;

        [SetUp]
        public void SetUp()
        {
            try
            {
                _dbPath = Path.Combine(Path.GetTempPath(), $"certdb_test_{Guid.NewGuid():N}.ddb");
                _db = new CertDatabase(_dbPath);
            }
            catch (Exception ex) when (IsNativeLibUnavailable(ex))
            {
                Assert.Ignore("DecentDB native library not available on this platform.");
            }
        }

        [TearDown]
        public void TearDown()
        {
            _db?.Dispose();
            _db = null;

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        public void Dispose()
        {
            TearDown();
        }

        [Test]
        public void InsertCa_ReturnsPositiveId()
        {
            var record = CreateSampleCa();
            var id = _db!.InsertCa(record);
            Assert.That(id, Is.GreaterThan(0));
        }

        [Test]
        public void GetCa_ReturnsInsertedRecord()
        {
            var record = CreateSampleCa();
            var id = _db!.InsertCa(record);
            var retrieved = _db.GetCa(id);

            Assert.That(retrieved, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(retrieved!.Name, Is.EqualTo("test-ca"));
                Assert.That(retrieved.Country, Is.EqualTo("US"));
                Assert.That(retrieved.Organization, Is.EqualTo("TestOrg"));
                Assert.That(retrieved.CertificatePem, Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(retrieved.PrivateKeyPem, Is.EqualTo(new byte[] { 4, 5, 6 }));
            });
        }

        [Test]
        public void GetCaByName_ReturnsCaseInsensitiveMatch()
        {
            var record = CreateSampleCa();
            _db!.InsertCa(record);
            var retrieved = _db.GetCaByName("TEST-CA");

            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.Name, Is.EqualTo("test-ca"));
        }

        [Test]
        public void GetCaByName_ReturnsNullForNonExistentName()
        {
            var result = _db!.GetCaByName("nonexistent");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ListCas_ReturnsAllRecords()
        {
            _db!.InsertCa(CreateSampleCa("ca-one"));
            _db.InsertCa(CreateSampleCa("ca-two"));

            var cas = _db.ListCas();
            Assert.That(cas, Has.Count.EqualTo(2));
        }

        [Test]
        public void ListCas_ReturnsEmpty_WhenNoCas()
        {
            var cas = _db!.ListCas();
            Assert.That(cas, Is.Empty);
        }

        [Test]
        public void DeleteCa_RemovesCaAndCerts()
        {
            var caId = _db!.InsertCa(CreateSampleCa());
            _db.InsertCert(CreateSampleCert(caId));
            _db.InsertCert(CreateSampleCert(caId, "other-server", "other.example.com"));

            Assert.That(_db.ListCerts(caId), Has.Count.EqualTo(2));

            var deleted = _db.DeleteCa(caId);
            Assert.That(deleted, Is.True);
            Assert.That(_db.GetCa(caId), Is.Null);
            Assert.That(_db.ListCerts(caId), Is.Empty);
        }

        [Test]
        public void DeleteCa_ReturnsFalse_WhenNotFound()
        {
            var deleted = _db!.DeleteCa(999);
            Assert.That(deleted, Is.False);
        }

        [Test]
        public void InsertCert_ReturnsPositiveId()
        {
            var caId = _db!.InsertCa(CreateSampleCa());
            var certId = _db.InsertCert(CreateSampleCert(caId));
            Assert.That(certId, Is.GreaterThan(0));
        }

        [Test]
        public void GetCert_ReturnsInsertedRecord()
        {
            var caId = _db!.InsertCa(CreateSampleCa());
            var certId = _db.InsertCert(CreateSampleCert(caId));
            var retrieved = _db.GetCert(certId);

            Assert.That(retrieved, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(retrieved!.CaId, Is.EqualTo(caId));
                Assert.That(retrieved.Name, Is.EqualTo("web-server"));
                Assert.That(retrieved.DnsNames, Is.EqualTo("server.example.com"));
                Assert.That(retrieved.IpAddresses, Is.EqualTo("192.168.1.1"));
            });
        }

        [Test]
        public void GetCert_ReturnsNull_WhenNotFound()
        {
            var result = _db!.GetCert(999);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ListCerts_FiltersByCaId()
        {
            var ca1 = _db!.InsertCa(CreateSampleCa("ca-1"));
            var ca2 = _db.InsertCa(CreateSampleCa("ca-2"));
            _db.InsertCert(CreateSampleCert(ca1, "server-a", "a.example.com"));
            _db.InsertCert(CreateSampleCert(ca1, "server-b", "b.example.com"));
            _db.InsertCert(CreateSampleCert(ca2, "server-c", "c.example.com"));

            var ca1Certs = _db.ListCerts(ca1);
            var ca2Certs = _db.ListCerts(ca2);
            var allCerts = _db.ListCerts();

            Assert.Multiple(() =>
            {
                Assert.That(ca1Certs, Has.Count.EqualTo(2));
                Assert.That(ca2Certs, Has.Count.EqualTo(1));
                Assert.That(allCerts, Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void DeleteCert_RemovesSingleCert()
        {
            var caId = _db!.InsertCa(CreateSampleCa());
            var certId = _db.InsertCert(CreateSampleCert(caId));

            var deleted = _db.DeleteCert(certId);
            Assert.That(deleted, Is.True);
            Assert.That(_db.GetCert(certId), Is.Null);
        }

        [Test]
        public void DeleteCert_ReturnsFalse_WhenNotFound()
        {
            var deleted = _db!.DeleteCert(999);
            Assert.That(deleted, Is.False);
        }

        [Test]
        public void GetCertsByName_ReturnsCaseInsensitiveMatches()
        {
            var caId = _db!.InsertCa(CreateSampleCa());
            _db.InsertCert(CreateSampleCert(caId, "Web-Server", "server.example.com"));
            _db.InsertCert(CreateSampleCert(caId, "other-server", "other.example.com"));

            var matches = _db.GetCertsByName("web-server");
            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].Name, Is.EqualTo("Web-Server"));
        }

        [Test]
        public void GetCertsByName_ReturnsMultipleMatches()
        {
            var ca1 = _db!.InsertCa(CreateSampleCa("ca-1"));
            var ca2 = _db.InsertCa(CreateSampleCa("ca-2"));
            _db.InsertCert(CreateSampleCert(ca1, "web-server", "a.example.com"));
            _db.InsertCert(CreateSampleCert(ca2, "web-server", "b.example.com"));

            var matches = _db.GetCertsByName("web-server");
            Assert.That(matches, Has.Count.EqualTo(2));
        }

        [Test]
        public void GetCertsByName_ReturnsEmpty_WhenNoMatch()
        {
            var caId = _db!.InsertCa(CreateSampleCa());
            _db.InsertCert(CreateSampleCert(caId));

            var matches = _db.GetCertsByName("nonexistent");
            Assert.That(matches, Is.Empty);
        }

        [Test]
        public void Constructor_CreatesDatabase_WhenFileDoesNotExist()
        {
            Assert.That(File.Exists(_dbPath), Is.True);
        }

        [Test]
        public void Constructor_ThrowsOnNullPath()
        {
            try
            {
                Assert.Throws<ArgumentException>(() => new CertDatabase(null!));
            }
            catch (Exception ex) when (IsNativeLibUnavailable(ex))
            {
                Assert.Ignore("DecentDB native library not available on this platform.");
            }
        }

        [Test]
        public void Constructor_ThrowsOnEmptyPath()
        {
            try
            {
                Assert.Throws<ArgumentException>(() => new CertDatabase(string.Empty));
            }
            catch (Exception ex) when (IsNativeLibUnavailable(ex))
            {
                Assert.Ignore("DecentDB native library not available on this platform.");
            }
        }

        private static CertificateAuthorityRecord CreateSampleCa(string name = "test-ca")
        {
            return new CertificateAuthorityRecord
            {
                Name = name,
                Country = "US",
                Organization = "TestOrg",
                CertificatePem = [1, 2, 3],
                PrivateKeyPem = [4, 5, 6],
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddYears(10),
            };
        }

        private static DeviceCertificateRecord CreateSampleCert(int caId, string name = "web-server", string dnsNames = "server.example.com")
        {
            return new DeviceCertificateRecord
            {
                CaId = caId,
                Name = name,
                DnsNames = dnsNames,
                IpAddresses = "192.168.1.1",
                CertificatePem = [10, 11, 12],
                PrivateKeyPem = [13, 14, 15],
                Pfx = [16, 17, 18],
                FullChainPem = [19, 20, 21],
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(397),
            };
        }

        private static bool IsNativeLibUnavailable(Exception ex)
        {
            if (ex is DllNotFoundException)
            {
                return true;
            }

            if (ex is TypeInitializationException tie && IsNativeLibUnavailable(tie.InnerException!))
            {
                return true;
            }

            // DecentDB wraps DLL load errors in InvalidOperationException
            if (ex is InvalidOperationException && ex.InnerException is DllNotFoundException)
            {
                return true;
            }

            // Fallback: walk the inner exception chain for DllNotFoundException
            var inner = ex.InnerException;
            while (inner != null)
            {
                if (inner is DllNotFoundException)
                {
                    return true;
                }

                inner = inner.InnerException;
            }

            return false;
        }
    }
}
