using UnityEngine;

namespace ZWebGLFrameWork.MiniSiege
{
    public sealed partial class MiniSiegeGame
    {
        private static readonly Color Ink = new Color32(17, 28, 41, 255);
        private static readonly Color Panel = new Color32(28, 43, 58, 255);
        private static readonly Color Muted = new Color32(152, 170, 183, 255);
        private static readonly Color Paper = new Color32(240, 235, 218, 255);
        private static readonly Color Blue = new Color32(96, 197, 238, 255);
        private static readonly Color Red = new Color32(246, 140, 121, 255);
        private static readonly Color Gold = new Color32(245, 208, 123, 255);
        private GUIStyle _small, _body, _title, _heading, _field;
        private Texture2D _circle;

        private void PrepareStyles()
        {
            _circle = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            _circle.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), new Vector2(23.5f, 23.5f));
                    _circle.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(24 - radius)));
                }
            _circle.Apply();
        }

        private void EnsureStyles()
        {
            if (_small != null) return;
            _small = LabelStyle(12, Muted);
            _body = LabelStyle(16, Paper);
            _heading = LabelStyle(22, Paper, true);
            _title = LabelStyle(38, Paper, true);
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        }

        private static GUIStyle LabelStyle(int size, Color color, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = color;
            return style;
        }

        private static void Block(float x, float y, float w, float h, Color color)
        {
            Color old = GUI.color; GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void Dot(float x, float y, float size, Color color)
        {
            Color old = GUI.color; GUI.color = color;
            GUI.DrawTexture(new Rect(x - size / 2, y - size / 2, size, size), _circle);
            GUI.color = old;
        }

        private static void Text(float x, float y, float w, float h, string value, GUIStyle style,
            Color? color = null, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Color old = style.normal.textColor;
            TextAnchor oldAlign = style.alignment;
            if (color.HasValue) style.normal.textColor = color.Value;
            style.alignment = alignment;
            GUI.Label(new Rect(x, y, w, h), value, style);
            style.normal.textColor = old; style.alignment = oldAlign;
        }

        private bool Button(Rect rect, string label, Color color, bool enabled = true)
        {
            Block(rect.x, rect.y, rect.width, rect.height,
                enabled ? color : new Color32(42, 57, 72, 255));
            if (enabled && rect.Contains(Event.current.mousePosition))
                Block(rect.x, rect.y, rect.width, rect.height, new Color(1, 1, 1, 0.15f));
            Text(rect.x, rect.y, rect.width, rect.height, label, _body,
                enabled ? Ink : Muted, TextAnchor.MiddleCenter);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = enabled;
            bool pressed = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.enabled = oldEnabled;
            return pressed;
        }
    }
}
