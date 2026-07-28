using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class DeployPanelAvailabilityTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator OnOpen_OnlyIncludesUnitsUnlockedByPlayersTownHallLevel()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            ConfigTableManager config = CreateOwnedObject("ConfigTableManager")
                .AddComponent<ConfigTableManager>();
            Assert.That(config.PreloadAllAsync().GetAwaiter().GetResult(), Is.True);
            DataManager.Instance.Player.TownHallLevel.Value = 1;

            DeployPanel panel = CreateOwnedObject("DeployPanel").AddComponent<DeployPanel>();
            InvokeOnOpen(panel);
            yield return null;

            List<UnitConfig> available = GetPrivateField<List<UnitConfig>>(panel, "_availableUnits");
            Assert.That(available, Is.Not.Empty);
            Assert.That(available, Is.All.Matches<UnitConfig>(
                unit => unit.RequiredTownHall <= 1));
            Assert.That(available, Is.All.Matches<UnitConfig>(
                unit => FrameworkBattleContent.TryGetUnitArchetypeId(unit.Id, out _)));
            Assert.That(available, Has.None.Matches<UnitConfig>(unit => unit.Id == 3));
            Assert.That(available.Select(unit => unit.Id), Is.EquivalentTo(new[] { 1 }));
        }

        private static void InvokeOnOpen(DeployPanel panel)
        {
            MethodInfo method = typeof(DeployPanel).GetMethod("OnOpen",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(panel, new object[] { null });
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }
    }
}
