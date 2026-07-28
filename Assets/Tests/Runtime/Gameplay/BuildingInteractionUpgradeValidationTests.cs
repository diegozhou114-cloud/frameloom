using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class BuildingInteractionUpgradeValidationTests : RuntimeTestBase
    {
        [Test]
        public void UpgradeOperations_WithNegativeCosts_DoNotMutatePlayerOrBuilding()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            var selection = CreateOwnedObject("SelectionManager").AddComponent<SelectionManager>();
            var interaction = CreateOwnedObject("BuildingInteraction")
                .AddComponent<BuildingInteraction>();
            SetPrivateField(interaction, "_selection", selection);

            BuildingData building = BuildingData.Create(1, 0, 0);
            Assert.That(DataManager.Instance.Village.AddBuilding(building), Is.True);
            SelectBuilding(selection, building.Id);
            int gold = DataManager.Instance.Player.Gold.Value;
            int elixir = DataManager.Instance.Player.Elixir.Value;
            int gems = DataManager.Instance.Player.Gems.Value;

            LogAssert.Expect(LogType.Error,
                "[BuildingInteraction] 升级消耗或时长不能为负数");
            interaction.UpgradeSelected(-1, -1, -1);

            Assert.That(DataManager.Instance.Player.Gold.Value, Is.EqualTo(gold));
            Assert.That(DataManager.Instance.Player.Elixir.Value, Is.EqualTo(elixir));
            Assert.That(building.State.Value, Is.EqualTo(BuildingState.Normal));
            Assert.That(building.UpgradeEndTime.Value, Is.Zero);

            building.State.Value = BuildingState.Upgrading;
            building.UpgradeEndTime.Value = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
            LogAssert.Expect(LogType.Error, "[BuildingInteraction] 宝石消耗不能为负数");
            interaction.InstantUpgrade(-1);

            Assert.That(DataManager.Instance.Player.Gems.Value, Is.EqualTo(gems));
            Assert.That(building.Level.Value, Is.EqualTo(1));
            Assert.That(building.State.Value, Is.EqualTo(BuildingState.Upgrading));
        }

        private static void SelectBuilding(SelectionManager selection, string buildingId)
        {
            MethodInfo method = typeof(SelectionManager).GetMethod("SelectBuilding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(selection, new object[] { buildingId });
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
