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
        private int _lastGold;
        private int _lastElixir;

        public MainVillageState() : base(GameState.MainVillage) { }

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
            _resourceProducer = new ResourceProducer(DataManager.Instance.Village);
            _resourceProducer.Start();

            // ④ 订阅 HUD 攻击按钮事件
            EventBus.Subscribe("MainVillage_Attack", OnAttackClicked);

            EventBus.Publish("MainVillageEntered");

            // 自动存档状态初始化
            _autoSaveTimer = 0f;
            _lastGold = DataManager.Instance.Player.Gold.Value;
            _lastElixir = DataManager.Instance.Player.Elixir.Value;
        }

        public override void OnUpdate()
        {
            _resourceProducer?.Tick(Time.deltaTime);

            // 自动存档：每 30 秒检查资源变化
            _autoSaveTimer += Time.deltaTime;
            if (_autoSaveTimer >= 30f)
            {
                _autoSaveTimer = 0f;
                var p = DataManager.Instance.Player;
                if (p.Gold.Value != _lastGold || p.Elixir.Value != _lastElixir)
                {
                    SaveManager.SaveGame();
                    _lastGold = p.Gold.Value;
                    _lastElixir = p.Elixir.Value;
                    Debug.Log("[MainVillageState] 自动存档");
                }
            }
        }

        public override void OnExit()
        {
            EventBus.Unsubscribe("MainVillage_Attack", OnAttackClicked);
            _resourceProducer?.Stop();
            UIManager.Instance.Close<MainVillageHUD>();
            base.OnExit();
            Debug.Log("[GameFSM] 退出主城状态");
        }

        // ==================== 事件回调 ====================

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

            // 创建攻击参数：攻击自己的村庄（PvE 模式）
            var bp = BattleParams.VillageAttack(village, village);
            ChangeState(GameState.Battle, bp);
        }

        // ==================== 辅助 ====================

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
