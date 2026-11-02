using ColonyDoctor.Core;
using NUnit.Framework;

namespace ColonyDoctor.Tests
{
    public class IdleTests
    {
        [Test] public void NothingAssigned() => Assert.That(IdleReason.Pick(0, 0, 0, 0), Is.EqualTo(Detail.IdleNoWork));
        [Test] public void CrafterWithBlockedBills() => Assert.That(IdleReason.Pick(1, 0, 1, 0), Is.EqualTo(Detail.IdleWorkBlocked));
        [Test] public void HaulerAllReserved() => Assert.That(IdleReason.Pick(1, 0, 1, 0), Is.EqualTo(Detail.IdleWorkBlocked));
        [Test] public void NothingToDo() => Assert.That(IdleReason.Pick(3, 0, 0, 0), Is.EqualTo(Detail.IdleNothingToDo));
        [Test] public void OpenWorkNotTaken() => Assert.That(IdleReason.Pick(2, 1, 1, 0), Is.EqualTo(Detail.IdleWorkNotTaken));
        [Test] public void UnknownWorkBeatsBlocked() => Assert.That(IdleReason.Pick(2, 0, 1, 1), Is.EqualTo(Detail.IdleWorkNotTaken));
    }
}
