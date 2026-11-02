using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    public class OutcompetedTests
    {
        // Work rank (spec: Work rank)
        [Test]
        public void LowerPriorityNumberRanksFirst()
            => Assert.That(Outcompeted.RankKey(false, 1, 10, 0), Is.LessThan(Outcompeted.RankKey(false, 2, 1000, 1000)));

        [Test]
        public void SamePriorityHigherNaturalPriorityFirst() // Crafting (natural 1100?) vs Hauling: higher natural first
            => Assert.That(Outcompeted.RankKey(false, 3, 500, 0), Is.LessThan(Outcompeted.RankKey(false, 3, 300, 0)));

        [Test]
        public void SameTypeHigherPriorityInTypeFirst()
            => Assert.That(Outcompeted.RankKey(false, 3, 500, 80), Is.LessThan(Outcompeted.RankKey(false, 3, 500, 60)));

        [Test]
        public void EmergencyBeatsEverything()
            => Assert.That(Outcompeted.RankKey(true, 4, 0, 0), Is.LessThan(Outcompeted.RankKey(false, 1, 9999, 9999)));

        [Test]
        public void DisabledRanksLast()
            => Assert.That(Outcompeted.RankKey(false, 0, 9999, 9999), Is.GreaterThan(Outcompeted.RankKey(false, 4, 0, 0)));

        // Decision (spec: Outcompeted decision)
        [Test]
        public void OnlyCrafterDoctoring()
            => Assert.That(Outcompeted.Decide(higher: 1, sameOrLower: 0, offDuty: 0, idle: 0, out _), Is.EqualTo(Reason.Outcompeted));

        [Test]
        public void OneAsleepOtherHigher()
            => Assert.That(Outcompeted.Decide(1, 0, 1, 0, out _), Is.EqualTo(Reason.Outcompeted));

        [Test]
        public void SomeoneIdle()
        {
            Assert.That(Outcompeted.Decide(1, 0, 0, 1, out Detail d), Is.EqualTo(Reason.WaitingForWorker));
            Assert.That(d, Is.EqualTo(Detail.SameOrLowerRank));
        }

        [Test]
        public void EveryoneAsleep()
        {
            Assert.That(Outcompeted.Decide(0, 0, 3, 0, out Detail d), Is.EqualTo(Reason.WaitingForWorker));
            Assert.That(d, Is.EqualTo(Detail.OffDuty));
        }

        [Test]
        public void SameRankWork()
        {
            Assert.That(Outcompeted.Decide(0, 1, 2, 0, out Detail d), Is.EqualTo(Reason.WaitingForWorker));
            Assert.That(d, Is.EqualTo(Detail.SameOrLowerRank));
        }

        [Test]
        public void SitePrecedencePutsOutcompetedBeforeWaiting()
        {
            var counts = new int[Reasons.Count];
            counts[(int)Reason.Outcompeted] = 3;
            counts[(int)Reason.WaitingForWorker] = 3;
            Assert.That(SiteReason.Majority(counts), Is.EqualTo(Reason.Outcompeted));
        }

        // Tally (spec: Competing work tally)
        [Test]
        public void TallySharesAndTop()
        {
            var t = new CompetingTally();
            object latch = "Latch", bob = "Bob";
            for (int i = 0; i < 14; i++) t.Add(2, 10, latch); // Doctor
            for (int i = 0; i < 6; i++) t.Add(5, 10, bob);    // Hauling
            Assert.That(t.Total, Is.EqualTo(20));
            Assert.That(t.CountFor(2) * 100 / t.Total, Is.EqualTo(70));
            var top = new int[3];
            Assert.That(t.TopWorkTypes(top), Is.EqualTo(2));
            Assert.That(top[0], Is.EqualTo(2));
            Assert.That(top[1], Is.EqualTo(5));
            Assert.That(t.TopCompetitor, Is.SameAs(latch));
        }

        [Test]
        public void TallyResets()
        {
            var t = new CompetingTally();
            t.Add(1, 4, "x");
            t.Reset();
            Assert.That(t.Total, Is.EqualTo(0));
            Assert.That(t.CountFor(1), Is.EqualTo(0));
            Assert.That(t.TopCompetitor, Is.Null);
            Assert.That(t.TopWorkTypes(new int[3]), Is.EqualTo(0));
        }

        [Test]
        public void TallyEvictsLeastFrequentCompetitor()
        {
            var t = new CompetingTally();
            var pawns = new object[CompetingTally.CompetitorSlots];
            for (int p = 0; p < pawns.Length; p++)
            {
                pawns[p] = new object();
                for (int k = 0; k <= p; k++) t.Add(0, 1, pawns[p]);
            }
            var newcomer = new object();
            t.Add(0, 1, newcomer);
            t.Add(0, 1, newcomer);
            Assert.That(t.TopCompetitor, Is.SameAs(pawns[pawns.Length - 1]));
        }
    }
}
