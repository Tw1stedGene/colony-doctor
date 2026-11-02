using System;
using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    public class BillHistoryTests
    {
        private const int Hour = GameTime.TicksPerHour;
        private const int Day = GameTime.TicksPerDay;
        private const int Step = GameTime.TicksPerSample;

        /// <summary>Records one sample every 250 ticks in [from, to) using <paramref name="pick"/>.</summary>
        private static void Fill(BillHistory h, int from, int to, Func<int, Reason> pick)
        {
            for (int t = from; t < to; t += Step) h.Record(t, Diagnosis.Of(pick(t)));
        }

        [Test]
        public void OneHourOfSamples_GivesTenSamplesInHourRange()
        {
            var h = new BillHistory();
            int start = 10 * Day;
            Fill(h, start, start + Hour, _ => Reason.MaterialsMissing);
            var a = new HistoryAggregate();
            h.Aggregate(start + Hour - Step, TimeRange.Hour, a);
            Assert.That(a.ActiveSamples, Is.EqualTo(10));
            Assert.That(a.StalledPercent, Is.EqualTo(100));
            for (int i = 0; i < 10; i++) Assert.That(a.Slice(i), Is.EqualTo((byte)Reason.MaterialsMissing), $"slice {i}");
        }

        [Test]
        public void HourRingWrapsAround_KeepsOnlyLastHour()
        {
            var h = new BillHistory();
            int start = 3 * Day;
            Fill(h, start, start + Hour, _ => Reason.Working);
            Fill(h, start + Hour, start + 2 * Hour, _ => Reason.NoEligibleWorker);
            var a = new HistoryAggregate();
            h.Aggregate(start + 2 * Hour - Step, TimeRange.Hour, a);
            Assert.That(a.Count(Reason.Working), Is.EqualTo(0));
            Assert.That(a.Count(Reason.NoEligibleWorker), Is.EqualTo(10));
        }

        [Test]
        public void MixedDay_StalledPercentAndDominant()
        {
            // Spec scenario: 30% Working, 55% Materials missing, 15% No eligible worker over the last day.
            var h = new BillHistory();
            int start = 20 * Day;
            int samples = GameTime.SamplesPerHour * 24; // 240
            int i = 0;
            Fill(h, start, start + Day, _ =>
            {
                int k = i++ * 100 / samples;
                return k < 30 ? Reason.Working : k < 85 ? Reason.MaterialsMissing : Reason.NoEligibleWorker;
            });
            var a = new HistoryAggregate();
            h.Aggregate(start + Day - Step, TimeRange.Day, a);
            Assert.That(a.ActiveSamples, Is.EqualTo(240));
            Assert.That(a.StalledPercent, Is.EqualTo(70));
            Assert.That(a.Dominant, Is.EqualTo(Reason.MaterialsMissing));
            Assert.That(a.Percent(Reason.Working), Is.EqualTo(30));
            Assert.That(a.Percent(Reason.MaterialsMissing), Is.EqualTo(55));
            Assert.That(a.Percent(Reason.NoEligibleWorker), Is.EqualTo(15));
        }

        [Test]
        public void DayRange_ExcludesOlderSamples()
        {
            var h = new BillHistory();
            int start = 30 * Day;
            Fill(h, start, start + Day, _ => Reason.MaterialsMissing);
            Fill(h, start + Day, start + 2 * Day, _ => Reason.Working);
            var a = new HistoryAggregate();
            h.Aggregate(start + 2 * Day - Step, TimeRange.Day, a);
            Assert.That(a.StalledPercent, Is.EqualTo(0));
            Assert.That(a.Dominant, Is.Null);
        }

        [Test]
        public void PartialCoverage_EarlierSlicesHaveNoData()
        {
            // Spec scenario: bill created 6 hours ago, last-day range requested.
            var h = new BillHistory();
            int start = 50 * Day + 18 * Hour;
            Fill(h, start, start + 6 * Hour, _ => Reason.WaitingForWorker);
            var a = new HistoryAggregate();
            h.Aggregate(start + 6 * Hour - Step, TimeRange.Day, a);
            Assert.That(a.ActiveSamples, Is.EqualTo(60));
            Assert.That(a.StalledPercent, Is.EqualTo(100));
            for (int s = 0; s < 18; s++) Assert.That(a.Slice(s), Is.EqualTo(Reasons.NoData), $"slice {s}");
            for (int s = 18; s < 24; s++) Assert.That(a.Slice(s), Is.EqualTo((byte)Reason.WaitingForWorker), $"slice {s}");
        }

        [Test]
        public void NoSamples_HasNoData()
        {
            var a = new HistoryAggregate();
            new BillHistory().Aggregate(5 * Day, TimeRange.Quadrum, a);
            Assert.That(a.HasData, Is.False);
            Assert.That(a.StalledPercent, Is.EqualTo(-1));
        }

        [Test]
        public void SuspendedPeriod_DoesNotCountAsStalled()
        {
            // Spec scenario: suspended for a day, then resumed.
            var h = new BillHistory();
            int start = 40 * Day;
            Fill(h, start, start + Day, _ => Reason.Suspended);
            Fill(h, start + Day, start + Day + 12 * Hour, _ => Reason.Working);
            var a = new HistoryAggregate();
            h.Aggregate(start + Day + 12 * Hour - Step, TimeRange.Quadrum, a);
            Assert.That(a.StalledPercent, Is.EqualTo(0));
            Assert.That(a.ActiveSamples, Is.EqualTo(120));
            Assert.That(a.Count(Reason.Suspended), Is.EqualTo(240));
        }

        [Test]
        public void Quadrum_FifteenDailySlicesAlignedToDays()
        {
            var h = new BillHistory();
            int now = 100 * Day + 5 * Hour;
            int from = 86 * Day; // first day of the window: day 100 - 14
            Fill(h, from, now + Step, t => (t / Day) % 2 == 0 ? Reason.MaterialsMissing : Reason.Working);
            var a = new HistoryAggregate();
            h.Aggregate(now, TimeRange.Quadrum, a);
            Assert.That(a.SliceCount, Is.EqualTo(15));
            for (int s = 0; s < 15; s++)
            {
                int day = 86 + s;
                var expected = day % 2 == 0 ? Reason.MaterialsMissing : Reason.Working;
                Assert.That(a.Slice(s), Is.EqualTo((byte)expected), $"slice {s} (day {day})");
            }
        }

        [Test]
        public void Quadrum_ForgetsDataOlderThanFifteenDays()
        {
            var h = new BillHistory();
            Fill(h, 0, 10 * Day, _ => Reason.MaterialsMissing);
            Fill(h, 10 * Day, 40 * Day, _ => Reason.Working);
            var a = new HistoryAggregate();
            h.Aggregate(40 * Day - Step, TimeRange.Quadrum, a);
            Assert.That(a.Count(Reason.MaterialsMissing), Is.EqualTo(0));
            Assert.That(a.StalledPercent, Is.EqualTo(0));
        }

        [Test]
        public void ContinuousStall_TracksStartAndResetsOnWorking()
        {
            var h = new BillHistory();
            h.Record(1000, Diagnosis.Of(Reason.Working));
            h.Record(1250, Diagnosis.Of(Reason.MaterialsMissing));
            h.Record(1500, Diagnosis.Of(Reason.NoEligibleWorker));
            Assert.That(h.StallStartTick, Is.EqualTo(1250));
            Assert.That(h.ContinuousStallTicks(2000), Is.EqualTo(750));
            Assert.That(h.IsCurrentlyStalled, Is.True);
            h.Record(1750, Diagnosis.Of(Reason.Working));
            Assert.That(h.StallStartTick, Is.EqualTo(-1));
            Assert.That(h.IsCurrentlyStalled, Is.False);
        }

        [Test]
        public void LastDiagnosisPerReason_IsKept()
        {
            var h = new BillHistory();
            var d = new Diagnosis { Reason = Reason.MaterialsMissing, Detail = Detail.OutsideRadius, ValueA = 46, ValueB = 30 };
            h.Record(5000, d);
            h.Record(5250, Diagnosis.Of(Reason.Working));
            ref readonly var last = ref h.LastFor(Reason.MaterialsMissing);
            Assert.That(last.Detail, Is.EqualTo(Detail.OutsideRadius));
            Assert.That(last.ValueA, Is.EqualTo(46));
            Assert.That(last.Tick, Is.EqualTo(5000));
        }

        [Test]
        public void YearOfSamples_DoesNotGrowMemoryOrAllocate()
        {
            // Spec: Fixed memory. The history owns only fixed arrays; recording a year of samples must not allocate.
            var h = new BillHistory();
            var d = Diagnosis.Of(Reason.MaterialsMissing, Detail.Forbidden);
            for (int t = 0; t < 15 * Day; t += Step) h.Record(t, d); // warm-up: first quadrum

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int gen0 = GC.CollectionCount(0);
            long before = GC.GetTotalMemory(false);
            int year = 60 * Day;
            for (int t = 15 * Day; t < 15 * Day + year; t += Step)
            {
                d.Reason = (t / Step) % 3 == 0 ? Reason.Working : Reason.MaterialsMissing;
                h.Record(t, d);
            }
            long after = GC.GetTotalMemory(false);
            Assert.That(GC.CollectionCount(0), Is.EqualTo(gen0), "a GC happened while recording");
            Assert.That(after - before, Is.LessThan(64 * 1024), "recording allocated memory");
        }

        [Test]
        public void Aggregate_ReusesResultWithoutAllocating()
        {
            var h = new BillHistory();
            for (int t = 0; t < 20 * Day; t += Step) h.Record(t, Diagnosis.Of(Reason.WaitingForWorker));
            var a = new HistoryAggregate();
            h.Aggregate(20 * Day, TimeRange.Quadrum, a); // warm-up
            GC.Collect();
            int gen0 = GC.CollectionCount(0);
            long before = GC.GetTotalMemory(false);
            for (int i = 0; i < 10000; i++) h.Aggregate(20 * Day, (TimeRange)(i % 3), a);
            Assert.That(GC.CollectionCount(0), Is.EqualTo(gen0));
            Assert.That(GC.GetTotalMemory(false) - before, Is.LessThan(64 * 1024));
        }
    }
}
