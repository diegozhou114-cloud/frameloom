using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// 启动加载面板 — 显示 Logo + 进度条 + 阶段描述 + 版本号。
    /// 通过 EventBus 订阅 BootProgress 事件更新 UI。
    /// 挂载到 UILayer.Top 层，覆盖所有其他 UI。
    /// </summary>
    public class LoadingPanel : UIBase
    {
        [Header("UI 组件")]
        private Text _phaseText;
        private Slider _progressSlider;
        private Text _progressText;
        private Text _versionText;
        private Text _hintText;
        private Image _logoImage;
        private Sprite _logoSprite;

        private const string LogoResourcePath = "UI/Textures/Logo";

        protected override void OnOpen(object data)
        {
            BuildOnce(BuildUI);
            TryLoadLogo();
            EventBus.Subscribe<BootProgressEvent>(BootProgressEvent.EventKey, OnBootProgress);
        }

        protected override void OnClose()
        {
            EventBus.Unsubscribe<BootProgressEvent>(BootProgressEvent.EventKey, OnBootProgress);
            if (_logoSprite != null)
            {
                Object.Destroy(_logoSprite);
                _logoSprite = null;
            }
        }

        private void BuildUI()
        {
            // 遮罩背景
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.95f);
            bg.rectTransform.anchorMin = Vector2.zero;
            bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.sizeDelta = Vector2.zero;

            // Logo
            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(transform, false);
            _logoImage = logoGo.AddComponent<Image>();
            _logoImage.rectTransform.anchorMin = new Vector2(0.5f, 0.65f);
            _logoImage.rectTransform.anchorMax = new Vector2(0.5f, 0.65f);
            _logoImage.rectTransform.sizeDelta = new Vector2(200, 200);
            _logoImage.color = Color.white;

            // 阶段描述
            var phaseGo = CreateText("PhaseText", 28, new Vector2(0.5f, 0.45f));
            _phaseText = phaseGo.GetComponent<Text>();
            _phaseText.text = "引擎初始化中...";

            // 进度条
            var sliderGo = new GameObject("ProgressSlider", typeof(RectTransform));
            sliderGo.transform.SetParent(transform, false);
            _progressSlider = sliderGo.AddComponent<Slider>();
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0.2f, 0.38f);
            sliderRt.anchorMax = new Vector2(0.8f, 0.40f);
            sliderRt.sizeDelta = Vector2.zero;
            _progressSlider.minValue = 0;
            _progressSlider.maxValue = 1;
            _progressSlider.value = 0;

            // 进度条填充
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(sliderGo.transform, false);
            _progressSlider.fillRect = fillGo.GetComponent<RectTransform>();
            _progressSlider.fillRect.anchorMin = Vector2.zero;
            _progressSlider.fillRect.anchorMax = Vector2.one;
            _progressSlider.fillRect.sizeDelta = Vector2.zero;
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = new Color(0.2f, 0.6f, 1f);

            // 进度百分比
            var pctGo = CreateText("ProgressText", 18, new Vector2(0.5f, 0.34f));
            _progressText = pctGo.GetComponent<Text>();
            _progressText.text = "0%";

            // 额外提示（如网速）
            var hintGo = CreateText("HintText", 14, new Vector2(0.5f, 0.30f));
            _hintText = hintGo.GetComponent<Text>();
            _hintText.color = new Color(0.7f, 0.7f, 0.7f);
            _hintText.text = "";

            // 版本号
            var verGo = CreateText("VersionText", 14, new Vector2(0.5f, 0.05f));
            _versionText = verGo.GetComponent<Text>();
            _versionText.color = new Color(0.4f, 0.4f, 0.4f);
            _versionText.text = $"v{Application.version}";
        }

        private GameObject CreateText(string name, int fontSize, Vector2 anchorY)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = anchorY;
            text.rectTransform.anchorMax = anchorY;
            text.rectTransform.sizeDelta = new Vector2(600, 40);
            return go;
        }

        private void TryLoadLogo()
        {
            if (_logoImage == null) return;
            if (_logoSprite != null)
            {
                Object.Destroy(_logoSprite);
                _logoSprite = null;
            }
            var tex = Resources.Load<Texture2D>(LogoResourcePath);
            if (tex != null)
            {
                _logoSprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _logoImage.sprite = _logoSprite;
            }
        }

        private void OnBootProgress(BootProgressEvent evt)
        {
            _phaseText.text = evt.Phase;
            _progressSlider.value = evt.Progress;
            _progressText.text = $"{Mathf.RoundToInt(evt.Progress * 100)}%";
            if (!string.IsNullOrEmpty(evt.Hint))
                _hintText.text = evt.Hint;
        }
    }
}
