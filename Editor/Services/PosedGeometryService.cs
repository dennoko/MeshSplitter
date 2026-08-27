using System;
using System.Collections.Generic;
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
        /// <summary>ポーズが変わっていないかを確認しにいく最短間隔 (秒)。</summary>
        private const double PoseCheckInterval = 0.15;

        private Mesh _baked;
        // vertices プロパティは呼ぶたびに配列を確保するため、使い回せる List で受ける。
        private readonly List<Vector3> _localPositions = new List<Vector3>();
        private Vector3[] _worldPositions;
        private int _vertexCount;

        private Renderer _displayRenderer;
        private Matrix4x4 _lastMatrix;
        private double _lastPoseCheckTime;
        private int _lastPoseHash;
        private bool _hasPoseHash;
        private bool _valid;
        private bool _reloadHookInstalled;

        public bool IsValid => _valid;
        public Vector3[] WorldPositions => _worldPositions;

        /// <summary>
        /// <see cref="WorldPositions"/> の内容が変わりうるたびに増える番号。
        /// 呼び出し側はこれを比べるだけで「座標がキャッシュ時点から動いたか」を判定できる。
        /// </summary>
        public int Generation { get; private set; }

        public PosedGeometryService()
        {
            // ドメインリロードで OnDisable → Dispose が走らない構成でも、HideAndDontSave の
            // ベイク用メッシュが参照を失ったまま残らないよう保険をかける。
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseBakedMesh;
            _reloadHookInstalled = true;
        }

        public void Sync(Renderer source, Mesh analyzedMesh)
        {
            if (source == null || analyzedMesh == null)
            {
                Reset();
                return;
            }

            var matrix = source.transform.localToWorldMatrix;
            bool needBake = !_valid
                            || _displayRenderer != source
                            || _lastMatrix != matrix
                            || _vertexCount != analyzedMesh.vertexCount;

            _displayRenderer = source;

            // スキニング結果はトランスフォームだけでは決まらないので、ボーン姿勢と
            // ブレンドシェイプが動いていないかを別途確認する。毎イベント確認すると無駄なので
            // 最短間隔を空け、変化が無いフレームではベイク自体を丸ごと省く。
            if (!needBake && source is SkinnedMeshRenderer skinnedCheck
                && EditorApplication.timeSinceStartup - _lastPoseCheckTime > PoseCheckInterval)
            {
                _lastPoseCheckTime = EditorApplication.timeSinceStartup;
                int hash = ComputePoseHash(skinnedCheck);
                if (!_hasPoseHash || hash != _lastPoseHash)
                {
                    _lastPoseHash = hash;
                    _hasPoseHash = true;
                    needBake = true;
                }
            }

            if (!needBake) return;

            _lastMatrix = matrix;
            _lastPoseCheckTime = EditorApplication.timeSinceStartup;
            if (source is SkinnedMeshRenderer skinned)
            {
                _lastPoseHash = ComputePoseHash(skinned);
                _hasPoseHash = true;
            }
            else
            {
                _hasPoseHash = false;
            }

            _valid = Bake(source, analyzedMesh.vertexCount);
            Generation++;
        }

        public void Invalidate()
        {
            _valid = false;
            _hasPoseHash = false;
            Generation++;
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

        /// <summary>
        /// BakeMesh の結果を左右する要素 (ボーン姿勢とブレンドシェイプの重み) をまとめたハッシュ。
        /// 前回と一致していればスキニング結果も変わっていないとみなす。
        /// </summary>
        private static int ComputePoseHash(SkinnedMeshRenderer skinned)
        {
            unchecked
            {
                int hash = 17;

                var bones = skinned.bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    hash = hash * 31 + (bone != null ? bone.localToWorldMatrix.GetHashCode() : 0);
                }

                var mesh = skinned.sharedMesh;
                int shapeCount = mesh != null ? mesh.blendShapeCount : 0;
                for (int i = 0; i < shapeCount; i++)
                {
                    hash = hash * 31 + skinned.GetBlendShapeWeight(i).GetHashCode();
                }

                return hash;
            }
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

                    _baked.GetVertices(_localPositions);
                    if (_localPositions.Count != expectedVertexCount) return false;

                    var toWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                    for (int i = 0; i < expectedVertexCount; i++)
                    {
                        _worldPositions[i] = toWorld.MultiplyPoint3x4(_localPositions[i]);
                    }
                    return true;
                }
                catch (Exception) { }
            }

            var filter = renderer.GetComponent<MeshFilter>();
            var staticMesh = filter != null ? filter.sharedMesh : (renderer is SkinnedMeshRenderer s ? s.sharedMesh : null);
            if (staticMesh == null || staticMesh.vertexCount != expectedVertexCount) return false;

            try
            {
                // Read/Write が無効なメッシュでは例外になる。オーバーレイを諦めるだけで済ませる。
                staticMesh.GetVertices(_localPositions);
                if (_localPositions.Count != expectedVertexCount) return false;

                var l2w = transform.localToWorldMatrix;
                for (int i = 0; i < expectedVertexCount; i++)
                {
                    _worldPositions[i] = l2w.MultiplyPoint3x4(_localPositions[i]);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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
            if (_valid || _displayRenderer != null) Generation++;
            _valid = false;
            _hasPoseHash = false;
            _displayRenderer = null;
        }

        public void Dispose()
        {
            if (_reloadHookInstalled)
            {
                AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBakedMesh;
                _reloadHookInstalled = false;
            }
            ReleaseBakedMesh();
            Reset();
        }

        private void ReleaseBakedMesh()
        {
            if (_baked == null) return;
            UnityEngine.Object.DestroyImmediate(_baked);
            _baked = null;
            _valid = false;
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
