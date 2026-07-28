using System.Reflection;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class MainVillageBattleEligibilityTests
    {
        private static readonly MethodInfo HasSupportedDefender = typeof(MainVillageState).GetMethod(
            "HasSupportedDefender", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void HasSupportedDefender_OnlyUnsupportedBuildings_ReturnsFalse()
        {
            var village = new VillageData(_ => null);
            village.AddBuilding(BuildingData.Create(2, 0, 0));

            Assert.That(HasSupportedDefender, Is.Not.Null);
            Assert.That(HasSupportedDefender.Invoke(null, new object[] { village }), Is.False);
        }

        [Test]
        public void HasSupportedDefender_TownHallOrCannon_ReturnsTrue()
        {
            var village = new VillageData(_ => null);
            village.AddBuilding(BuildingData.Create(1, 0, 0));
            village.AddBuilding(BuildingData.Create(5, 2, 0));

            Assert.That(HasSupportedDefender, Is.Not.Null);
            Assert.That(HasSupportedDefender.Invoke(null, new object[] { village }), Is.True);
        }
    }
}
