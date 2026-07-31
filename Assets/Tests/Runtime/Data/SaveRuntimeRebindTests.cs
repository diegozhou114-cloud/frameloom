using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class SaveRuntimeRebindTests : RuntimeTestBase
    {
        [Test]
        public void MainVillageHud_ModelReplacement_RefreshesAndUnsubscribesOldPlayer()
        {
            DataManager manager = CreateOwnedObject("DataManager").AddComponent<DataManager>();
            UIManager ui = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            manager.Player.Gold.Value = 10;
            MainVillageHUD hud = ui.Open<MainVillageHUD>();
            PlayerData previous = manager.Player;
            var player = new PlayerData();
            player.Gold.Value = 20;

            manager.ReplaceValidatedState(new ValidatedSaveState(player, new VillageData()));

            Text gold = (Text)typeof(MainVillageHUD).GetField(
                "_goldText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
            Assert.That(gold.text, Is.EqualTo("💰 20"));
            previous.Gold.Value = 30;
            Assert.That(gold.text, Is.EqualTo("💰 20"));
            player.Gold.Value = 21;
            Assert.That(gold.text, Is.EqualTo("💰 21"));
        }

        [Test]
        public void MainVillageState_ModelReplacement_RebindsResourceProducer()
        {
            DataManager manager = CreateOwnedObject("DataManager").AddComponent<DataManager>();
            CreateOwnedObject("UIManager").AddComponent<UIManager>();
            var state = new MainVillageState();
            state.OnEnter();
            ResourceProducer previous = GetResourceProducer(state);
            var player = new PlayerData();
            var village = new VillageData();

            manager.ReplaceValidatedState(new ValidatedSaveState(player, village));

            ResourceProducer current = GetResourceProducer(state);
            Assert.That(current, Is.Not.SameAs(previous));
            Assert.That(GetField<PlayerData>(current, "_player"), Is.SameAs(player));
            Assert.That(GetField<VillageData>(current, "_village"), Is.SameAs(village));
            state.OnExit();
        }

        [Test]
        public void MainVillageState_ModelReplacement_ResetsAutosaveBaseline()
        {
            DataManager manager = CreateOwnedObject("DataManager").AddComponent<DataManager>();
            CreateOwnedObject("UIManager").AddComponent<UIManager>();
            int saves = 0;
            var autosave = new AutosaveCoordinator(
                () => SaveDataMapper.Capture(manager.Player, manager.Village),
                () => { saves++; return true; });
            var state = new MainVillageState(autosave);
            state.OnEnter();
            var player = new PlayerData();
            player.Gold.Value = 20;

            manager.ReplaceValidatedState(new ValidatedSaveState(player, new VillageData()));

            Assert.That(autosave.TrySaveIfChanged(), Is.False);
            Assert.That(saves, Is.Zero);
            player.Gold.Value++;
            Assert.That(autosave.TrySaveIfChanged(), Is.True);
            Assert.That(saves, Is.EqualTo(1));
            state.OnExit();
        }

        [UnityTest]
        public IEnumerator ModelReplacement_MissingSelectedBuilding_ClearsSelectionAndClosesPanel()
        {
            DataManager manager = CreateOwnedObject("DataManager").AddComponent<DataManager>();
            UIManager ui = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            CreateOwnedObject("SceneServiceRegistry").AddComponent<SceneServiceRegistry>();
            var selection = CreateOwnedObject("SelectionManager").AddComponent<SelectionManager>();
            BuildingData selected = BuildingData.Create(1, 0, 0);
            Assert.That(manager.Village.AddBuilding(selected), Is.True);
            yield return null;
            SelectBuilding(selection, selected.Id);
            BuildingInfoPanel panel = Object.FindObjectOfType<BuildingInfoPanel>(true);
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.IsOpen, Is.True);

            manager.ReplaceValidatedState(
                new ValidatedSaveState(new PlayerData(), new VillageData()));

            Assert.That(selection.CurrentType, Is.EqualTo(SelectionType.None));
            Assert.That(selection.SelectedBuildingId, Is.Null);
            Assert.That(panel.IsOpen, Is.False);
            ui.Close<BuildingInfoPanel>();
        }

        [UnityTest]
        public IEnumerator VillageRenderer_ModelReplacement_UnsubscribesOldVillage()
        {
            DataManager manager = CreateOwnedObject("DataManager").AddComponent<DataManager>();
            CreateOwnedObject("GridManager").AddComponent<GridManager>();
            var renderer = CreateOwnedObject("VillageViewRenderer")
                .AddComponent<VillageViewRenderer>();
            yield return null;
            VillageData previous = manager.Village;
            Assert.That(previous.AddBuilding(BuildingData.Create(1, 0, 0)), Is.True);
            Assert.That(renderer.ViewCount, Is.EqualTo(1));
            var village = new VillageData();
            Assert.That(village.AddBuilding(BuildingData.Create(1, 1, 1)), Is.True);

            manager.ReplaceValidatedState(new ValidatedSaveState(new PlayerData(), village));

            Assert.That(renderer.ViewCount, Is.EqualTo(1));
            Assert.That(previous.AddBuilding(BuildingData.Create(2, 2, 2)), Is.True);
            Assert.That(renderer.ViewCount, Is.EqualTo(1));
            Assert.That(village.AddBuilding(BuildingData.Create(2, 3, 3)), Is.True);
            Assert.That(renderer.ViewCount, Is.EqualTo(2));
        }

        private static ResourceProducer GetResourceProducer(MainVillageState state) =>
            GetField<ResourceProducer>(state, "_resourceProducer");

        private static T GetField<T>(object instance, string name) =>
            (T)instance.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);

        private static void SelectBuilding(SelectionManager selection, string buildingId)
        {
            MethodInfo method = typeof(SelectionManager).GetMethod(
                "SelectBuilding", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(selection, new object[] { buildingId });
        }
    }
}
