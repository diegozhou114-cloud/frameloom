using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 启动错误面板 — 显示错误信息 + 重试/跳过按钮。
    /// 通过 EventBus 订阅 BootError 事件弹出。
    /// </summary>
    public class ErrorPanel : UIBase
    {
        private Text _messageText;
        private Button _retryButton;
        private Button _skipButton;

        private System.Action _onRetry;
        private System.Action _onSkip;

        protected override void OnOpen(object data)
        {
            BuildUI();
            EventBus.Subscribe<BootErrorEvent>(BootErrorEvent.EventKey, OnBootError);
        }

        protected override void OnClose()
        {
            EventBus.Unsubscribe<BootErrorEvent>(BootErrorEvent.EventKey, OnBootError);
        }

        private void BuildUI()
        {
            // 半透明遮罩
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.7f);
            bg.rectTransform.anchorMin = Vector2.zero;
            bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.sizeDelta = Vector2.zero;

            // 弹窗容器
            var box = new GameObject("DialogBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(transform, false);
            var boxImg = box.GetComponent<Image>();
            boxImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            var boxRt = box.GetComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0.15f, 0.3f);
            boxRt.anchorMax = new Vector2(0.85f, 0.7f);
            boxRt.sizeDelta = Vector2.zero;

            // 错误图标
            var iconGo = new GameObject("ErrorIcon", typeof(RectTransform), typeof(Text));
            iconGo.transform.SetParent(box.transform, false);
            var icon = iconGo.GetComponent<Text>();
            icon.text = "⚠";
            icon.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            icon.fontSize = 48;
            icon.color = new Color(1f, 0.8f, 0.2f);
            icon.alignment = TextAnchor.MiddleCenter;
            icon.rectTransform.anchorMin = new Vector2(0.5f, 0.65f);
            icon.rectTransform.anchorMax = new Vector2(0.5f, 0.65f);
            icon.rectTransform.sizeDelta = new Vector2(80, 60);

            // 错误消息
            var msgGo = new GameObject("Message", typeof(RectTransform), typeof(Text));
            msgGo.transform.SetParent(box.transform, false);
            _messageText = msgGo.GetComponent<Text>();
            _messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _messageText.fontSize = 18;
            _messageText.color = Color.white;
            _messageText.alignment = TextAnchor.MiddleCenter;
            _messageText.rectTransform.anchorMin = new Vector2(0.05f, 0.35f);
            _messageText.rectTransform.anchorMax = new Vector2(0.95f, 0.6f);
            _messageText.rectTransform.sizeDelta = Vector2.zero;
            _messageText.text = "启动过程出现错误";

            // 按钮容器
            var btnRow = new GameObject("ButtonRow", typeof(RectTransform));
            btnRow.transform.SetParent(box.transform, false);
            var btnRt = btnRow.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.1f, 0.08f);
            btnRt.anchorMax = new Vector2(0.9f, 0.28f);
            btnRt.sizeDelta = Vector2.zero;

            // 跳过按钮
            _skipButton = CreateButton("SkipBtn", "跳过（离线模式）",
                new Color(0.4f, 0.4f, 0.4f), btnRow.transform, new Vector2(0.52f, 0.5f));
            _skipButton.onClick.AddListener(() =>
            {
                _onSkip?.Invoke();
                Close();
            });

            // 重试按钮
            _retryButton = CreateButton("RetryBtn", "重试",
                new Color(0.2f, 0.6f, 1f), btnRow.transform, new Vector2(0.18f, 0.5f));
            _retryButton.onClick.AddListener(() =>
            {
                _onRetry?.Invoke();
                Close();
            });
        }

        private Button CreateButton(string name, string label, Color color,
            Transform parent, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(180, 50);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var txt = labelGo.GetComponent<Text>();
            txt.text = label;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = 18;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.sizeDelta = Vector2.zero;

            return go.GetComponent<Button>();
        }

        private void OnBootError(BootErrorEvent evt)
        {
            _messageText.text = $"[{evt.Module}]\n{evt.Message}";
            _skipButton.gameObject.SetActive(evt.CanSkip);

            _onRetry = null;
            _onSkip = null;

            // 由 LaunchState 提供重试/跳过回调，通过 Open(data) 传入
            if (OpenData is BootErrorCallbacks cbs)
            {
                _onRetry = cbs.OnRetry;
                _onSkip = cbs.OnSkip;
            }
        }
    }

    /// <summary>ErrorPanel 回调（由 LaunchState 通过 Open 传入）</summary>
    public class BootErrorCallbacks
    {
        public System.Action OnRetry;
        public System.Action OnSkip;
    }
}
