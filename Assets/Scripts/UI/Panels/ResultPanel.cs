using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 结算面板 — 显示星级、战利品、返回按钮。
    /// 在收到 CombatEnded 事件后打开。
    /// </summary>
    public class ResultPanel : UIBase
    {
        public ResultPanel()
        {
            _layer = UILayer.Popup;
            _pauseLowerPanels = true;
        }

        protected override void OnOpen(object data)
        {
            var result = data as CombatEndEvent?;
            if (result == null) return;

            BuildUI(result.Value);
        }

        private void BuildUI(CombatEndEvent result)
        {
            // 半透明背景
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.75f);

            // 结果卡片
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(transform, false);
            card.GetComponent<Image>().color = new Color(0.15f, 0.12f, 0.1f, 1f);
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.2f, 0.25f);
            crt.anchorMax = new Vector2(0.8f, 0.75f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            // 标题
            bool win = result.Winner == Team.Attacker;
            CreateText("Title", win ? "🏆 胜利!" : "💀 失败",
                40, new Vector2(0.5f, 0.85f), card.transform,
                TextAnchor.MiddleCenter, win ? Color.yellow : Color.gray, 300, 60);

            // 星级
            string starsStr = new string('⭐', result.Stars) + new string('☆', 3 - result.Stars);
            CreateText("Stars", starsStr, 36, new Vector2(0.5f, 0.65f), card.transform,
                TextAnchor.MiddleCenter, Color.yellow, 300, 50);

            // 战利品
            string loot = $"💰 金币: {result.LootGold}   💧 圣水: {result.LootElixir}";
            CreateText("Loot", loot, 22, new Vector2(0.5f, 0.45f), card.transform,
                TextAnchor.MiddleCenter, Color.white, 350, 40);

            // 返回按钮
            CreateButton("BtnReturn", "↩ 返回主城", new Vector2(0.25f, 0.15f),
                new Vector2(0.75f, 0.30f), card.transform,
                new Color(0.35f, 0.6f, 0.3f), () =>
            {
                EventBus.Publish("Battle_ReturnToVillage");
            });
        }

    }
}
