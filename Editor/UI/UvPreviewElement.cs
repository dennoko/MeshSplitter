using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// UV ビュー上でアイランド / 連結ポリゴンをクリック・ドラッグ選択するプレビュー。
    /// </summary>
    public sealed class UvPreviewElement : VisualElement
    {
        public event Action<IReadOnlyCollection<int>, bool> SelectionRequested;

        private static readonly Color32 BackgroundColor = new Color32(0x12, 0x12, 0x12, 0xFF);
        private static readonly Color32 FillColor = new Color32(0x3a, 0x3a, 0x3a, 0xFF);
        private static readonly Color32 SelectedColor = new Color32(0x2f, 0x6d, 0xa8, 0xFF);
        private static readonly Color32 WireColor = new Color32(0x55, 0x55, 0x55, 0xFF);
        private static readonly Color32 SelectedWireColor = new Color32(0x9d, 0xd2, 0xff, 0xFF);

        private const int MaxTextureSize = 1024;
        private const int WireframeTriangleLimit = 60000;
        private const float ClickThreshold = 4f;

        private readonly VisualElement _marquee;

        private MeshTopology _topology;
        private MmPickMode _mode = MmPickMode.UvIsland;
        private HashSet<int> _selection = new HashSet<int>();
        private bool _addMode = true;

        private Texture2D _texture;
        private Rect _view = new Rect(0f, 0f, 1f, 1f);
        private Vector2 _elementSize;

        private bool _dragging;
        private bool _panning;
        private Vector2 _dragStart;
        private Vector2 _dragStartUv;

        public UvPreviewElement()
        {
            focusable = true;
            style.overflow = Overflow.Hidden;

            _marquee = new VisualElement { pickingMode = PickingMode.Ignore };
            _marquee.AddToClassList("mm-marquee");
            _marquee.style.position = Position.Absolute;
            _marquee.style.display = DisplayStyle.None;
            Add(_marquee);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<DetachFromPanelEvent>(_ => ReleaseTexture());
        }

        public void SetSource(
            MeshTopology topology, MmPickMode mode, IReadOnlyCollection<int> selection, bool addMode)
        {
            bool topologyChanged = !ReferenceEquals(_topology, topology) || _mode != mode;
            bool selectionChanged = !SelectionMatches(selection);

            _topology = topology;
            _mode = mode;
            if (selectionChanged)
            {
                _selection = selection != null ? new HashSet<int>(selection) : new HashSet<int>();
            }
            _addMode = addMode;

            // Repaint() は全三角形をソフトウェアラスタライズするため、見た目に影響しない
            // 状態変化 (Prefab 名の入力など) では走らせない。
            if (!topologyChanged && !selectionChanged) return;

            if (topologyChanged) ResetView();
            Repaint();
        }

        /// <summary>現在保持している選択と内容が一致するか。</summary>
        private bool SelectionMatches(IReadOnlyCollection<int> other)
        {
            if (other == null) return _selection.Count == 0;
            if (other.Count != _selection.Count) return false;
            foreach (int group in other)
            {
                if (!_selection.Contains(group)) return false;
            }
            return true;
        }

        public void ResetView()
        {
            _view = FitView();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            var size = new Vector2(evt.newRect.width, evt.newRect.height);
            if (size.x < 1f || size.y < 1f) return;
            if ((size - _elementSize).sqrMagnitude < 1f) return;

            _elementSize = size;
            _view = FitView();
            Repaint();
        }

        private Rect FitView()
        {
            if (_elementSize.x < 1f || _elementSize.y < 1f) return new Rect(0f, 0f, 1f, 1f);

            float aspect = _elementSize.x / _elementSize.y;
            return aspect >= 1f
                ? new Rect(0.5f - aspect * 0.5f, 0f, aspect, 1f)
                : new Rect(0f, 0.5f - 0.5f / aspect, 1f, 1f / aspect);
        }

        private void Repaint()
        {
            int width = Mathf.Clamp(Mathf.RoundToInt(_elementSize.x), 16, MaxTextureSize);
            int height = Mathf.Clamp(Mathf.RoundToInt(_elementSize.y), 16, MaxTextureSize);

            if (_texture == null || _texture.width != width || _texture.height != height)
            {
                ReleaseTexture();
                _texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = BackgroundColor;

            if (_topology != null)
            {
                var groupOf = _topology.GroupOf(_mode);
                var triangles = _topology.Triangles;
                bool drawWire = triangles.Length <= WireframeTriangleLimit;

                for (int i = 0; i < triangles.Length; i++)
                {
                    bool selected = _selection.Contains(groupOf[i]);
                    var t = triangles[i];
                    FillTriangle(pixels, width, height,
                        ToPixel(t.U0, width, height), ToPixel(t.U1, width, height), ToPixel(t.U2, width, height),
                        selected ? SelectedColor : FillColor);
                }

                if (drawWire)
                {
                    for (int i = 0; i < triangles.Length; i++)
                    {
                        bool selected = _selection.Contains(groupOf[i]);
                        var color = selected ? SelectedWireColor : WireColor;
                        var t = triangles[i];
                        var p0 = ToPixel(t.U0, width, height);
                        var p1 = ToPixel(t.U1, width, height);
                        var p2 = ToPixel(t.U2, width, height);
                        DrawLine(pixels, width, height, p0, p1, color);
                        DrawLine(pixels, width, height, p1, p2, color);
                        DrawLine(pixels, width, height, p2, p0, color);
                    }
                }
            }

            _texture.SetPixels32(pixels);
            _texture.Apply(false);
            style.backgroundImage = new StyleBackground(_texture);
        }

        private void ReleaseTexture()
        {
            if (_texture == null) return;
            UnityEngine.Object.DestroyImmediate(_texture);
            _texture = null;
        }

        private Vector2 ToPixel(Vector2 uv, int width, int height)
        {
            return new Vector2(
                (uv.x - _view.xMin) / _view.width * width,
                (uv.y - _view.yMin) / _view.height * height);
        }

        private static void FillTriangle(
            Color32[] pixels, int width, int height, Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (minX > maxX || minY > maxY) return;

            float area = Edge(a, b, c);
            if (Mathf.Abs(area) < 1e-6f) return;
            float inv = 1f / area;

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * width;
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(b, c, p) * inv;
                    float w1 = Edge(c, a, p) * inv;
                    float w2 = Edge(a, b, p) * inv;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    pixels[row + x] = color;
                }
            }
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 p)
        {
            return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }

        private static void DrawLine(Color32[] pixels, int width, int height, Vector2 from, Vector2 to, Color32 color)
        {
            int x0 = Mathf.RoundToInt(from.x), y0 = Mathf.RoundToInt(from.y);
            int x1 = Mathf.RoundToInt(to.x), y1 = Mathf.RoundToInt(to.y);

            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int guard = dx - dy + 4;

            while (guard-- > 0)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height) pixels[y0 * width + x0] = color;
                if (x0 == x1 && y0 == y1) break;

                int e2 = err * 2;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private Vector2 ToUv(Vector2 local)
        {
            if (_elementSize.x < 1f || _elementSize.y < 1f) return Vector2.zero;
            return new Vector2(
                _view.xMin + local.x / _elementSize.x * _view.width,
                _view.yMin + (1f - local.y / _elementSize.y) * _view.height);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_topology == null) return;

            _dragStart = evt.localPosition;
            _dragStartUv = ToUv(_dragStart);

            // 右ドラッグ / 中ドラッグ / Alt + ドラッグでパンする (ドキュメント記載の操作)
            if (evt.altKey || evt.button == 1 || evt.button == 2)
            {
                _panning = true;
            }
            else if (evt.button == 0)
            {
                _dragging = true;
                _marquee.style.display = DisplayStyle.Flex;
                UpdateMarquee(_dragStart, _dragStart);
            }
            else
            {
                return;
            }

            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_panning)
            {
                var current = ToUv(evt.localPosition);
                var delta = _dragStartUv - current;
                _view.position += delta;
                Repaint();
                evt.StopPropagation();
                return;
            }

            if (!_dragging) return;
            UpdateMarquee(_dragStart, evt.localPosition);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging && !_panning) return;

            this.ReleasePointer(evt.pointerId);
            _marquee.style.display = DisplayStyle.None;

            bool wasDragging = _dragging;
            _dragging = false;
            _panning = false;
            evt.StopPropagation();

            if (!wasDragging || _topology == null) return;

            var end = (Vector2)evt.localPosition;
            if ((end - _dragStart).magnitude <= ClickThreshold)
            {
                int group = _topology.PickGroupAtUv(_mode, ToUv(end));
                if (group >= 0)
                {
                    bool add = !_selection.Contains(group);
                    SelectionRequested?.Invoke(new[] { group }, add);
                }
                return;
            }

            var endUv = ToUv(end);
            var rect = Rect.MinMaxRect(
                Mathf.Min(_dragStartUv.x, endUv.x), Mathf.Min(_dragStartUv.y, endUv.y),
                Mathf.Max(_dragStartUv.x, endUv.x), Mathf.Max(_dragStartUv.y, endUv.y));

            var groups = _topology.PickGroupsInRect(_mode, rect);
            if (groups.Count > 0) SelectionRequested?.Invoke(groups, _addMode);
        }

        private void OnWheel(WheelEvent evt)
        {
            if (_topology == null) return;

            var pivot = ToUv(evt.localMousePosition);
            float scale = Mathf.Pow(1.1f, evt.delta.y);
            float width = Mathf.Clamp(_view.width * scale, 0.01f, 8f);
            float height = width * (_view.height / _view.width);

            _view = new Rect(
                pivot.x - (pivot.x - _view.xMin) * (width / _view.width),
                pivot.y - (pivot.y - _view.yMin) * (height / _view.height),
                width, height);

            Repaint();
            evt.StopPropagation();
        }

        private void UpdateMarquee(Vector2 from, Vector2 to)
        {
            _marquee.style.left = Mathf.Min(from.x, to.x);
            _marquee.style.top = Mathf.Min(from.y, to.y);
            _marquee.style.width = Mathf.Abs(to.x - from.x);
            _marquee.style.height = Mathf.Abs(to.y - from.y);
        }
    }
}
