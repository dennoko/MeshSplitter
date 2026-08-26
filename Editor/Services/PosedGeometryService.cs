using System;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// シーンビュー上で現在見えている姿勢での頂点位置をワールド座標で取得し、
    /// Raycast 判定およびオーバーレイ描画に供給する。
    /// </summary>
    public sealed class PosedGeometryService : IDisposable
    {
        private const double RebakeInterval = 0.15;

        private Mesh _baked;
        private Vector3[] _worldPositions;
        private int _vertexCount;

        private Renderer _displayRenderer;
        private Matrix4x4 _lastMatrix;
        private double _lastBakeTime;
        private bool _valid;

        public bool IsValid => _valid;
        public Vector3[] WorldPositions => _worldPositions;

        public void Sync(Renderer source, Mesh analyzedMesh)
        {
            if (source == null || analyzedMesh == null)
            {
                Reset();
                return;
            }

            var transform = source.transform;
            var matrix = transform.localToWorldMatrix;

            bool needBake = !_valid
                            || _displayRenderer != source
                            || _lastMatrix != matrix
                            || _vertexCount != analyzedMesh.vertexCount
                            || EditorApplication.timeSinceStartup - _lastBakeTime > RebakeInterval;

            _displayRenderer = source;
            if (!needBake) return;

            _lastMatrix = matrix;
            _lastBakeTime = EditorApplication.timeSinceStartup;
            _valid = Bake(source, analyzedMesh.vertexCount);
        }

        public void Invalidate()
        {
            _valid = false;
        }

        public bool Raycast(Ray ray, MeshTopology topology, out int triangleArrayIndex)
        {
            triangleArrayIndex = -1;
            if (!_valid || topology == null || _worldPositions == null) return false;

            var triangles = topology.Triangles;
            float nearest = float.MaxValue;

            for (int i = 0; i < triangles.Length; i++)
            {
                var tri = triangles[i];
                if ((uint)tri.V0 >= _vertexCount || (uint)tri.V1 >= _vertexCount || (uint)tri.V2 >= _vertexCount)
                    continue;

                if (!RayTriangle(ray, _worldPositions[tri.V0], _worldPositions[tri.V1], _worldPositions[tri.V2], out float distance))
                    continue;

                if (distance < nearest)
                {
                    nearest = distance;
                    triangleArrayIndex = i;
                }
            }

            return triangleArrayIndex >= 0;
        }

        private bool Bake(Renderer renderer, int expectedVertexCount)
        {
            EnsureBuffers(expectedVertexCount);
            var transform = renderer.transform;

            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                try
                {
                    if (_baked == null)
                    {
                        _baked = new Mesh { name = "MM_PosedBake", hideFlags = HideFlags.HideAndDontSave };
                    }

                    skinned.BakeMesh(_baked, false);
                    if (_baked.vertexCount != expectedVertexCount) return false;

                    var local = _baked.vertices;
                    var toWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                    for (int i = 0; i < expectedVertexCount; i++)
                    {
                        _worldPositions[i] = toWorld.MultiplyPoint3x4(local[i]);
                    }
                    return true;
                }
                catch (Exception) { }
            }

            var filter = renderer.GetComponent<MeshFilter>();
            var staticMesh = filter != null ? filter.sharedMesh : (renderer is SkinnedMeshRenderer s ? s.sharedMesh : null);
            if (staticMesh == null || staticMesh.vertexCount != expectedVertexCount) return false;

            var vertices = staticMesh.vertices;
            var l2w = transform.localToWorldMatrix;
            for (int i = 0; i < expectedVertexCount; i++)
            {
                _worldPositions[i] = l2w.MultiplyPoint3x4(vertices[i]);
            }
            return true;
        }

        private void EnsureBuffers(int vertexCount)
        {
            if (_worldPositions == null || _worldPositions.Length != vertexCount)
            {
                _worldPositions = new Vector3[vertexCount];
            }
            _vertexCount = vertexCount;
        }

        private void Reset()
        {
            _valid = false;
            _displayRenderer = null;
        }

        public void Dispose()
        {
            if (_baked != null)
            {
                UnityEngine.Object.DestroyImmediate(_baked);
                _baked = null;
            }
            Reset();
        }

        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 e1 = b - a;
            Vector3 e2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-9f && det < 1e-9f) return false;

            float invDet = 1f / det;
            Vector3 t = ray.origin - a;
            float u = Vector3.Dot(t, p) * invDet;
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(t, e1);
            float v = Vector3.Dot(ray.direction, q) * invDet;
            if (v < 0f || u + v > 1f) return false;

            float hit = Vector3.Dot(e2, q) * invDet;
            if (hit <= 1e-6f) return false;

            distance = hit;
            return true;
        }
    }
}
