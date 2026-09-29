using System;
using Blocky.Localization;
using NUnit.Framework;

namespace Blocky.Compiler.Tests
{
    /// <summary>
    /// Every test here reads English, whatever the machine's own language or the language last picked in Play mode
    /// — assertions on words would otherwise pass on one computer and fail on the next.
    /// </summary>
    [SetUpFixture]
    public sealed class EnglishForTests
    {
        private IDisposable _english;

        [OneTimeSetUp]
        public void PinEnglish() => _english = BlockyLanguages.Override(BlockyLanguages.SourceLanguage);

        [OneTimeTearDown]
        public void Unpin() => _english?.Dispose();
    }
}
