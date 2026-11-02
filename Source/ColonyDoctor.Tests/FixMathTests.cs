using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    public class FixMathTests
    {
        [TestCase(42.3f, 45)]
        [TestCase(10f, 12)]
        [TestCase(2000f, 999)]
        public void Radius(float dist, int expected) => Assert.That(FixMath.RadiusFor(dist), Is.EqualTo(expected));

        [Test]
        public void SkillRangeExcludesBestCrafter() // spec: 10–20, best 7 → 7–20
        {
            int min = 10, max = 20;
            FixMath.WidenSkillRange(ref min, ref max, 7);
            Assert.That((min, max), Is.EqualTo((7, 20)));
        }

        [Test]
        public void SkillRangeAboveMax()
        {
            int min = 0, max = 5;
            FixMath.WidenSkillRange(ref min, ref max, 9);
            Assert.That((min, max), Is.EqualTo((0, 9)));
        }

        [Test]
        public void StacksForShortfall() // spec: short 75, stacks 50/50/50 → two
            => Assert.That(FixMath.StacksToCover(new[] { 50f, 50f, 50f }, 3, 75f), Is.EqualTo(2));

        [Test]
        public void StacksNotEnough() => Assert.That(FixMath.StacksToCover(new[] { 10f, 10f }, 2, 75f), Is.EqualTo(2));

        [Test]
        public void PriorityBeatsDoctor() // Doctor 1 first at equal: can't beat by changing Crafting alone
            => Assert.That(FixMath.PriorityToBeat(3, 1, stalledFirstAtEqual: false), Is.EqualTo(0));

        [Test]
        public void PriorityOneBetter() => Assert.That(FixMath.PriorityToBeat(3, 2, false), Is.EqualTo(1));

        [Test]
        public void PriorityEqualEnough() => Assert.That(FixMath.PriorityToBeat(3, 1, true), Is.EqualTo(1));

        [Test]
        public void PriorityAlreadyBetter() => Assert.That(FixMath.PriorityToBeat(1, 2, true), Is.EqualTo(0));
    }
}

namespace ColonyDoctor.Tests
{
    public class FixMonitorTests
    {
        [Test] public void NoFix() => Assert.That(FixMonitor.StateAt(-1, -1, 100), Is.EqualTo(FixState.None));
        [Test] public void Monitoring() => Assert.That(FixMonitor.StateAt(1000, -1, 30000), Is.EqualTo(FixState.Monitoring));
        [Test] public void GivesUpAfterDay() => Assert.That(FixMonitor.StateAt(1000, -1, 62000), Is.EqualTo(FixState.None));
        [Test] public void FixedForAnHour() => Assert.That(FixMonitor.StateAt(1000, 3000, 5000), Is.EqualTo(FixState.Fixed));
        [Test] public void FixedExpires() => Assert.That(FixMonitor.StateAt(1000, 3000, 6000), Is.EqualTo(FixState.None));
    }
}
