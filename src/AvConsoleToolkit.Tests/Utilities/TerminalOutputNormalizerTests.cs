using AvConsoleToolkit.Utilities;
using NUnit.Framework;

namespace AvConsoleToolkit.Tests.Utilities
{
    [TestFixture]
    public sealed class TerminalOutputNormalizerTests
    {
        [Test]
        public void NormalizeForDisplay_RemovesCarriageReturnsFromProgressOutput()
        {
            const string value = "Completing program stop:01\r........................................\r\n";

            var normalized = TerminalOutputNormalizer.NormalizeForDisplay(value);

            Assert.That(normalized, Is.EqualTo("Completing program stop:01........................................\n"));
        }

        [Test]
        public void NormalizeCapturedOutput_PreservesActualNewLinesAndRemovesPromptEcho()
        {
            const string value = "Program Boot Directory:01: \\SIMPL\\app01\nLoading Application:01........\nACT> ";

            var normalized = TerminalOutputNormalizer.NormalizeCapturedOutput(value, "ACT>");

            Assert.That(normalized, Is.EqualTo("Program Boot Directory:01: \\SIMPL\\app01\nLoading Application:01........"));
        }
    }
}
