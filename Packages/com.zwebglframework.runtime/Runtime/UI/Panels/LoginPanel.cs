using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.UI
{
    /// <summary>登录面板 — 离线模式 / 游客登录 / 微信登录</summary>
    public class LoginPanel : UIBase
    {
        private Text _statusText;

        protected override void OnOpen(object data)
        {
            BuildOnce(BuildUI);
            BindError(data);
            EventBus.Subscribe<string>(LoginEvents.LoginError, ShowError);
        }

        protected override void OnRebind(object data) => BindError(data);

        private void BindError(object data)
        {
            if (data is string error)
                ShowError(error);
        }

        protected override void OnClose()
        {
            EventBus.Unsubscribe<string>(LoginEvents.LoginError, ShowError);
        }

        private void BuildUI()
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.15f, 0.25f, 1f);

            CreateText("Title", "部落冲突", 36, new Vector2(0.5f, 0.78f),
                TextAnchor.MiddleCenter, Color.white);

            CreateButton("BtnOffline", "🏠 离线模式", new Vector2(0.5f, 0.60f),
                new Color(0.35f, 0.5f, 0.3f), () =>
            {
                EventBus.Publish(LoginEvents.OfflineLogin);
            });

            CreateButton("BtnGuest", "🎮 游客登录", new Vector2(0.5f, 0.48f),
                new Color(0.3f, 0.45f, 0.3f), () =>
            {
                EventBus.Publish(LoginEvents.GuestLogin);
            });

            CreateButton("BtnWeChat", "💬 微信登录", new Vector2(0.5f, 0.36f),
                new Color(0.25f, 0.25f, 0.25f), () =>
            {
                ShowError("微信登录暂未开放");
            });

            var go = CreateText("Status", "", 16, new Vector2(0.5f, 0.15f),
                TextAnchor.MiddleCenter, Color.gray);
            _statusText = go;
        }

        public void ShowError(string msg)
        {
            if (_statusText != null)
            {
                _statusText.text = msg;
                _statusText.color = Color.red;
            }
        }

        public void SetStatus(string msg)
        {
            if (_statusText != null)
            {
                _statusText.text = msg;
                _statusText.color = Color.white;
            }
        }

        // ==================== 快捷 UI ====================

        private Text CreateText(string name, string text, int fontSize,
            Vector2 anchor, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(300, 50);
            return txt;
        }

        private void CreateButton(string name, string label, Vector2 anchor,
            Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name,
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(220, 50);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lt = labelGo.AddComponent<Text>();
            lt.text = label;
            lt.fontSize = 22;
            lt.color = Color.white;
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.rectTransform.anchorMin = Vector2.zero;
            lt.rectTransform.anchorMax = Vector2.one;
        }
    }
}
