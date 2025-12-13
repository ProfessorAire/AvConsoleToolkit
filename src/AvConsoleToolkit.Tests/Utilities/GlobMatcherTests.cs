using System;
using System.Collections.Generic;
using AvConsoleToolkit.Utilities;
using NUnit.Framework;

namespace AvConsoleToolkit.Tests.Utilities
{
    [TestFixture]
    public sealed class GlobMatcherTests
    {
        [TestCase("file*.txt", "file.txt", true)]
        [TestCase("file*.txt", "file123.txt", true)]
        [TestCase("file*.txt", "folder/file.txt", false)]
        [TestCase("logs/*.txt", "logs/app.txt", true)]
        [TestCase("logs/*.txt", "logs/sub/app.txt", false)]
        public void SingleAsteriskMatchesWithinSegment(string pattern, string path, bool expected)
        {
            Assert.That(GlobMatcher.IsMatch(pattern, path), Is.EqualTo(expected));
        }

        [TestCase("logs/**/*.txt", "logs/app.txt", true)]
        [TestCase("logs/**/*.txt", "logs/sub/app.txt", true)]
        [TestCase("logs/**/*.txt", "logs/sub/deep/app.txt", true)]
        [TestCase("logs/**/*.txt", "data/logs/app.txt", false)]
        public void DoubleAsteriskMatchesAcrossDirectories(string pattern, string path, bool expected)
        {
            Assert.That(GlobMatcher.IsMatch(pattern, path), Is.EqualTo(expected));
        }

        [TestCase("file?.txt", "file1.txt", true)]
        [TestCase("file?.txt", "fileA.txt", true)]
        [TestCase("file?.txt", "file12.txt", false)]
        public void QuestionMarkMatchesExactlyOneCharacter(string pattern, string path, bool expected)
        {
            Assert.That(GlobMatcher.IsMatch(pattern, path), Is.EqualTo(expected));
        }

