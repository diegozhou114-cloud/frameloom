using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 强制更新面板 — 不可关闭，只能跳转应用商店。
    /// LaunchState 检测到 ForceUpdate 时打开此面板。
    /// </summary>
    public class ForceUpdatePanel : UIBase
    {
        private string _storeUrl;

        protected override void OnOpen(object data)
        {
            BindStoreUrl(data);
            BuildOnce(BuildUI);
        }

        protected override void OnRebind(object data) => BindStoreUrl(data);

        private void BindStoreUrl(object data)
        {
            _storeUrl = data as string ?? "market://details?id=xxx";
        }

        private void BuildUI()
        {
            // 遮罩
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.9f);
            bg.rectTransform.anchorMin = Vector2.zero;
            bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.sizeDelta = Vector2.zero;

            // 弹窗
            var box = new GameObject("DialogBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(transform, false);
            box.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            var boxRt = box.GetComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0.12f, 0.28f);
            boxRt.anchorMax = new Vector2(0.88f, 0.72f);
            boxRt.sizeDelta = Vector2.zero;

            // 图标
            var icon = CreateText(box.transform, "🔄", 56, new Vector2(0.5f, 0.72f),
                new Vector2(80, 70), new Color(0.2f, 0.7f, 1f));

            // 标题
            var title = CreateText(box.transform, "发现新版本", 26, new Vector2(0.5f, 0.55f),
                new Vector2(500, 40), Color.white);

            // 描述
            var desc = CreateText(box.transform, "需要更新到最新版本才能继续游戏",
                16, new Vector2(0.5f, 0.40f), new Vector2(500, 60),
                new Color(0.7f, 0.7f, 0.7f));

            // 版本
            var ver = CreateText(box.transform, $"v{Application.version} → 最新版",
                14, new Vector2(0.5f, 0.28f), new Vector2(500, 30),
                new Color(0.5f, 0.5f, 0.5f));

            // 前往商店按钮
            var btnGo = new GameObject("GoToStoreBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(box.transform, false);
            btnGo.GetComponent<Image>().color = new Color(0.2f, 0.6f, 1f);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.25f, 0.08f);
            btnRt.anchorMax = new Vector2(0.75f, 0.2f);
            btnRt.sizeDelta = Vector2.zero;

            var btnLabel = CreateText(btnGo.transform, "前往商店更新", 20,
                new Vector2(0.5f, 0.5f), new Vector2(300, 40), Color.white);

            btnGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                Application.OpenURL(_storeUrl);
            });
        }

        private Text CreateText(Transform parent, string content, int fontSize,
            Vector2 anchor, Vector2 size, Color color)
        {
            var go = new GameObject(content, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var txt = go.GetComponent<Text>();
            txt.text = content;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.rectTransform.anchorMin = anchor;
            txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = size;
            return txt;
        }
    }
}
