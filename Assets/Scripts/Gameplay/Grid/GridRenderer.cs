using UnityEngine;

namespace ZWebGLFrameWork.Gameplay
{
    /// <summary>
    /// 网格可视化 — 放置/编辑模式下显示网格线和合法性高亮。
    /// 使用 LineRenderer 绘制网格线。
    /// </summary>
    public class GridRenderer : MonoBehaviour
    {
        [Header("显示")]
        [SerializeField] private bool _showGrid = true;
        [SerializeField] private Color _lineColor = new Color(1f, 1f, 1f, 0.15f);
        [SerializeField] private float _lineWidth = 0.02f;

        [Header("高亮")]
        [SerializeField] private Color _validColor = new Color(0f, 1f, 0f, 0.3f);
        [SerializeField] private Color _invalidColor = new Color(1f, 0f, 0f, 0.3f);

        private LineRenderer[] _lineRenderers;
        private GameObject _highlightPlane;
        private MeshRenderer _highlightRenderer;
        private Material _lineMaterial;
        private Material _highlightMaterial;

        private GridManager _grid => GridManager.Instance;

        private void Start()
        {
            if (_showGrid)
                CreateGridLines();

            CreateHighlightPlane();
        }

        // ==================== 网格线 ====================

        private void CreateGridLines()
        {
            int totalLines = (_grid.Rows + 1) + (_grid.Columns + 1);
            _lineRenderers = new LineRenderer[totalLines];

            var gridOrigin = _grid.GridOrigin;
            _lineMaterial = new Material(Shader.Find("Sprites/Default"));
            var lineMat = _lineMaterial;

            int index = 0;

            // 水平线（沿 X 轴）
            for (int row = 0; row <= _grid.Rows; row++)
            {
                var lr = CreateLine($"GridLine_H_{row}", lineMat);
                float z = gridOrigin.z + row * _grid.CellSize;
                lr.SetPositions(new[] {
                    new Vector3(gridOrigin.x, 0.01f, z),
                    new Vector3(gridOrigin.x + _grid.WorldWidth, 0.01f, z),
                });
                _lineRenderers[index++] = lr;
            }

            // 竖直线（沿 Z 轴）
            for (int col = 0; col <= _grid.Columns; col++)
            {
                var lr = CreateLine($"GridLine_V_{col}", lineMat);
                float x = gridOrigin.x + col * _grid.CellSize;
                lr.SetPositions(new[] {
                    new Vector3(x, 0.01f, gridOrigin.z),
                    new Vector3(x, 0.01f, gridOrigin.z + _grid.WorldHeight),
                });
                _lineRenderers[index++] = lr;
            }
        }

        private LineRenderer CreateLine(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.startColor = _lineColor;
            lr.endColor = _lineColor;
            lr.startWidth = _lineWidth;
            lr.endWidth = _lineWidth;
            lr.positionCount = 2;
            return lr;
        }

        // ==================== 高亮 ====================

        private void CreateHighlightPlane()
        {
            _highlightPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _highlightPlane.name = "GridHighlight";
            _highlightPlane.transform.SetParent(transform);
            _highlightPlane.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _highlightRenderer = _highlightPlane.GetComponent<MeshRenderer>();
            _highlightMaterial = new Material(Shader.Find("Sprites/Default"));
            _highlightRenderer.material = _highlightMaterial;

            // 透明
            var col = _validColor;
            col.a = 0.3f;
            _highlightMaterial.color = col;

            _highlightPlane.SetActive(false);
        }

        /// <summary>显示高亮（绿色=合法，红色=非法）</summary>
        public void ShowHighlight(Vector3 worldPos, int width, int height, bool valid)
        {
            _highlightPlane.SetActive(true);

            float w = width * _grid.CellSize;
            float h = height * _grid.CellSize;

            _highlightPlane.transform.position = worldPos + new Vector3(0f, 0.02f, 0f);
            _highlightPlane.transform.localScale = new Vector3(w, h, 1f);

            var col = valid ? _validColor : _invalidColor;
            _highlightRenderer.material.color = col;
        }

        /// <summary>隐藏高亮</summary>
        public void HideHighlight()
        {
            if (_highlightPlane != null)
                _highlightPlane.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
                Destroy(_lineMaterial);
            if (_highlightMaterial != null)
                Destroy(_highlightMaterial);
        }

        /// <summary>显示/隐藏网格线</summary>
        public void SetGridVisible(bool visible)
        {
            _showGrid = visible;
            if (_lineRenderers != null)
            {
                foreach (var lr in _lineRenderers)
                    lr.enabled = visible;
            }
        }
    }
}
