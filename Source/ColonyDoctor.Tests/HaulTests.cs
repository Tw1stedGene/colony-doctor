using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    [TestFixture]
    public class HaulTests
    {
        private static int[] Counts(params (Reason r, int n)[] items)
        {
            var c = new int[Reasons.Count];
            foreach (var (r, n) in items) c[(int)r] = n;
            return c;
        }

        [Test]
        public void FreshDropIsNotWaiting()
        {
            Assert.That(HaulReason.IsWaiting(1000, 1000 + 2499), Is.False);
            Assert.That(HaulReason.IsWaiting(1000, 1000 + 2500), Is.True);
        }

        [Test]
        public void RottingOnlyWhenFreshAndWithinADay()
        {
            Assert.That(HaulReason.IsRotting(true, 59999), Is.True);
            Assert.That(HaulReason.IsRotting(true, 60000), Is.False);
            Assert.That(HaulReason.IsRotting(false, 10), Is.False);
        }

        [Test]
        public void NoCausesIsWorking() => Assert.That(HaulReason.Majority(Counts()), Is.EqualTo(Reason.Working));

        [Test]
        public void MostItemsWins()
            => Assert.That(HaulReason.Majority(Counts((Reason.NoStorage, 1), (Reason.Outcompeted, 4))), Is.EqualTo(Reason.Outcompeted));

        [Test]
        public void TiesGoToPrecedence()
        {
            Assert.That(HaulReason.Majority(Counts((Reason.NoEligibleWorker, 2), (Reason.NoStorage, 2))), Is.EqualTo(Reason.NoStorage));
            Assert.That(HaulReason.Majority(Counts((Reason.WaitingForWorker, 3), (Reason.Outcompeted, 3))), Is.EqualTo(Reason.Outcompeted));
        }
    }
}
