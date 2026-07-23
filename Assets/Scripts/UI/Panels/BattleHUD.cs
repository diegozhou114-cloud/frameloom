using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 战斗中 HUD — 顶部显示剩余时间、波次、双方存活数。
    /// 在 SimCombatPhase.Battling 期间显示。
    /// </summary>
    public class BattleHUD : UIBase
    {
        private Text _timerText;
        private Text _waveText;
        private Text _aliveText;

        public BattleHUD()
        {
            _layer = UILayer.HUD;
            _pauseLowerPanels = false;
        }

        protected override void OnOpen(object data)
        {
            BuildOnce(BuildUI);
        }

        private void BuildUI()
        {
            var bar = new GameObject("HUDTopBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(transform, false);
            bar.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.88f);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _timerText = CreateText("Timer", "3:00", 22, new Vector2(0.05f, 0.5f),
                bar.transform, TextAnchor.MiddleLeft, Color.white);
            _waveText = CreateText("Wave", "波次: 0", 18, new Vector2(0.4f, 0.5f),
                bar.transform, TextAnchor.MiddleCenter, Color.yellow);
            _aliveText = CreateText("Alive", "⚔ 0 vs 0 🛡", 18, new Vector2(0.7f, 0.5f),
                bar.transform, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
        }

        private void Update()
        {
            if (!IsOpen) return;

            var cm = CombatManager.Instance;
            if (cm == null || cm.CurrentPhase != SimCombatPhase.Battling) return;

            int remainingSeconds = cm.RemainingBattleTicks / 20;
            int min = remainingSeconds / 60;
            int sec = remainingSeconds % 60;
            _timerText.text = $"{min}:{sec:D2}";

            _aliveText.text = $"⚔ {cm.AttackerAliveCount} vs {cm.DefenderAliveCount} 🛡";
            _waveText.text = $"波次: {cm.CurrentWave}";
        }


    }
}
