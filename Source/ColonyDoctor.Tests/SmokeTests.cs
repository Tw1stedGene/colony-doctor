using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    public class SmokeTests
    {
        [Test]
        public void CoreAssemblyIsCompiledIn() => Assert.That(Reasons.Count, Is.EqualTo(8));
    }
}
