#if UNITY_EDITOR
using NUnit.Framework;
using TankWarfare.Core;

namespace TankWarfare.Tests
{
    public sealed class TankWarfareTests
    {
        [Test]
        public void TankClasses_HaveExpectedTradeoffs()
        {
            TankSpec heavy = TankCatalog.Get(TankType.Heavy);
            TankSpec medium = TankCatalog.Get(TankType.Medium);
            TankSpec light = TankCatalog.Get(TankType.Light);

            Assert.That(heavy.MaxHealth, Is.GreaterThan(medium.MaxHealth));
            Assert.That(medium.MaxHealth, Is.GreaterThan(light.MaxHealth));
            Assert.That(light.MoveSpeed, Is.GreaterThan(medium.MoveSpeed));
            Assert.That(medium.MoveSpeed, Is.GreaterThan(heavy.MoveSpeed));
            Assert.That(heavy.Damage, Is.GreaterThan(medium.Damage));
            Assert.That(medium.Damage, Is.GreaterThan(light.Damage));
            Assert.That(light.BulletSpeed, Is.GreaterThan(heavy.BulletSpeed));
        }

        [Test]
        public void Statistics_AggregatesCompletedMatches()
        {
            var statistics = new PlayerStatistics();
            statistics.AddMatch(true, TankType.Light,
                new MatchPlayerStatistics { meters = 12.5f, shots = 7, walls = 2 });

            Assert.That(statistics.victories, Is.EqualTo(1));
            Assert.That(statistics.matches, Is.EqualTo(1));
            Assert.That(statistics.metersDriven, Is.EqualTo(12.5d));
            Assert.That(statistics.shotsFired, Is.EqualTo(7));
            Assert.That(statistics.wallsBroken, Is.EqualTo(2));
            Assert.That(statistics.FavoriteTank, Is.EqualTo(TankType.Light));
        }
    }
}
#endif
