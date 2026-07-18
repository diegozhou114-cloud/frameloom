using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// Toast 提示面板 — 短暂显示消息后自动消失。
    /// 使用 UILayer.Tip 层，不暂停下层交互。
    /// </summary>
    public class ToastPanel : UIBase
    {
        private Text _messageText;
        private float _duration = 2f;
        private Coroutine _autoCloseCoroutine;

        public ToastPanel()
        {
            _layer = UILayer.Tip;
            _pauseLowerPanels = false;
        }

        protected override void OnOpen(object data)
        {
            BuildOnce(BuildUI);
            BindToast(data);
        }

        protected override void OnRebind(object data) => BindToast(data);

        private void BindToast(object data)
        {
            string message = data as string ?? "";
            float duration = 2f;

            // 支持传入 (string, float) 元组
            if (data is System.Tuple<string, float> tuple)
            {
                message = tuple.Item1;
                duration = tuple.Item2;
            }

            _messageText.text = message;
            if (_autoCloseCoroutine != null)
                StopCoroutine(_autoCloseCoroutine);
            _autoCloseCoroutine = StartCoroutine(AutoClose(duration));
        }

        protected override void OnClose()
        {
            if (_autoCloseCoroutine != null)
            {
                StopCoroutine(_autoCloseCoroutine);
                _autoCloseCoroutine = null;
            }
        }

        private void BuildUI()
        {
            // 底部居中半透明条
            var bar = new GameObject("ToastBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(transform, false);
            bar.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.2f, 0.08f);
            rt.anchorMax = new Vector2(0.8f, 0.15f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _messageText = new GameObject("Message", typeof(RectTransform), typeof(Text))
                .GetComponent<Text>();
            _messageText.transform.SetParent(bar.transform, false);
            _messageText.text = string.Empty;
            _messageText.fontSize = 18;
            _messageText.color = Color.white;
            _messageText.alignment = TextAnchor.MiddleCenter;
            _messageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _messageText.rectTransform.anchorMin = Vector2.zero;
            _messageText.rectTransform.anchorMax = Vector2.one;
            _messageText.rectTransform.sizeDelta = Vector2.zero;
        }

        private IEnumerator AutoClose(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            Close();
        }
    }
}
