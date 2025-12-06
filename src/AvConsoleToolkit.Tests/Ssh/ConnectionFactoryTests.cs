// <copyright file="ConnectionFactoryTests.cs">
// The MIT License
// Copyright © Christopher McNeely
// </copyright>

using System;
using AvConsoleToolkit.Ssh;
using NUnit.Framework;

namespace AvConsoleToolkit.Tests.Ssh
{
    [TestFixture]
    public class ConnectionFactoryTests
    {
        private IConnectionFactory? factory;

        [SetUp]
        public void Setup()
        {
            this.factory = ConnectionFactory.Instance;
            this.factory.ReleaseAll();
        }

        [TearDown]
        public void TearDown()
        {
            this.factory?.ReleaseAll();
        }

        [Test]
        public void InstanceShouldReturnSingletonInstance()
        {
            var instance1 = ConnectionFactory.Instance;
            var instance2 = ConnectionFactory.Instance;
            Assert.That(instance2, Is.SameAs(instance1));
        }

        [Test]
        public void GetSshConnectionWithPasswordShouldReturnConnection()
        {
            var connection = this.factory!.GetSshConnection("test.example.com", 22, "testuser", "testpass");
            Assert.That(connection, Is.Not.Null);
            Assert.That(connection, Is.InstanceOf<ISshConnection>());
        }

        [Test]
        public void GetSshConnectionWithSameParametersShouldReturnCachedConnection()
        {
            var connection1 = this.factory!.GetSshConnection("test.example.com", 22, "testuser", "testpass");
            var connection2 = this.factory!.GetSshConnection("test.example.com", 22, "testuser", "testpass");
            Assert.That(connection2, Is.SameAs(connection1));
        }

        [Test]
        public void GetSshConnectionWithDifferentHostShouldReturnDifferentConnection()
        {
            var connection1 = this.factory!.GetSshConnection("host1.example.com", 22, "testuser", "testpass");
            var connection2 = this.factory!.GetSshConnection("host2.example.com", 22, "testuser", "testpass");
            Assert.That(connection2, Is.Not.SameAs(connection1));
        }

        [Test]
        public void ReleaseAllShouldClearConnectionCache()
        {
            var connection1 = this.factory!.GetSshConnection("test.example.com", 22, "testuser", "testpass");
            this.factory!.ReleaseAll();
            var connection2 = this.factory!.GetSshConnection("test.example.com", 22, "testuser", "testpass");
            Assert.That(connection2, Is.Not.SameAs(connection1));
        }
    }
}
