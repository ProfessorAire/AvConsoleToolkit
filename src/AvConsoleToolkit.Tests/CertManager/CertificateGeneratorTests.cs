using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using AvConsoleToolkit.CertManager;
using NUnit.Framework;

namespace AvConsoleToolkit.Tests.CertManager
{
    [TestFixture]
    public sealed class CertificateGeneratorTests
    {
        [Test]
        public void CreateCaCertificate_ReturnsValidCert()
        {
            using var cert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca", 365);

            Assert.That(cert, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(cert.HasPrivateKey, Is.True);
                Assert.That(cert.Subject, Does.Contain("CN=TestOrg Private Root CA"));
                Assert.That(cert.Subject, Does.Contain("O=TestOrg"));
                Assert.That(cert.Subject, Does.Contain("C=US"));
                Assert.That(cert.NotAfter, Is.GreaterThan(DateTime.UtcNow.AddDays(360)));
            });
        }

        [Test]
        public void CreateCaCertificate_HasBasicConstraintsCa()
        {
            using var cert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");

            var basicConstraints = cert.Extensions["2.5.29.19"] as X509BasicConstraintsExtension;
            Assert.That(basicConstraints, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(basicConstraints!.CertificateAuthority, Is.True);
                Assert.That(basicConstraints.Critical, Is.True);
            });
        }

        [Test]
        public void CreateDeviceCertificate_ReturnsValidCert()
        {
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert, ["server.example.com"], ["192.168.1.100"], "US", "TestOrg", 365);

            Assert.That(deviceCert, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(deviceCert.HasPrivateKey, Is.True);
                Assert.That(deviceCert.Subject, Does.Contain("CN=server.example.com"));
                Assert.That(deviceCert.Issuer, Does.Contain("CN=TestOrg Private Root CA"));
            });
        }

        [Test]
        public void CreateDeviceCertificate_IsNotCA()
        {
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert, ["server.example.com"], ["192.168.1.100"], "US", "TestOrg");

            var basicConstraints = deviceCert.Extensions["2.5.29.19"] as X509BasicConstraintsExtension;
            Assert.That(basicConstraints, Is.Not.Null);
            Assert.That(basicConstraints!.CertificateAuthority, Is.False);
        }

        [Test]
        public void CreateDeviceCertificate_ContainsSanDns()
        {
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert, ["myhost.local"], ["10.0.0.1"], "US", "TestOrg");

            // Check SAN extension exists (OID 2.5.29.17)
            var sanExt = deviceCert.Extensions["2.5.29.17"];
            Assert.That(sanExt, Is.Not.Null);

            // The formatted SAN should contain the DNS name
            var sanFormatted = sanExt!.Format(true);
            Assert.That(sanFormatted, Does.Contain("myhost.local"));
        }

        [Test]
        public void CreateDeviceCertificate_ContainsMultipleDnsNames()
        {
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert, ["server.example.com", "server.local", "server"], [], "US", "TestOrg");

            var sanExt = deviceCert.Extensions["2.5.29.17"];
            Assert.That(sanExt, Is.Not.Null);

            var sanFormatted = sanExt!.Format(true);
            Assert.Multiple(() =>
            {
                Assert.That(sanFormatted, Does.Contain("server.example.com"));
                Assert.That(sanFormatted, Does.Contain("server.local"));
                Assert.That(sanFormatted, Does.Contain("server"));
            });
        }

        [Test]
        public void CreateDeviceCertificate_ContainsSanIp()
        {
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert, ["myhost.local"], ["10.0.0.1", "192.168.1.50"], "US", "TestOrg");

            var sanExt = deviceCert.Extensions["2.5.29.17"];
            Assert.That(sanExt, Is.Not.Null);

            var sanFormatted = sanExt!.Format(true);
            Assert.That(sanFormatted, Does.Contain("10.0.0.1"));
            Assert.That(sanFormatted, Does.Contain("192.168.1.50"));
        }

        [Test]
        public void ExportCertificatePem_ReturnsValidPem()
        {
            using var cert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            var pem = CertificateGenerator.ExportCertificatePem(cert);
            var pemText = System.Text.Encoding.UTF8.GetString(pem);

            Assert.Multiple(() =>
            {
                Assert.That(pem, Has.Length.GreaterThan(0));
                Assert.That(pemText, Does.Contain("BEGIN CERTIFICATE"));
                Assert.That(pemText, Does.Contain("END CERTIFICATE"));
            });
        }

        [Test]
        public void ExportPrivateKeyPem_ReturnsValidPem()
        {
            using var cert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            var keyPem = CertificateGenerator.ExportPrivateKeyPem(cert);
            var keyText = System.Text.Encoding.UTF8.GetString(keyPem);

            Assert.Multiple(() =>
            {
                Assert.That(keyPem, Has.Length.GreaterThan(0));
                Assert.That(keyText, Does.Contain("BEGIN RSA PRIVATE KEY"));
                Assert.That(keyText, Does.Contain("END RSA PRIVATE KEY"));
            });
        }

        [Test]
        public void ExportPfx_ReturnsNonEmptyBytes()
        {
            using var cert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            var pfx = CertificateGenerator.ExportPfx(cert, "testpassword");

            Assert.That(pfx, Has.Length.GreaterThan(0));
        }

        [Test]
        public void LoadCaCertificate_RoundTrips()
        {
            using var original = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            var certPem = CertificateGenerator.ExportCertificatePem(original);
            var keyPem = CertificateGenerator.ExportPrivateKeyPem(original);

            using var loaded = CertificateGenerator.LoadCaCertificate(certPem, keyPem);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Subject, Is.EqualTo(original.Subject));
                Assert.That(loaded.HasPrivateKey, Is.True);
            });
        }

        [Test]
        public void BuildFullChainPem_CombinesCertAndCa()
        {
            var certPem = System.Text.Encoding.UTF8.GetBytes("CERT-PEM-DATA");
            var caPem = System.Text.Encoding.UTF8.GetBytes("CA-PEM-DATA");

            var fullChain = CertificateGenerator.BuildFullChainPem(certPem, caPem);
            var fullChainText = System.Text.Encoding.UTF8.GetString(fullChain);

            Assert.That(fullChainText, Is.EqualTo("CERT-PEM-DATACA-PEM-DATA"));
        }

        [Test]
        public void CreateDeviceCertificate_WithMultipleIps()
        {
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert, ["multi.local"], ["192.168.1.1", "10.0.0.1", "172.16.0.1"], "US", "TestOrg");

            Assert.That(deviceCert.HasPrivateKey, Is.True);
            Assert.That(deviceCert.Subject, Does.Contain("CN=multi.local"));
        }

        [Test]
        public void CreateDeviceCertificate_WithEmptyIps()
        {
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                caCert, ["no-ip.local"], [], "US", "TestOrg");

            Assert.That(deviceCert.HasPrivateKey, Is.True);
            Assert.That(deviceCert.Subject, Does.Contain("CN=no-ip.local"));
        }

        [Test]
        public void FullWorkflow_CreateCa_CreateDevice_ExportAll()
        {
            // Create CA
            using var caCert = CertificateGenerator.CreateCaCertificate("US", "TestOrg", "test-ca");
            var caCertPem = CertificateGenerator.ExportCertificatePem(caCert);
            var caKeyPem = CertificateGenerator.ExportPrivateKeyPem(caCert);

            // Reload CA from PEM (simulating loading from database)
            using var loadedCa = CertificateGenerator.LoadCaCertificate(caCertPem, caKeyPem);

            // Create device cert
            using var deviceCert = CertificateGenerator.CreateDeviceCertificate(
                loadedCa, ["device.local"], ["192.168.1.50"], "US", "TestOrg");

            var devicePem = CertificateGenerator.ExportCertificatePem(deviceCert);
            var deviceKeyPem = CertificateGenerator.ExportPrivateKeyPem(deviceCert);
            var devicePfx = CertificateGenerator.ExportPfx(deviceCert, "pass123");
            var fullChain = CertificateGenerator.BuildFullChainPem(devicePem, caCertPem);

            Assert.Multiple(() =>
            {
                Assert.That(devicePem, Has.Length.GreaterThan(0));
                Assert.That(deviceKeyPem, Has.Length.GreaterThan(0));
                Assert.That(devicePfx, Has.Length.GreaterThan(0));
                Assert.That(fullChain, Has.Length.GreaterThan(devicePem.Length));
            });
        }
    }
}
