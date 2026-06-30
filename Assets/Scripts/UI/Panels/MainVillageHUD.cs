using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.UI
{
    /// <summary>主城 HUD — 顶部资源栏 + 攻击按钮</summary>
    public class MainVillageHUD : UIBase
    {
        private Text _goldText;
        private Text _elixirText;
        private Text _gemsText;
        private Text _levelText;
        private Text _thText;

        protected override void OnOpen(object data)
        {
            BuildUI();
            BindObservables();
        }

        protected override void OnClose()
        {
            UnbindObservables();
        }

        private void BuildUI()
        {
            // 顶部半透明背景栏
            var topBar = new GameObject("TopBar", typeof(RectTransform), typeof(Image));
            topBar.transform.SetParent(transform, false);
            topBar.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
            var tbRt = topBar.GetComponent<RectTransform>();
            tbRt.anchorMin = new Vector2(0, 0.93f);
            tbRt.anchorMax = new Vector2(1, 1);
            tbRt.offsetMin = Vector2.zero;
            tbRt.offsetMax = Vector2.zero;

            _goldText = CreateResText("Gold", "💰 0", new Vector2(0.05f, 0.5f), topBar.transform, Color.yellow);
            _elixirText = CreateResText("Elixir", "💧 0", new Vector2(0.22f, 0.5f), topBar.transform, new Color(1f, 0.6f, 0.8f));
            _gemsText = CreateResText("Gems", "💎 0", new Vector2(0.39f, 0.5f), topBar.transform, new Color(0.5f, 0.9f, 1f));
            _levelText = CreateResText("Level", "Lv.1", new Vector2(0.56f, 0.5f), topBar.transform, Color.white);
            _thText = CreateResText("TH", "TH1", new Vector2(0.68f, 0.5f), topBar.transform, Color.white);

            // 攻击按钮
            CreateButton("BtnAttack", "⚔ 攻击!", new Vector2(0.87f, 0.5f), topBar.transform,
                new Color(0.85f, 0.25f, 0.2f), () =>
            {
                EventBus.Publish("MainVillage_Attack");
            });
        }

        private Text CreateResText(string name, string text, Vector2 anchor,
            Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = 16;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(85, 28);
            return txt;
        }

        private void CreateButton(string name, string label, Vector2 anchor,
            Transform parent, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name,
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<RectTransform>().anchorMin =
                go.GetComponent<RectTransform>().anchorMax = anchor;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(90, 32);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = 16;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.rectTransform.anchorMin = Vector2.zero;
            lt.rectTransform.anchorMax = Vector2.one;
        }

        // ==================== 数据绑定 ====================

        private void BindObservables()
        {
            var player = DataManager.Instance?.Player;
            if (player == null) return;
            player.Gold.OnChanged += OnGoldChanged;
            player.Elixir.OnChanged += OnElixirChanged;
            player.Gems.OnChanged += OnGemsChanged;
            player.Level.OnChanged += OnLevelChanged;
            player.TownHallLevel.OnChanged += OnTHChanged;

            // 初始刷新
            OnGoldChanged(0, player.Gold.Value);
            OnElixirChanged(0, player.Elixir.Value);
            OnGemsChanged(0, player.Gems.Value);
            OnLevelChanged(0, player.Level.Value);
            OnTHChanged(0, player.TownHallLevel.Value);
        }

        private void UnbindObservables()
        {
            var player = DataManager.Instance?.Player;
            if (player != null)
            {
                player.Gold.OnChanged -= OnGoldChanged;
                player.Elixir.OnChanged -= OnElixirChanged;
                player.Gems.OnChanged -= OnGemsChanged;
                player.Level.OnChanged -= OnLevelChanged;
                player.TownHallLevel.OnChanged -= OnTHChanged;
            }
        }

        private void OnGoldChanged(int old, int v) { if (_goldText) _goldText.text = $"💰 {v}"; }
        private void OnElixirChanged(int old, int v) { if (_elixirText) _elixirText.text = $"💧 {v}"; }
        private void OnGemsChanged(int old, int v) { if (_gemsText) _gemsText.text = $"💎 {v}"; }
        private void OnLevelChanged(int old, int v) { if (_levelText) _levelText.text = $"Lv.{v}"; }
        private void OnTHChanged(int old, int v) { if (_thText) _thText.text = $"TH{v}"; }
    }
}
