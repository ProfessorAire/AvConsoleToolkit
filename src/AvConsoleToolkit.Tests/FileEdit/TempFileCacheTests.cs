// <copyright file="TempFileCacheTests.cs">
// The MIT License
// Copyright © Christopher McNeely
// </copyright>

using System;
using System.IO;
using AvConsoleToolkit.Commands.Crestron.FileEdit;
using NUnit.Framework;

namespace AvConsoleToolkit.Tests.FileEdit
{
    [TestFixture]
    public class TempFileCacheTests
    {
        [Test]
        public void InstanceShouldReturnSingletonInstance()
        {
            var instance1 = TempFileCache.Instance;
            var instance2 = TempFileCache.Instance;
            Assert.That(instance2, Is.SameAs(instance1));
        }

        [Test]
        public void GetOrCreateCachePathShouldReturnConsistentPath()
        {
            var cache = TempFileCache.Instance;
            var path1 = cache.GetOrCreateCachePath("192.168.1.100", "program01/config.xml");
            var path2 = cache.GetOrCreateCachePath("192.168.1.100", "program01/config.xml");

            Assert.That(path2, Is.EqualTo(path1));
        }

        [Test]
        public void GetOrCreateCachePathShouldReturnDifferentPathsForDifferentHosts()
        {
            var cache = TempFileCache.Instance;
            var path1 = cache.GetOrCreateCachePath("192.168.1.100", "program01/config.xml");
            var path2 = cache.GetOrCreateCachePath("192.168.1.200", "program01/config.xml");

            Assert.That(path2, Is.Not.EqualTo(path1));
        }

        [Test]
        public void GetOrCreateCachePathShouldReturnDifferentPathsForDifferentRemotePaths()
        {
            var cache = TempFileCache.Instance;
            var path1 = cache.GetOrCreateCachePath("192.168.1.100", "program01/config.xml");
            var path2 = cache.GetOrCreateCachePath("192.168.1.100", "program02/config.xml");

            Assert.That(path2, Is.Not.EqualTo(path1));
        }

        [Test]
        public void GetCachedFilePathShouldReturnNullWhenNotCached()
        {
            var cache = TempFileCache.Instance;
            var result = cache.GetCachedFilePath("not-cached-host", "not-cached-file.txt");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetCachedFilePathShouldReturnPathWhenCachedAndFileExists()
        {
            var cache = TempFileCache.Instance;
            var hostAddress = "test-host-" + Guid.NewGuid();
            var remotePath = "test-file-" + Guid.NewGuid() + ".txt";

            // Create the cached path and write a file there
            var localPath = cache.GetOrCreateCachePath(hostAddress, remotePath);
            File.WriteAllText(localPath, "test content");

            try
            {
                var result = cache.GetCachedFilePath(hostAddress, remotePath);
                Assert.That(result, Is.EqualTo(localPath));
            }
            finally
            {
                // Cleanup
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }
            }
        }

        [Test]
        public void RemoveFromCacheShouldRemoveCachedPath()
        {
            var cache = TempFileCache.Instance;
            var hostAddress = "remove-test-host-" + Guid.NewGuid();
            var remotePath = "remove-test-file-" + Guid.NewGuid() + ".txt";

            // Create a cached file
            var localPath = cache.GetOrCreateCachePath(hostAddress, remotePath);
            File.WriteAllText(localPath, "test content");

            // Verify it's cached
            Assert.That(cache.GetCachedFilePath(hostAddress, remotePath), Is.EqualTo(localPath));

            // Remove from cache
            cache.RemoveFromCache(hostAddress, remotePath);

            // Verify it's no longer cached and file is deleted
            Assert.That(cache.GetCachedFilePath(hostAddress, remotePath), Is.Null);
            Assert.That(File.Exists(localPath), Is.False);
        }
    }
}
