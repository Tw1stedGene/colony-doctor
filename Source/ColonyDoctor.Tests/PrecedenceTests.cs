using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    public class PrecedenceTests
    {
        [Test]
        public void UnpoweredBenchWithNoMaterials_IsBenchProblem()
            => Assert.That(Reasons.Pick(benchProblem: true, noEligibleWorker: false, materialsMissing: true),
                Is.EqualTo(Reason.BenchProblem));

        [Test]
        public void NoWorkerAndNoMaterials_IsNoEligibleWorker()
            => Assert.That(Reasons.Pick(false, true, true), Is.EqualTo(Reason.NoEligibleWorker));

        [Test]
        public void MaterialsAlone()
            => Assert.That(Reasons.Pick(false, false, true), Is.EqualTo(Reason.MaterialsMissing));

        [Test]
        public void NothingBlocking_IsWaitingForWorker()
            => Assert.That(Reasons.Pick(false, false, false), Is.EqualTo(Reason.WaitingForWorker));

        [Test]
        public void EverythingBlocking_IsBenchProblem()
            => Assert.That(Reasons.Pick(true, true, true), Is.EqualTo(Reason.BenchProblem));

        [TestCase(Reason.Working, false)]
        [TestCase(Reason.Suspended, false)]
        [TestCase(Reason.MaterialsMissing, true)]
        [TestCase(Reason.WaitingForWorker, true)]
        [TestCase(Reason.Unknown, true)]
        public void IsStalled(Reason reason, bool expected) => Assert.That(Reasons.IsStalled(reason), Is.EqualTo(expected));
    }
}
