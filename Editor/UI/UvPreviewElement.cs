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
        private static readonly Color32 TileBorderColor = new Color32(0x80, 0x80, 0x80, 0xFF);

        private const int MaxTextureSize = 1024;
        private const int MaxBackgroundSize = 1024;
        private const int WireframeTriangleLimit = 60000;
        private const float ClickThreshold = 4f;

        // 背景にテクスチャを敷いているときの面の塗りの濃さ (0-255)。
        // テクスチャを完全に隠さない程度に留める。
        private const int SelectedFillAlpha = 140;
        private const int UnselectedFillAlpha = 90;

        private const byte CoverageNone = 0;
        private const byte CoverageUnselected = 1;
        private const byte CoverageSelected = 2;

        private readonly VisualElement _marquee;

        private MeshTopology _topology;
        private MmPickMode _mode = MmPickMode.UvIsland;
        private HashSet<int> _selection = new HashSet<int>();
        private bool _addMode = true;

        private Texture2D _texture;
        private Rect _view = new Rect(0f, 0f, 1f, 1f);
        private Vector2 _elementSize;

        private UvBackground _background = UvBackground.None;
        private Color32[] _backgroundPixels;
        private int _backgroundWidth;
        private int _backgroundHeight;
        private bool _backgroundReadFailed;

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
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ReleaseTexture();
                ReleaseBackgroundPixels();
            });
        }

        public void SetSource(
            MeshTopology topology, MmPickMode mode, IReadOnlyCollection<int> selection, bool addMode,
            UvBackground background)
        {
            bool topologyChanged = !ReferenceEquals(_topology, topology) || _mode != mode;
            bool selectionChanged = !SelectionMatches(selection);
            bool backgroundChanged = !_background.Equals(background);

            _topology = topology;
            _mode = mode;
            if (selectionChanged)
            {
                _selection = selection != null ? new HashSet<int>(selection) : new HashSet<int>();
            }
            _addMode = addMode;
            if (backgroundChanged)
            {
                // タイリング (_MainTex_ST) だけの変化なら読み直す必要はない
                if (!ReferenceEquals(_background.Texture, background.Texture)) ReleaseBackgroundPixels();
                _background = background;
            }

            // Repaint() は全三角形をソフトウェアラスタライズするため、見た目に影響しない
            // 状態変化 (Prefab 名の入力など) では走らせない。
            if (!topologyChanged && !selectionChanged && !backgroundChanged) return;

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
            bool hasBackground = FillBackground(pixels, width, height);

            if (_topology != null)
            {
                var groupOf = _topology.GroupOf(_mode);
                var triangles = _topology.Triangles;
                bool drawWire = triangles.Length <= WireframeTriangleLimit;

                // テクスチャを敷いているときは非選択面を塗らず、ワイヤーフレームだけで示す。
                // ワイヤーを省略する高ポリメッシュでは面が見えなくなるので、薄く塗って補う。
                int selectedAlpha = hasBackground ? SelectedFillAlpha : 255;
                int unselectedAlpha = hasBackground ? (drawWire ? 0 : UnselectedFillAlpha) : 255;

                // 面ごとに直接ブレンドすると、隣り合う三角形が共有する辺や UV が重なる部分で
                // 二重に塗られてしまう。一度カバレッジを取ってから、ピクセルごとに一回だけ合成する。
                var coverage = new byte[width * height];
                for (int i = 0; i < triangles.Length; i++)
                {
                    bool selected = _selection.Contains(groupOf[i]);
                    if ((selected ? selectedAlpha : unselectedAlpha) <= 0) continue;

                    var t = triangles[i];
                    MarkTriangle(coverage, width, height,
                        ToPixel(t.U0, width, height), ToPixel(t.U1, width, height), ToPixel(t.U2, width, height),
                        selected ? CoverageSelected : CoverageUnselected);
                }

                for (int i = 0; i < coverage.Length; i++)
                {
                    if (coverage[i] == CoverageNone) continue;

                    bool selected = coverage[i] == CoverageSelected;
                    int alpha = selected ? selectedAlpha : unselectedAlpha;
                    Color32 color = selected ? SelectedColor : FillColor;
                    pixels[i] = alpha >= 255 ? color : Blend(pixels[i], color, alpha);
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

            if (hasBackground) DrawTileBorder(pixels, width, height);

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

        private void ReleaseBackgroundPixels()
        {
            _backgroundPixels = null;
            _backgroundReadFailed = false;
        }

        /// <summary>
        /// 背景をピクセル配列へ書き込む。メインテクスチャを敷けたときだけ true。
        /// </summary>
        private bool FillBackground(Color32[] pixels, int width, int height)
        {
            if (!TryReadBackgroundPixels())
            {
                for (int i = 0; i < pixels.Length; i++) pixels[i] = BackgroundColor;
                return false;
            }

            // シェーダー側と同じ変換 (uv * _MainTex_ST.xy + _MainTex_ST.zw) でテクスチャ座標を求める。
            // 範囲外は繰り返さず背景色のままにして、テクスチャ 1 枚分がどこに乗るかを分かるようにする。
            Vector2 scale = _background.Scale;
            Vector2 offset = _background.Offset;

            float uStep = _view.width / width;
            float vStep = _view.height / height;
            float uStart = _view.xMin + uStep * 0.5f;
            float vStart = _view.yMin + vStep * 0.5f;

            for (int y = 0; y < height; y++)
            {
                float v = vStart + vStep * y;
                int srcY = Texel(v * scale.y + offset.y, _backgroundHeight);
                int dstRow = y * width;

                if (srcY < 0)
                {
                    for (int x = 0; x < width; x++) pixels[dstRow + x] = BackgroundColor;
                    continue;
                }

                int srcRow = srcY * _backgroundWidth;
                for (int x = 0; x < width; x++)
                {
                    float u = uStart + uStep * x;
                    int srcX = Texel(u * scale.x + offset.x, _backgroundWidth);
                    pixels[dstRow + x] = srcX < 0 ? BackgroundColor : _backgroundPixels[srcRow + srcX];
                }
            }
            return true;
        }

        /// <summary>テクスチャ座標をテクセル位置へ変換する。0-1 の外 (繰り返し側) は -1。</summary>
        private static int Texel(float t, int size)
        {
            if (!(t >= 0f) || t >= 1f) return -1;
            int i = (int)(t * size);
            return i < size ? i : size - 1;
        }

        /// <summary>
        /// メインテクスチャの内容を CPU 側へ読み出してキャッシュする。
        /// アバターのテクスチャは圧縮済みかつ Read/Write 無効なのが普通で GetPixels32 を直接呼べないため、
        /// 一度 RenderTexture へ Blit してから読み戻す。
        /// </summary>
        private bool TryReadBackgroundPixels()
        {
            if (_backgroundPixels != null) return true;
            if (_backgroundReadFailed || !_background.IsValid) return false;

            // 失敗しても Repaint のたびに読み直さないよう、先に落としておく
            _backgroundReadFailed = true;

            Texture2D source = _background.Texture;
            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);
            int longest = Mathf.Max(width, height);
            if (longest > MaxBackgroundSize)
            {
                float ratio = (float)MaxBackgroundSize / longest;
                width = Mathf.Max(1, Mathf.RoundToInt(width * ratio));
                height = Mathf.Max(1, Mathf.RoundToInt(height * ratio));
            }

            // sRGB で読み書きすると変換が往復で打ち消し合い、元のテクスチャのバイト列がそのまま得られる。
            // プレビュー用テクスチャも sRGB 変換なしで表示されるので、これで見た目が一致する。
            var renderTexture = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;

                readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                readback.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readback.Apply(false);

                _backgroundPixels = readback.GetPixels32();
                _backgroundWidth = width;
                _backgroundHeight = height;
                _backgroundReadFailed = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Mesh Splitter] {source.name} をプレビューに読み込めませんでした: {e.Message}");
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            }

            return !_backgroundReadFailed;
        }

        /// <summary>
        /// UV の 0-1 範囲の枠。UV 空間の基準が分かるように引く。
        /// UV 空間の軸に平行な矩形なので、DrawLine は使わず画面内に収まる範囲だけ走査する
        /// (拡大するほど辺が長くなり、DrawLine だと画面外まで延々となぞることになるため)。
        /// </summary>
        private void DrawTileBorder(Color32[] pixels, int width, int height)
        {
            Vector2 min = ToPixel(new Vector2(0f, 0f), width, height);
            Vector2 max = ToPixel(new Vector2(1f, 1f), width, height);

            int left = ToPixelIndex(min.x), right = ToPixelIndex(max.x);
            int bottom = ToPixelIndex(min.y), top = ToPixelIndex(max.y);

            for (int x = Mathf.Max(left, 0); x <= Mathf.Min(right, width - 1); x++)
            {
                if (bottom >= 0 && bottom < height) pixels[bottom * width + x] = TileBorderColor;
                if (top >= 0 && top < height) pixels[top * width + x] = TileBorderColor;
            }
            for (int y = Mathf.Max(bottom, 0); y <= Mathf.Min(top, height - 1); y++)
            {
                if (left >= 0 && left < width) pixels[y * width + left] = TileBorderColor;
                if (right >= 0 && right < width) pixels[y * width + right] = TileBorderColor;
            }
        }

        /// <summary>RoundToInt が桁あふれしないよう、十分に画面外と分かる範囲へ丸めてから整数化する。</summary>
        private static int ToPixelIndex(float value) => Mathf.RoundToInt(Mathf.Clamp(value, -1e6f, 1e6f));

        private Vector2 ToPixel(Vector2 uv, int width, int height)
        {
            return new Vector2(
                (uv.x - _view.xMin) / _view.width * width,
                (uv.y - _view.yMin) / _view.height * height);
        }

        /// <summary>三角形が覆うピクセルにカバレッジ値を書き込む。選択面 (値が大きい方) を優先する。</summary>
        private static void MarkTriangle(
            byte[] coverage, int width, int height, Vector2 a, Vector2 b, Vector2 c, byte value)
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
                    if (coverage[row + x] < value) coverage[row + x] = value;
                }
            }
        }

        private static Color32 Blend(Color32 dst, Color32 src, int alpha)
        {
            return new Color32(
                (byte)(dst.r + (src.r - dst.r) * alpha / 255),
                (byte)(dst.g + (src.g - dst.g) * alpha / 255),
                (byte)(dst.b + (src.b - dst.b) * alpha / 255),
                0xFF);
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
