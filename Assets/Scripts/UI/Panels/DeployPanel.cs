using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 部署面板 — 底部兵种选择栏 + 点击网格放兵。
    /// 在 CombatPhase.Deploying 期间显示。
    /// </summary>
    public class DeployPanel : UIBase
    {
        private List<UnitConfig> _availableUnits;
        private UnitConfig _selectedUnit;
        private Text _timerText;
        private Text _countText;
        private int _deployedCount;
        private float _deployStartTime;

        public DeployPanel()
        {
            _layer = UILayer.HUD;
            _pauseLowerPanels = false;
        }

        protected override void OnOpen(object data)
        {
            _deployedCount = 0;
            _deployStartTime = Time.time;
            var allUnits = ConfigTableManager.Instance?.GetAllUnits();
            _availableUnits = allUnits != null
                ? new List<UnitConfig>(allUnits)
                : new List<UnitConfig>();
            if (_availableUnits.Count > 0)
                _selectedUnit = _availableUnits[0];

            BuildUI();
            EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        protected override void OnClose()
        {
            EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
        }

        // ==================== UI 构建 ====================

        private void BuildUI()
        {
            // 底部半透明栏 — 兵种选择
            var bottomBar = MakeBar("BottomBar", new Vector2(0, 0), new Vector2(1, 0.12f),
                new Color(0, 0, 0, 0.6f));

            float btnW = 1f / Mathf.Max(_availableUnits.Count, 1);
            for (int i = 0; i < _availableUnits.Count; i++)
            {
                int idx = i;
                var unit = _availableUnits[i];
                CreateButton($"UnitBtn_{unit.Id}", $"{unit.Name}\n{unit.TrainingCost}",
                    new Vector2(i * btnW, 0), new Vector2((i + 1) * btnW, 1),
                    bottomBar.transform, new Color(0.3f, 0.5f, 0.3f), () =>
                {
                    _selectedUnit = unit;
                    Debug.Log($"[DeployPanel] 选择兵种: {unit.Name}");
                });
            }

            // 顶部计时 + 计数
            var topBar = MakeBar("TopBar", new Vector2(0, 0.88f), new Vector2(1, 1),
                new Color(0, 0, 0, 0.45f));

            _timerText = CreateText("Timer", "30s", 20, new Vector2(0.05f, 0.5f),
                topBar.transform, TextAnchor.MiddleLeft, Color.white);
            _countText = CreateText("Count", "已部署: 0", 18, new Vector2(0.5f, 0.5f),
                topBar.transform, TextAnchor.MiddleCenter, Color.white);

            // 开始战斗按钮
            CreateButton("BtnStart", "⚔ 开始战斗", new Vector2(0.85f, 0.5f),
                new Vector2(0.96f, 1f), topBar.transform,
                new Color(0.85f, 0.25f, 0.2f), () =>
            {
                Debug.Log("[DeployPanel] 手动开始战斗");
                CombatManager.Instance.EndDeployPhase();
            });
        }

        // ==================== 每帧更新 ====================

        private void Update()
        {
            if (!IsOpen) return;

            var cm = CombatManager.Instance;
            if (cm == null) return;

            if (cm.CurrentPhase == CombatPhase.Deploying)
            {
                float elapsed = Time.time - _deployStartTime;
                float remain = Mathf.Max(0, cm.CurrentMode.DeployTimeLimit - elapsed);
                _timerText.text = $"{remain:F0}s";
            }
        }

        // ==================== 手势 → 部署 ====================

        private void OnGesture(GestureData gesture)
        {
            if (gesture.Type != GestureType.Tap) return;

            var cm = CombatManager.Instance;
            if (cm == null || cm.CurrentPhase != CombatPhase.Deploying) return;

            var mainCam = Camera.main;
            if (mainCam == null) return;

            // 屏幕坐标 → 世界坐标 → 网格坐标
            var worldPos = mainCam.ScreenToWorldPoint(
                new Vector3(gesture.ScreenPosition.x, gesture.ScreenPosition.y,
                    mainCam.transform.position.y));
            var gridPos = GridManager.Instance.WorldToGrid(worldPos);

            DeployAt(gridPos);
        }

        private void DeployAt(Vector2Int gridPos)
        {
            if (_selectedUnit == null) return;

            var cm = CombatManager.Instance;
            if (cm == null || cm.CurrentPhase != CombatPhase.Deploying) return;

            int max = cm.CurrentMode.MaxDeployCount;
            if (max > 0 && _deployedCount >= max) return;

            var grid = GridManager.Instance;
            if (grid == null || !grid.IsInBounds(gridPos)) return;
            if (grid.IsOccupied(gridPos)) return;

            cm.SpawnDeployUnit(gridPos, _selectedUnit);
            _deployedCount++;
            _countText.text = $"已部署: {_deployedCount}";
        }

        // ==================== 快捷 UI ====================

        private GameObject MakeBar(string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private Text CreateText(string name, string text, int fontSize, Vector2 anchor,
            Transform parent, TextAnchor align, Color color)
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
            txt.rectTransform.sizeDelta = new Vector2(200, 28);
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
            lt.fontSize = 14;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.rectTransform.anchorMin = Vector2.zero;
            lt.rectTransform.anchorMax = Vector2.one;
        }
    }
}
