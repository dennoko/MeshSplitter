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
        private const int MaxDrawTriangles = 20000;

        private readonly Action<IMmAction> _dispatch;
        private readonly PosedGeometryService _geometry = new PosedGeometryService();

        private MmState _state;
        private int _hoverGroup = -1;
        private Vector2 _lastHoverPosition = new Vector2(float.NaN, float.NaN);
        private bool _painting;
        private int _lastPaintedGroup = -1;

        private Vector3[] _selectedLines = Array.Empty<Vector3>();
        private Vector3[] _hoverLines = Array.Empty<Vector3>();

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
            bool needRepaint = _state == null
                               || _state.Topology != state.Topology
                               || _state.Selection != state.Selection
                               || _state.PickMode != state.PickMode
                               || _state.SelectionMode != state.SelectionMode
                               || _state.Source != state.Source
                               || _state.SceneOverlayEnabled != state.SceneOverlayEnabled
                               || _state.SceneSelectionEnabled != state.SceneSelectionEnabled
                               || _state.SceneOverlayXray != state.SceneOverlayXray;

            if (_state == null || _state.Source != state.Source || _state.Topology != state.Topology)
            {
                _geometry.Invalidate();
                _hoverGroup = -1;
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
                case EventType.Repaint:
                    if (state.SceneOverlayEnabled) DrawOverlay(state, sceneView);
                    break;

                case EventType.MouseMove:
                case EventType.Layout:
                    if (state.SceneSelectionEnabled && !_painting) UpdateHover(state, sceneView, e.mousePosition);
                    break;

                case EventType.MouseDown:
                    if (state.SceneSelectionEnabled && IsSelectionDrag(e) && TryPaint(state, e))
                    {
                        _painting = true;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_painting && state.SceneSelectionEnabled && TryPaint(state, e))
                    {
                        e.Use();
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
            if (mousePos == _lastHoverPosition) return;
            _lastHoverPosition = mousePos;

            int hit = PickGroupUnderMouse(state, sceneView, mousePos);
            if (hit != _hoverGroup)
            {
                _hoverGroup = hit;
                sceneView.Repaint();
            }
        }

        private bool TryPaint(MmState state, Event e)
        {
            int hit = PickGroupUnderMouse(state, SceneView.currentDrawingSceneView, e.mousePosition);
            if (hit < 0 || hit == _lastPaintedGroup) return false;

            _lastPaintedGroup = hit;
            bool add = state.SelectionMode == MmSelectionMode.Add;
            _dispatch(new ModifySelection(new[] { hit }, add));
            return true;
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

            if (_selectedLines.Length > 0)
            {
                Handles.color = SelectedColor;
                Handles.DrawLines(_selectedLines);
            }

            if (_hoverLines.Length > 0 && _hoverGroup >= 0)
            {
                Handles.color = HoverColor;
                Handles.DrawLines(_hoverLines);
            }

            Handles.zTest = prevZTest;
        }

        private void RebuildLineBuffers(MmState state)
        {
            var topology = state.Topology;
            if (topology == null || !_geometry.IsValid)
            {
                _selectedLines = Array.Empty<Vector3>();
                _hoverLines = Array.Empty<Vector3>();
                return;
            }

            var groupOf = topology.GroupOf(state.PickMode);
            var triangles = topology.Triangles;
            var world = _geometry.WorldPositions;
            if (world == null) return;

            var selLines = new List<Vector3>();
            var hovLines = new List<Vector3>();

            int count = Mathf.Min(triangles.Length, MaxDrawTriangles);
            for (int i = 0; i < count; i++)
            {
                int g = groupOf[i];
                bool isSel = state.Selection.Contains(g);
                bool isHov = g == _hoverGroup;
                if (!isSel && !isHov) continue;

                var tri = triangles[i];
                if ((uint)tri.V0 >= world.Length || (uint)tri.V1 >= world.Length || (uint)tri.V2 >= world.Length) continue;

                Vector3 a = world[tri.V0], b = world[tri.V1], c = world[tri.V2];

                if (isSel)
                {
                    selLines.Add(a); selLines.Add(b);
                    selLines.Add(b); selLines.Add(c);
                    selLines.Add(c); selLines.Add(a);
                }
                if (isHov)
                {
                    hovLines.Add(a); hovLines.Add(b);
                    hovLines.Add(b); hovLines.Add(c);
                    hovLines.Add(c); hovLines.Add(a);
                }
            }

            _selectedLines = selLines.ToArray();
            _hoverLines = hovLines.ToArray();
        }
    }
}
