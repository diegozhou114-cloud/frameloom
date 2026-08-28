using System;
using UnityEngine;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 主城状态 — 核心玩法入口。
    /// OnEnter: 加载存档→Grid→建筑→Camera→资源产出→HUD。
    /// OnUpdate: 资源产出计时 + 建筑升级计时。
    /// </summary>
    public class MainVillageState : FSMState<GameState>
    {
        private ResourceProducer _resourceProducer;
        private float _autoSaveTimer;
        private AutosaveCoordinator _autosave;
        private readonly AutosaveCoordinator _injectedAutosave;
        private readonly FrameworkBattleAuthority _battleAuthority =
            new FrameworkBattleAuthority();

        public MainVillageState() : base(GameState.MainVillage) { }

        internal MainVillageState(AutosaveCoordinator autosave) : base(GameState.MainVillage)
        {
            _injectedAutosave = autosave ?? throw new ArgumentNullException(nameof(autosave));
            _autosave = _injectedAutosave;
        }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入主城状态");

            // ① 打开主城 HUD
            UIManager.Instance.Open<MainVillageHUD>();

            // ② 初始化/定位相机到 Town Hall
            var camera = Camera.main?.GetComponent<CameraController>();
            if (camera != null)
            {
                camera.FocusOn(FindTownHallPosition());
            }

            // ③ 启动资源产出计时器
            StartResourceProducer();

            // ④ 订阅 HUD 攻击按钮事件
            EventBus.Subscribe("MainVillage_Attack", OnAttackClicked);
            EventBus.Subscribe(DataManager.EventDataReady, OnDataReady);

            EventBus.Publish("MainVillageEntered");

            // 自动存档状态初始化
            _autoSaveTimer = 0f;
            _autosave = _injectedAutosave ?? new AutosaveCoordinator(
                () => SaveDataMapper.Capture(
                    DataManager.Instance.Player,
                    DataManager.Instance.Village),
                () => SaveManager.SaveGame());
            _autosave.InitializeBaseline();
        }

        public override void OnUpdate()
        {
            _resourceProducer?.Tick(Time.deltaTime);

            // 自动存档：每 30 秒检查完整持久化状态
            _autoSaveTimer += Time.deltaTime;
            if (_autoSaveTimer >= 30f)
            {
                _autoSaveTimer = 0f;
                if (_autosave?.TrySaveIfChanged() == true)
                    Debug.Log("[MainVillageState] 自动存档");
            }
        }

        public override void OnSuspend(object data)
        {
            _autosave?.FlushPending();
            base.OnSuspend(data);
        }

        public override void OnExit()
        {
            _autosave?.FlushPending();
            _autosave = null;
            EventBus.Unsubscribe("MainVillage_Attack", OnAttackClicked);
            EventBus.Unsubscribe(DataManager.EventDataReady, OnDataReady);
            _resourceProducer?.Stop();
            _resourceProducer = null;
            UIManager.Instance.Close<MainVillageHUD>();
            base.OnExit();
            Debug.Log("[GameFSM] 退出主城状态");
        }

        // ==================== 事件回调 ====================

        private void OnDataReady()
        {
            _resourceProducer?.Stop();
            StartResourceProducer();
            _autosave?.InitializeBaseline();
        }

        /// <summary>HUD 攻击按钮 → Battle</summary>
        private void OnAttackClicked()
        {
            Debug.Log("[MainVillageState] ⚔ 攻击按钮 → Battle");

            var village = DataManager.Instance.Village;
            if (village == null || village.Buildings.Count == 0)
            {
                Debug.LogWarning("[MainVillageState] 村庄无建筑，无法发起战斗");
                return;
            }

            if (!HasSupportedDefender(village))
            {
                Debug.LogWarning("[MainVillageState] 村庄没有框架战斗内容支持的防守建筑，无法发起战斗");
                return;
            }

            BattleParams parameters = _battleAuthority.CreateVillageAttack(
                DataManager.Instance.Player, village, village);
            ChangeState(GameState.Battle, parameters);
        }

        // ==================== 辅助 ====================

        private static bool HasSupportedDefender(VillageData village)
        {
            return village != null && village.Buildings.Exists(building =>
                building != null &&
                FrameworkBattleContent.TryGetBuildingArchetypeId(building.TypeId, out _));
        }

        private void StartResourceProducer()
        {
            _resourceProducer = new ResourceProducer(DataManager.Instance.Village);
            _resourceProducer.Start();
        }

        private Vector3 FindTownHallPosition()
        {
            var village = DataManager.Instance.Village;
            var townHall = village?.Buildings.Find(b => b.TypeId == 1);
            if (townHall != null && GridManager.Instance != null)
            {
                return GridManager.Instance.GridToWorld(
                    townHall.GridX.Value, townHall.GridY.Value);
            }
            return Vector3.zero;
        }
    }
}
