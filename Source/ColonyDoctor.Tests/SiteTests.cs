using System.Collections.Generic;
using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    public class SiteReasonTests
    {
        private static int[] Counts(params (Reason r, int n)[] items)
        {
            var c = new int[Reasons.Count];
            foreach (var (r, n) in items) c[(int)r] = n;
            return c;
        }

        [Test]
        public void MostPiecesWins()
            => Assert.That(SiteReason.Majority(Counts((Reason.MaterialsMissing, 12), (Reason.Blocked, 2))),
                Is.EqualTo(Reason.MaterialsMissing));

        [Test]
        public void TieGoesToPrecedence()
            => Assert.That(SiteReason.Majority(Counts((Reason.MaterialsMissing, 5), (Reason.Blocked, 5))),
                Is.EqualTo(Reason.Blocked));

        [Test]
        public void NoWorkerBeatsEverythingOnTie()
            => Assert.That(SiteReason.Majority(Counts((Reason.WaitingForWorker, 3), (Reason.NoEligibleWorker, 3))),
                Is.EqualTo(Reason.NoEligibleWorker));

        [Test]
        public void NothingStalled_IsWorking()
            => Assert.That(SiteReason.Majority(Counts()), Is.EqualTo(Reason.Working));
    }

    public class SiteMatcherTests
    {
        private static List<int> Cells(int from, int count)
        {
            var l = new List<int>();
            for (int i = 0; i < count; i++) l.Add(from + i);
            return l;
        }

        private static Dictionary<int, int> Owners(params List<int>[] oldSites)
        {
            var d = new Dictionary<int, int>();
            for (int s = 0; s < oldSites.Length; s++)
                foreach (int c in oldSites[s]) d[c] = s;
            return d;
        }

        private static List<int> Run(Dictionary<int, int> owners, params List<int>[] newSites)
        {
            var result = new List<int>();
            new SiteMatcher().Match(newSites, owners, result);
            return result;
        }

        [Test]
        public void SameCells_KeepIdentity()
            => Assert.That(Run(Owners(Cells(0, 14)), Cells(0, 14)), Is.EqualTo(new[] { 0 }));

        [Test]
        public void PartlyBuilt_KeepsIdentity()
            // 3 of 14 walls finished: the remaining 11 cells still match (spec: Walls get built one by one).
            => Assert.That(Run(Owners(Cells(0, 14)), Cells(3, 11)), Is.EqualTo(new[] { 0 }));

        [Test]
        public void Merge_KeepsLargerPartsHistory()
        {
            // Old sites A (10 cells) and B (4 cells) joined by new pieces into one site.
            var owners = Owners(Cells(0, 10), Cells(20, 4));
            var merged = Cells(0, 10);
            merged.AddRange(Cells(10, 10));
            merged.AddRange(Cells(20, 4));
            Assert.That(Run(owners, merged), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void Split_LargestPartKeepsHistory_OthersFresh()
        {
            var owners = Owners(Cells(0, 20));
            Assert.That(Run(owners, Cells(0, 5), Cells(10, 10)), Is.EqualTo(new[] { -1, 0 }));
        }

        [Test]
        public void NewSite_StartsFresh()
            => Assert.That(Run(Owners(Cells(0, 5)), Cells(100, 5)), Is.EqualTo(new[] { -1 }));

        [Test]
        public void TwoUnrelatedSites_EachKeepTheirOwn()
            => Assert.That(Run(Owners(Cells(0, 5), Cells(50, 5)), Cells(50, 5), Cells(0, 5)), Is.EqualTo(new[] { 1, 0 }));
    }
}
