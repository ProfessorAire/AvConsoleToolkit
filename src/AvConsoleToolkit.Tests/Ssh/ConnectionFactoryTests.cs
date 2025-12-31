using System;
using AvConsoleToolkit.Connections;
using NUnit.Framework;

namespace AvConsoleToolkit.Tests.Connections
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
        public void GetCompositeConnectionWithPasswordShouldReturnConnection()
        {
            var connection = this.factory!.GetCompositeConnection("test.example.com", 22, "testuser", "testpass");
            Assert.That(connection, Is.Not.Null);
            Assert.That(connection, Is.InstanceOf<ICompositeConnection>());
        }

        [Test]
        public void GetCompositeConnectionWithSameParametersShouldReturnCachedConnection()
        {
            var connection1 = this.factory!.GetCompositeConnection("test.example.com", 22, "testuser", "testpass");
            var connection2 = this.factory!.GetCompositeConnection("test.example.com", 22, "testuser", "testpass");
            Assert.That(connection2, Is.SameAs(connection1));
        }

        [Test]
        public void GetCompositeConnectionWithDifferentHostShouldReturnDifferentConnection()
        {
            var connection1 = this.factory!.GetCompositeConnection("host1.example.com", 22, "testuser", "testpass");
            var connection2 = this.factory!.GetCompositeConnection("host2.example.com", 22, "testuser", "testpass");
            Assert.That(connection2, Is.Not.SameAs(connection1));
        }

        [Test]
        public void ReleaseAllShouldClearConnectionCache()
        {
            var connection1 = this.factory!.GetCompositeConnection("test.example.com", 22, "testuser", "testpass");
            this.factory!.ReleaseAll();
            var connection2 = this.factory!.GetCompositeConnection("test.example.com", 22, "testuser", "testpass");
            Assert.That(connection2, Is.Not.SameAs(connection1));
        }
    }
}
