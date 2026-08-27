using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// シーンビュー上でメッシュを直接クリック/ドラッグ選択し、オーバーレイを描画する。
    /// </summary>
    public sealed class SceneSelectionOverlay : IDisposable
    {
        private static readonly Color SelectedColor = new Color32(0x9d, 0xd2, 0xff, 0xE6);
        private static readonly Color HoverColor = new Color32(0xff, 0xb7, 0x4d, 0xFF);
        /// <summary>1 回の描画で線を張る三角形数の上限 (走査範囲ではなく描画数の上限)。</summary>
        private const int MaxDrawTriangles = 20000;
        /// <summary>太さ付きで 1 本ずつ描く線分数の上限。これを超えたらバッチ描画に切り替える。</summary>
        private const int ThickLineSegmentLimit = 2000;
        private const float LineThickness = 2.0f;

        private readonly Action<IMmAction> _dispatch;
        private readonly PosedGeometryService _geometry = new PosedGeometryService();

        private MmState _state;
        private int _hoverGroup = -1;
        private Vector2 _lastHoverPosition = new Vector2(float.NaN, float.NaN);
        private bool _painting;
        private bool _paintModeRemove;
        private int _lastPaintedGroup = -1;

        private Vector3[] _selectedLines = Array.Empty<Vector3>();
        private Vector3[] _hoverLines = Array.Empty<Vector3>();

        // 線バッファの構築は選択メッシュ全体の走査を伴うため、入力が変わったときだけやり直す。
        // 判定材料はすべてマネージドな値なので、ドメインリロードで丸ごと消えても
        // 「キャッシュ無し」の状態から再構築されるだけで、古い座標が残ることはない。
        private readonly List<Vector3> _lineBuffer = new List<Vector3>();
        private readonly HashSet<long> _edgeKeys = new HashSet<long>();
        private int _selectionRevision;

        private MeshTopology _selCacheTopology;
        private int _selCacheGeneration = -1;
        private int _selCacheRevision = -1;
        private MmPickMode _selCachePickMode;

        private MeshTopology _hovCacheTopology;
        private int _hovCacheGeneration = -1;
        private int _hovCacheGroup = -1;
        private MmPickMode _hovCachePickMode;

        public SceneSelectionOverlay(Action<IMmAction> dispatch)
        {
            _dispatch = dispatch;
            SceneView.duringSceneGui += OnSceneGui;
        }

        public void Dispose()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            _geometry.Dispose();
        }

        public void Render(MmState state)
        {
            // Selection は MmState.Clone() のたびに別インスタンスになるため、
            // 参照比較では常に「変化あり」となり毎回シーンビューを再描画してしまう。内容で比べる。
            bool selectionChanged = _state == null || !_state.Selection.SetEquals(state.Selection);
            if (selectionChanged) _selectionRevision++;

            bool needRepaint = _state == null
                               || _state.Topology != state.Topology
                               || selectionChanged
                               || _state.PickMode != state.PickMode
                               || _state.SelectionMode != state.SelectionMode
                               || _state.Source != state.Source
                               || _state.SceneOverlayEnabled != state.SceneOverlayEnabled
                               || _state.SceneSelectionEnabled != state.SceneSelectionEnabled
                               || _state.SceneOverlayXray != state.SceneOverlayXray;

            if (_state == null || _state.Source != state.Source || _state.Topology != state.Topology)
            {
                _geometry.Invalidate();
            }

            if (_state == null || _state.Source != state.Source || _state.Topology != state.Topology || !state.SceneSelectionEnabled)
            {
                _hoverGroup = -1;
                _lastHoverPosition = new Vector2(float.NaN, float.NaN);
            }

            _state = state;
            if (needRepaint) SceneView.RepaintAll();
        }

        private void OnSceneGui(SceneView sceneView)
        {
            var state = _state;
            if (state == null || state.Source == null || state.Topology == null) return;
            if (!state.SceneOverlayEnabled && !state.SceneSelectionEnabled) return;

            _geometry.Sync(state.Source, state.Topology.Mesh);
            if (!_geometry.IsValid) return;

            var e = Event.current;
            switch (e.type)
            {
                case EventType.KeyDown:
                    if (state.SceneSelectionEnabled && e.keyCode == KeyCode.Escape)
                    {
                        _dispatch(new ToggleSceneSelection());
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    if (state.SceneOverlayEnabled) DrawOverlay(state, sceneView);
                    break;

                case EventType.MouseMove:
                case EventType.Layout:
                    if (state.SceneSelectionEnabled && !_painting) UpdateHover(state, sceneView, e.mousePosition);
                    break;

                case EventType.MouseDown:
                    if (state.SceneSelectionEnabled && IsSelectionDrag(e))
                    {
                        int hit = PickGroupUnderMouse(state, SceneView.currentDrawingSceneView, e.mousePosition);
                        if (hit >= 0)
                        {
                            _painting = true;
                            _paintModeRemove = state.Selection.Contains(hit);
                            _lastPaintedGroup = hit;
                            _dispatch(new ModifySelection(new[] { hit }, !_paintModeRemove));
                            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_painting && state.SceneSelectionEnabled)
                    {
                        int hit = PickGroupUnderMouse(state, SceneView.currentDrawingSceneView, e.mousePosition);
                        if (hit >= 0 && hit != _lastPaintedGroup)
                        {
                            _lastPaintedGroup = hit;
                            _dispatch(new ModifySelection(new[] { hit }, !_paintModeRemove));
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (_painting)
                    {
                        _painting = false;
                        _lastPaintedGroup = -1;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        private static bool IsSelectionDrag(Event e)
        {
            return e.button == 0 && !e.alt && !e.control && !e.command;
        }

        private void UpdateHover(MmState state, SceneView sceneView, Vector2 mousePos)
        {
            if (!state.SceneSelectionEnabled)
            {
                if (_hoverGroup != -1)
                {
                    _hoverGroup = -1;
                    sceneView.Repaint();
                }
                return;
            }

            if (mousePos == _lastHoverPosition) return;
            _lastHoverPosition = mousePos;

            int hit = PickGroupUnderMouse(state, sceneView, mousePos);
            if (hit != _hoverGroup)
            {
                _hoverGroup = hit;
                sceneView.Repaint();
            }
        }

        private int PickGroupUnderMouse(MmState state, SceneView sceneView, Vector2 mousePos)
        {
            if (sceneView == null || state.Topology == null) return -1;
            var ray = HandleUtility.GUIPointToWorldRay(mousePos);
            if (!_geometry.Raycast(ray, state.Topology, out int triIndex)) return -1;

            var groupOf = state.Topology.GroupOf(state.PickMode);
            return (triIndex >= 0 && triIndex < groupOf.Length) ? groupOf[triIndex] : -1;
        }

        private void DrawOverlay(MmState state, SceneView sceneView)
        {
            RebuildLineBuffers(state);

            var prevZTest = Handles.zTest;
            Handles.zTest = state.SceneOverlayXray ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual;

            DrawLineSegments(_selectedLines, MmColorSettings.SceneSelectedColor);
            if (state.SceneSelectionEnabled && _hoverGroup >= 0)
            {
                DrawLineSegments(_hoverLines, MmColorSettings.SceneHoverColor);
            }

            Handles.zTest = prevZTest;
        }

        /// <summary>
        /// 線分列をまとめて描画する。Handles.DrawLine は 1 本ごとにドローコールを発行するため、
        /// 本数が多いときは太さを諦めて Handles.DrawLines の一括描画に切り替える。
        /// </summary>
        private static void DrawLineSegments(Vector3[] lines, Color color)
        {
            if (lines == null || lines.Length < 2) return;

            Handles.color = color;
            if (lines.Length <= ThickLineSegmentLimit * 2)
            {
                for (int i = 0; i < lines.Length; i += 2)
                {
                    Handles.DrawLine(lines[i], lines[i + 1], LineThickness);
                }
                return;
            }

            Handles.DrawLines(lines);
        }

        private void RebuildLineBuffers(MmState state)
        {
            var topology = state.Topology;
            var world = _geometry.IsValid ? _geometry.WorldPositions : null;
            if (topology == null || world == null)
            {
                ClearLineCache();
                return;
            }

            int generation = _geometry.Generation;
            int hoverGroup = (state.SceneSelectionEnabled && _hoverGroup >= 0) ? _hoverGroup : -1;

            // 選択線とホバー線は変わるタイミングが違う (ホバーはマウス移動のたびに変わる) ので、
            // それぞれ独立にキャッシュし、変わった方だけ組み直す。
            if (_selCacheTopology != topology
                || _selCacheGeneration != generation
                || _selCacheRevision != _selectionRevision
                || _selCachePickMode != state.PickMode)
            {
                _selCacheTopology = topology;
                _selCacheGeneration = generation;
                _selCacheRevision = _selectionRevision;
                _selCachePickMode = state.PickMode;
                _selectedLines = BuildLines(topology, state.PickMode, world, state.Selection, -1);
            }

            if (_hovCacheTopology != topology
                || _hovCacheGeneration != generation
                || _hovCacheGroup != hoverGroup
                || _hovCachePickMode != state.PickMode)
            {
                _hovCacheTopology = topology;
                _hovCacheGeneration = generation;
                _hovCacheGroup = hoverGroup;
                _hovCachePickMode = state.PickMode;
                _hoverLines = hoverGroup >= 0
                    ? BuildLines(topology, state.PickMode, world, null, hoverGroup)
                    : Array.Empty<Vector3>();
            }
        }

        /// <summary>
        /// 対象グループの三角形の輪郭線を線分列に組む。
        /// <paramref name="selection"/> を渡すと選択集合、そうでなければ
        /// <paramref name="hoverGroup"/> 単独のグループを対象にする。
        /// </summary>
        private Vector3[] BuildLines(
            MeshTopology topology, MmPickMode mode, Vector3[] world, HashSet<int> selection, int hoverGroup)
        {
            var groupOf = topology.GroupOf(mode);
            var triangles = topology.Triangles;
            uint vertexCount = (uint)world.Length;

            _lineBuffer.Clear();
            _edgeKeys.Clear();

            // 上限は「走査する三角形数」ではなく「線を張る三角形数」に掛ける。
            // 走査を打ち切ると、選択範囲がインデックスの後半にあるメッシュ (2 万三角形超) で
            // 選択したはずの部分がシーンビューに一切表示されなくなる。
            int budget = MaxDrawTriangles;
            for (int i = 0; i < triangles.Length && budget > 0; i++)
            {
                int g = groupOf[i];
                bool hit = selection != null ? selection.Contains(g) : g == hoverGroup;
                if (!hit) continue;
                budget--;

                var tri = triangles[i];
                if ((uint)tri.V0 >= vertexCount || (uint)tri.V1 >= vertexCount || (uint)tri.V2 >= vertexCount) continue;

                AddEdge(world, tri.V0, tri.V1);
                AddEdge(world, tri.V1, tri.V2);
                AddEdge(world, tri.V2, tri.V0);
            }

            return _lineBuffer.Count > 0 ? _lineBuffer.ToArray() : Array.Empty<Vector3>();
        }

        /// <summary>
        /// 辺を線分列に加える。隣り合う三角形が共有する辺は 2 回出てくるので、
        /// 頂点番号の組をキーにして 1 本だけ張る (閉じたメッシュなら線分数がおよそ半分になる)。
        /// </summary>
        private void AddEdge(Vector3[] world, int a, int b)
        {
            if (a == b) return;

            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!_edgeKeys.Add(key)) return;

            _lineBuffer.Add(world[a]);
            _lineBuffer.Add(world[b]);
        }

        private void ClearLineCache()
        {
            _selectedLines = Array.Empty<Vector3>();
            _hoverLines = Array.Empty<Vector3>();
            _selCacheTopology = null;
            _selCacheGeneration = -1;
            _selCacheRevision = -1;
            _hovCacheTopology = null;
            _hovCacheGeneration = -1;
            _hovCacheGroup = -1;
        }
    }
}