        [Test]
        public void CharacterListMatchesExpectedCharacters()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("file_[abc].txt", "file_a.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file_[abc].txt", "file_c.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file_[abc].txt", "file_d.txt"), Is.False);
            });
        }

        [Test]
        public void CharacterRangeMatchesExpectedCharacters()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("program_[a-z].cpz", "program_a.cpz"), Is.True);
                Assert.That(GlobMatcher.IsMatch("program_[a-z].cpz", "program_x.cpz"), Is.True);
                Assert.That(GlobMatcher.IsMatch("program_[a-z].cpz", "program_A.cpz", caseSensitive: true), Is.False);
            });
        }

        [Test]
        public void CombinedCharacterClassesMatchExpectedPatterns()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("build_[A-Z][0-9].lpz", "build_A1.lpz"), Is.True);
                Assert.That(GlobMatcher.IsMatch("build_[A-Z][0-9].lpz", "build_Z9.lpz"), Is.True);
                Assert.That(GlobMatcher.IsMatch("build_[A-Z][0-9].lpz", "build_a1.lpz", caseSensitive: true), Is.False);
                Assert.That(GlobMatcher.IsMatch("build_[A-Z][0-9].lpz", "build_A10.lpz"), Is.False);
            });
        }

        [Test]
        public void NegatedCharacterListExcludesSpecifiedCharacters()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("test_[!0-9].txt", "test_a.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("test_[!0-9].txt", "test_x.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("test_[!0-9].txt", "test_5.txt"), Is.False);
            });
        }

        [Test]
        public void NegatedCharacterRangeExcludesRange()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("log_[!a-z].dat", "log_1.dat"), Is.True);
                Assert.That(GlobMatcher.IsMatch("log_[!a-z].dat", "log_A.dat", caseSensitive: true), Is.True);
                Assert.That(GlobMatcher.IsMatch("log_[!a-z].dat", "log_x.dat"), Is.False);
            });
        }

        [Test]
        public void MixedPatternWithRangesAndWildcardsMatches()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("src/**/release/*.[ch]", "src/release/main.c"), Is.True);
                Assert.That(GlobMatcher.IsMatch("src/**/release/*.[ch]", "src/app/release/module.h"), Is.True);
                Assert.That(GlobMatcher.IsMatch("src/**/release/*.[ch]", "src/app/release/module.cpp"), Is.False);
            });
        }

        [Test]
        public void FilterMatchesReturnsOnlyMatchingPaths()
        {
            var paths = new List<string>
            {
                "logs/app.log",
                "logs/archive/old.log",
                "data/data.log"
            };

            var matches = GlobMatcher.FilterMatches("logs/**/*.log", paths);

            Assert.That(matches, Is.EquivalentTo(new[] { "logs/app.log", "logs/archive/old.log" }));
        }

        [Test]
        public void IsMatchCanBeCaseSensitiveWhenRequested()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("Program?.CPZ", "ProgramA.cpz"), Is.True);
                Assert.That(GlobMatcher.IsMatch("Program?.CPZ", "ProgramA.cpz", caseSensitive: true), Is.False);
                Assert.That(GlobMatcher.IsMatch("Program?.CPZ", "ProgramA.CPZ", caseSensitive: true), Is.True);
            });
        }

        [Test]
        public void IsMatchThrowsOnNullPattern()
        {
            Assert.Throws<ArgumentNullException>(() => GlobMatcher.IsMatch(null!, "test.txt"));
        }

        [Test]
        public void IsMatchThrowsOnEmptyPattern()
        {
            Assert.Throws<ArgumentNullException>(() => GlobMatcher.IsMatch(string.Empty, "test.txt"));
        }

        [Test]
        public void IsMatchReturnsFalseForNullPath()
        {
            Assert.That(GlobMatcher.IsMatch("*.txt", null!), Is.False);
        }

        [Test]
        public void IsMatchReturnsFalseForEmptyPath()
        {
            Assert.That(GlobMatcher.IsMatch("*.txt", string.Empty), Is.False);
        }

        [Test]
        public void UnmatchedOpeningBracketTreatedAsLiteral()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("file[abc.txt", "file[abc.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file[abc.txt", "filea.txt"), Is.False);
            });
        }

        [Test]
        public void DoubleAsteriskAtEndOfPattern()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("logs/**", "logs/app.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("logs/**", "logs/sub/app.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("logs/**", "logs/"), Is.True);
                Assert.That(GlobMatcher.IsMatch("logs/**", "data/app.txt"), Is.False);
            });
        }

        [Test]
        public void DoubleAsteriskInMiddleWithoutSeparator()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("logs**txt", "logs/app.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("logs**txt", "logsapp.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("logs**txt", "logs/sub/app.txt"), Is.True);
            });
        }

        [Test]
        public void BackslashesNormalizedToForwardSlashes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch(@"logs\*.txt", "logs/app.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("logs/*.txt", @"logs\app.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch(@"logs\**\*.txt", @"logs\sub\app.txt"), Is.True);
            });
        }

        [Test]
        public void EmptyCharacterClass()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("file_[].txt", "file_[].txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file_[].txt", "file_a.txt"), Is.False);
            });
        }

        [Test]
        public void MultipleWildcardsInPattern()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("*/*/*.txt", "a/b/c.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("*/*/*.txt", "a/b.txt"), Is.False);
                Assert.That(GlobMatcher.IsMatch("*/*/*.txt", "a/b/c/d.txt"), Is.False);
            });
        }

        [Test]
        public void QuestionMarkDoesNotMatchDirectorySeparator()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("file?.txt", "file/.txt"), Is.False);
                Assert.That(GlobMatcher.IsMatch("file?.txt", "filea.txt"), Is.True);
            });
        }

        [Test]
        public void DoubleAsteriskAtBeginningOfPattern()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("**/*.txt", "file.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("**/*.txt", "sub/file.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("**/*.txt", "a/b/c/file.txt"), Is.True);
            });
        }

        [Test]
        public void MultipleDoubleAsterisksInPattern()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("**/sub/**/*.txt", "sub/file.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("**/sub/**/*.txt", "a/sub/b/file.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("**/sub/**/*.txt", "a/b/sub/c/d/file.txt"), Is.True);
            });
        }

        [Test]
        public void ExactMatch()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("file.txt", "file.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file.txt", "file.log"), Is.False);
                Assert.That(GlobMatcher.IsMatch("path/to/file.txt", "path/to/file.txt"), Is.True);
            });
        }

        [Test]
        public void SpecialRegexCharactersAreEscaped()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("file.txt", "file.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file.txt", "fileatxt"), Is.False);
                Assert.That(GlobMatcher.IsMatch("file(1).txt", "file(1).txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file+.txt", "file+.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file^.txt", "file^.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file$.txt", "file$.txt"), Is.True);
            });
        }

        [Test]
        public void FilterMatchesWithEmptyCollection()
        {
            var paths = new List<string>();
            var matches = GlobMatcher.FilterMatches("*.txt", paths);
            Assert.That(matches, Is.Empty);
        }

        [Test]
        public void FilterMatchesWithNoMatches()
        {
            var paths = new List<string> { "file.log", "data.xml", "app.json" };
            var matches = GlobMatcher.FilterMatches("*.txt", paths);
            Assert.That(matches, Is.Empty);
        }

        [Test]
        public void FilterMatchesPreservesOrder()
        {
            var paths = new List<string>
            {
                "a.txt",
                "b.log",
                "c.txt",
                "d.xml",
                "e.txt"
            };

            var matches = GlobMatcher.FilterMatches("*.txt", paths).ToList();
            Assert.That(matches, Is.EqualTo(new[] { "a.txt", "c.txt", "e.txt" }));
        }

        [Test]
        public void ComplexNestedPatterns()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("src/**/test_[0-9][0-9].?pz", "src/tests/test_01.cpz"), Is.True);
                Assert.That(GlobMatcher.IsMatch("src/**/test_[0-9][0-9].?pz", "src/a/b/c/test_99.lpz"), Is.True);
                Assert.That(GlobMatcher.IsMatch("src/**/test_[0-9][0-9].?pz", "src/test_1.cpz"), Is.False);
                Assert.That(GlobMatcher.IsMatch("src/**/test_[0-9][0-9].?pz", "src/test_01.xyz"), Is.False);
            });
        }

        [Test]
        public void CaseInsensitiveByDefault()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GlobMatcher.IsMatch("File.TXT", "file.txt"), Is.True);
                Assert.That(GlobMatcher.IsMatch("file.txt", "FILE.TXT"), Is.True);
                Assert.That(GlobMatcher.IsMatch("*.TXT", "file.txt"), Is.True);
            });
        }
    }
}
