using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    public enum MmPickMode
    {
        UvIsland,
        ConnectedPolygon
    }

    public enum MmSelectionMode
    {
        Add,
        Remove
    }

    public sealed class MeshTopology
    {
        public struct Tri
        {
            public int Index;
            public int Submesh;
            public int V0, V1, V2;
            public Vector2 U0, U1, U2;
        }

        public Mesh Mesh { get; }
        public int SubmeshFilter { get; }
        public Tri[] Triangles { get; }
        public bool HasUv { get; }

        private readonly int[] _uvIslandOf;
        private readonly int[] _polyGroupOf;
        private readonly Rect[] _uvIslandBounds;
        private readonly Rect[] _polyGroupBounds;

        public int UvIslandCount => _uvIslandBounds.Length;
        public int PolyGroupCount => _polyGroupBounds.Length;

        internal MeshTopology(
            Mesh mesh, int submeshFilter, Tri[] triangles, bool hasUv,
            int[] uvIslandOf, int uvIslandCount,
            int[] polyGroupOf, int polyGroupCount)
        {
            Mesh = mesh;
            SubmeshFilter = submeshFilter;
            Triangles = triangles;
            HasUv = hasUv;
            _uvIslandOf = uvIslandOf;
            _polyGroupOf = polyGroupOf;
            _uvIslandBounds = ComputeBounds(triangles, uvIslandOf, uvIslandCount);
            _polyGroupBounds = ComputeBounds(triangles, polyGroupOf, polyGroupCount);
        }

        public int[] GroupOf(MmPickMode mode)
        {
            return mode == MmPickMode.UvIsland ? _uvIslandOf : _polyGroupOf;
        }

        public int GroupCount(MmPickMode mode)
        {
            return mode == MmPickMode.UvIsland ? UvIslandCount : PolyGroupCount;
        }

        public Rect[] GroupUvBounds(MmPickMode mode)
        {
            return mode == MmPickMode.UvIsland ? _uvIslandBounds : _polyGroupBounds;
        }

        public HashSet<int> ResolveTriangles(MmPickMode mode, IReadOnlyCollection<int> groups)
        {
            var result = new HashSet<int>();
            if (groups == null || groups.Count == 0) return result;

            var wanted = groups as HashSet<int> ?? new HashSet<int>(groups);
            var groupOf = GroupOf(mode);
            for (int i = 0; i < Triangles.Length; i++)
            {
                if (wanted.Contains(groupOf[i])) result.Add(Triangles[i].Index);
            }
            return result;
        }

        public int PickGroupAtUv(MmPickMode mode, Vector2 uv)
        {
            var groupOf = GroupOf(mode);
            for (int i = 0; i < Triangles.Length; i++)
            {
                var t = Triangles[i];
                if (PointInTriangle(uv, t.U0, t.U1, t.U2)) return groupOf[i];
            }
            return -1;
        }

        public List<int> PickGroupsInRect(MmPickMode mode, Rect rect)
        {
            var groupOf = GroupOf(mode);
            var hit = new HashSet<int>();
            for (int i = 0; i < Triangles.Length; i++)
            {
                var t = Triangles[i];
                var center = (t.U0 + t.U1 + t.U2) / 3f;
                if (rect.Contains(center)) hit.Add(groupOf[i]);
            }
            var list = new List<int>(hit);
            list.Sort();
            return list;
        }

        public int CountTriangles(MmPickMode mode, IReadOnlyCollection<int> groups)
        {
            if (groups == null || groups.Count == 0) return 0;
            var wanted = groups as HashSet<int> ?? new HashSet<int>(groups);
            var groupOf = GroupOf(mode);
            int count = 0;
            for (int i = 0; i < Triangles.Length; i++)
            {
                if (wanted.Contains(groupOf[i])) count++;
            }
            return count;
        }

        private static Rect[] ComputeBounds(Tri[] triangles, int[] groupOf, int groupCount)
        {
            var min = new Vector2[groupCount];
            var max = new Vector2[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                min[g] = new Vector2(float.MaxValue, float.MaxValue);
                max[g] = new Vector2(float.MinValue, float.MinValue);
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                int g = groupOf[i];
                if (g < 0 || g >= groupCount) continue;
                var t = triangles[i];
                Expand(ref min[g], ref max[g], t.U0);
                Expand(ref min[g], ref max[g], t.U1);
                Expand(ref min[g], ref max[g], t.U2);
            }

            var rects = new Rect[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                if (min[g].x > max[g].x) { rects[g] = new Rect(); continue; }
                rects[g] = new Rect(min[g], max[g] - min[g]);
            }
            return rects;
        }

        private static void Expand(ref Vector2 min, ref Vector2 max, Vector2 p)
        {
            min.x = Mathf.Min(min.x, p.x);
            min.y = Mathf.Min(min.y, p.y);
            max.x = Mathf.Max(max.x, p.x);
            max.y = Mathf.Max(max.y, p.y);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }
    }
}
