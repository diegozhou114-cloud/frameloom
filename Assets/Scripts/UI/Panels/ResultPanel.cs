using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.FSM;

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
            MakeText("Title", win ? "🏆 胜利!" : "💀 失败",
                40, new Vector2(0.5f, 0.85f), card.transform,
                TextAnchor.MiddleCenter, win ? Color.yellow : Color.gray, 300, 60);

            // 星级
            string starsStr = new string('⭐', result.Stars) + new string('☆', 3 - result.Stars);
            MakeText("Stars", starsStr, 36, new Vector2(0.5f, 0.65f), card.transform,
                TextAnchor.MiddleCenter, Color.yellow, 300, 50);

            // 战利品
            string loot = $"💰 金币: {result.LootGold}   💧 圣水: {result.LootElixir}";
            MakeText("Loot", loot, 22, new Vector2(0.5f, 0.45f), card.transform,
                TextAnchor.MiddleCenter, Color.white, 350, 40);

            // 返回按钮
            CreateButton("BtnReturn", "↩ 返回主城", new Vector2(0.25f, 0.15f),
                new Vector2(0.75f, 0.30f), card.transform,
                new Color(0.35f, 0.6f, 0.3f), () =>
            {
                GameFSM.Instance.ChangeState(GameState.MainVillage);
            });
        }

        private Text MakeText(string name, string text, int fontSize, Vector2 anchor,
            Transform parent, TextAnchor align, Color color, float w = 300, float h = 40)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(w, h);
            return txt;
        }

        private void CreateButton(string name, string label, Vector2 anchorMin, Vector2 anchorMax,
            Transform parent, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = 26;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.rectTransform.anchorMin = Vector2.zero;
            lt.rectTransform.anchorMax = Vector2.one;
        }
    }
}
