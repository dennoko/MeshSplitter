using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// メッシュを解析して <see cref="MeshTopology"/> を作成する純粋処理。
    /// </summary>
    public static class MeshIslandAnalyzer
    {
        private const float UvQuantize = 100000f;
        private const float PosQuantize = 100000f;

        public static MeshTopology Analyze(Mesh mesh, int submeshFilter, out string error)
        {
            error = null;

            if (mesh == null)
            {
                error = MmLocalization.Tr("err_mesh_not_found");
                return null;
            }
            if (!mesh.isReadable)
            {
                error = MmLocalization.Tr("err_mesh_not_readable", mesh.name);
                return null;
            }

            var vertices = mesh.vertices;
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            bool hasUv = uvs.Count == vertices.Length;

            var tris = new List<MeshTopology.Tri>(mesh.triangles.Length / 3);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (submeshFilter >= 0 && sub != submeshFilter) continue;

                int indexStart = (int)mesh.GetIndexStart(sub);
                var subTris = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < subTris.Length; i += 3)
                {
                    int v0 = subTris[i], v1 = subTris[i + 1], v2 = subTris[i + 2];
                    tris.Add(new MeshTopology.Tri
                    {
                        Index = (indexStart + i) / 3,
                        Submesh = sub,
                        V0 = v0, V1 = v1, V2 = v2,
                        U0 = hasUv ? uvs[v0] : Vector2.zero,
                        U1 = hasUv ? uvs[v1] : Vector2.zero,
                        U2 = hasUv ? uvs[v2] : Vector2.zero
                    });
                }
            }

            if (tris.Count == 0)
            {
                error = MmLocalization.Tr("err_no_triangles_in_submesh");
                return null;
            }

            var triangles = tris.ToArray();

            int[] uvIslandOf;
            int uvIslandCount;
            if (hasUv)
            {
                uvIslandOf = GroupByKey(triangles, out uvIslandCount, UvKeys);
            }
            else
            {
                uvIslandOf = GroupByKey(triangles, out uvIslandCount, t => PositionKeys(t, vertices));
            }

            var polyGroupOf = GroupByKey(triangles, out int polyGroupCount, t => PositionKeys(t, vertices));

            return new MeshTopology(mesh, submeshFilter, triangles, hasUv,
                uvIslandOf, uvIslandCount, polyGroupOf, polyGroupCount);
        }

        private static IEnumerable<long> UvKeys(MeshTopology.Tri t)
        {
            yield return UvKey(t.U0);
            yield return UvKey(t.U1);
            yield return UvKey(t.U2);
        }

        private static IEnumerable<long> PositionKeys(MeshTopology.Tri t, Vector3[] vertices)
        {
            yield return PosKey(vertices[t.V0]);
            yield return PosKey(vertices[t.V1]);
            yield return PosKey(vertices[t.V2]);
        }

        private static int[] GroupByKey(
            MeshTopology.Tri[] triangles,
            out int groupCountOut,
            Func<MeshTopology.Tri, IEnumerable<long>> keySelector)
        {
            var uf = new UnionFind(triangles.Length);
            var firstTriOfKey = new Dictionary<long, int>(triangles.Length * 2);

            for (int i = 0; i < triangles.Length; i++)
            {
                foreach (long key in keySelector(triangles[i]))
                {
                    if (firstTriOfKey.TryGetValue(key, out int other)) uf.Union(i, other);
                    else firstTriOfKey[key] = i;
                }
            }

            var idOfRoot = new Dictionary<int, int>();
            var result = new int[triangles.Length];
            int next = 0;
            for (int i = 0; i < triangles.Length; i++)
            {
                int root = uf.Find(i);
                if (!idOfRoot.TryGetValue(root, out int id))
                {
                    id = next++;
                    idOfRoot[root] = id;
                }
                result[i] = id;
            }

            groupCountOut = next;
            return result;
        }

        private static long UvKey(Vector2 uv)
        {
            long x = (long)Mathf.Round(uv.x * UvQuantize);
            long y = (long)Mathf.Round(uv.y * UvQuantize);
            return (x * 73856093L) ^ (y * 19349663L);
        }

        private static long PosKey(Vector3 p)
        {
            long x = (long)Mathf.Round(p.x * PosQuantize);
            long y = (long)Mathf.Round(p.y * PosQuantize);
            long z = (long)Mathf.Round(p.z * PosQuantize);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public UnionFind(int size)
            {
                _parent = new int[size];
                _rank = new int[size];
                for (int i = 0; i < size; i++) _parent[i] = i;
            }

            public int Find(int i)
            {
                while (_parent[i] != i)
                {
                    _parent[i] = _parent[_parent[i]];
                    i = _parent[i];
                }
                return i;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
                _parent[rb] = ra;
                if (_rank[ra] == _rank[rb]) _rank[ra]++;
            }
        }
    }
}
