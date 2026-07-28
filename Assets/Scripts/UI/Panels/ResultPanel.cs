using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 结算面板 — 显示星级、战利品、返回按钮。
    /// 在战斗结果成功结算并持久化后打开。
    /// </summary>
    public class ResultPanel : UIBase
    {
        private Text _titleText;
        private Text _starsText;
        private Text _lootText;

        public ResultPanel()
        {
            _layer = UILayer.Popup;
            _pauseLowerPanels = true;
        }

        protected override void OnOpen(object data)
        {
            BuildOnce(BuildUI);
            BindResult(data);
        }

        protected override void OnRebind(object data) => BindResult(data);

        private void BuildUI()
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
            _titleText = CreateText("Title", string.Empty,
                40, new Vector2(0.5f, 0.85f), card.transform,
                TextAnchor.MiddleCenter, Color.white, 300, 60);

            // 星级
            _starsText = CreateText("Stars", string.Empty, 36, new Vector2(0.5f, 0.65f),
                card.transform, TextAnchor.MiddleCenter, Color.yellow, 300, 50);

            // 战利品
            _lootText = CreateText("Loot", string.Empty, 22, new Vector2(0.5f, 0.45f),
                card.transform, TextAnchor.MiddleCenter, Color.white, 350, 40);

            // 返回按钮
            CreateButton("BtnReturn", "↩ 返回主城", new Vector2(0.25f, 0.15f),
                new Vector2(0.75f, 0.30f), card.transform,
                new Color(0.35f, 0.6f, 0.3f), () =>
            {
                EventBus.Publish("Battle_ReturnToVillage");
            });
        }

        private void BindResult(object data)
        {
            if (!(data is SimBattleResult result)) return;

            bool win = result.WinnerTeamId == SimTeam.Attacker;
            int stars = Mathf.Clamp(result.Stars, 0, 3);
            _titleText.text = win ? "🏆 胜利!" : "💀 失败";
            _titleText.color = win ? Color.yellow : Color.gray;
            _starsText.text = new string('⭐', stars) + new string('☆', 3 - stars);
            _lootText.text = $"💰 金币: {result.LootGold}   💧 圣水: {result.LootElixir}";
        }

    }
}
