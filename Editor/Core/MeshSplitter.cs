using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    public sealed class MeshSplitResult
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Transform[] Bones;
        public Transform RootBone;
        public Transform[] WeightedBones;
        public int TriangleCount;
        public int VertexCount;
    }

    /// <summary>
    /// 指定された三角形インデックスのみを含む新しい Mesh を生成する。
    /// SkinnedMeshRenderer (スキンメッシュ) および MeshRenderer (静的メッシュ) の両方に対応。
    /// </summary>
    public static class MeshSplitter
    {
        private const float WeightEpsilon = 0.0001f;

        public static MeshSplitResult Split(
            Renderer sourceRenderer,
            IReadOnlyCollection<int> triangleIndices,
            bool keepBlendShapes,
            bool optimizeBones,
            out string error)
        {
            error = null;

            if (sourceRenderer == null)
            {
                error = MmLocalization.Tr("err_no_source_renderer");
                return null;
            }

            var skinned = sourceRenderer as SkinnedMeshRenderer;
            var filter = sourceRenderer.GetComponent<MeshFilter>();
            var mesh = skinned != null ? skinned.sharedMesh : (filter != null ? filter.sharedMesh : null);

            if (mesh == null)
            {
                error = MmLocalization.Tr("err_source_mesh_not_found");
                return null;
            }
            if (!mesh.isReadable)
            {
                error = MmLocalization.Tr("err_mesh_not_readable", mesh.name);
                return null;
            }
            if (triangleIndices == null || triangleIndices.Count == 0)
            {
                error = MmLocalization.Tr("err_no_selection");
                return null;
            }

            var wanted = triangleIndices as HashSet<int> ?? new HashSet<int>(triangleIndices);
            var allTris = mesh.triangles;

            // 1. 残す頂点の特定
            var used = new bool[mesh.vertexCount];
            int keptTriangles = 0;
            for (int t = 0; t < allTris.Length / 3; t++)
            {
                if (!wanted.Contains(t)) continue;
                used[allTris[t * 3 + 0]] = true;
                used[allTris[t * 3 + 1]] = true;
                used[allTris[t * 3 + 2]] = true;
                keptTriangles++;
            }
            if (keptTriangles == 0)
            {
                error = MmLocalization.Tr("err_selection_not_in_mesh");
                return null;
            }

            var newIndexOf = new int[mesh.vertexCount];
            var oldOfNew = new List<int>(mesh.vertexCount);
            for (int v = 0; v < mesh.vertexCount; v++)
            {
                if (used[v])
                {
                    newIndexOf[v] = oldOfNew.Count;
                    oldOfNew.Add(v);
                }
                else
                {
                    newIndexOf[v] = -1;
                }
            }

            var result = new Mesh { name = mesh.name + "_Part" };
            if (oldOfNew.Count > 65535) result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // 2. 頂点属性コピー
            CopyVertexAttributes(mesh, result, oldOfNew);

            // 3. サブメッシュとマテリアルの構築
            var keptSubmeshes = new List<int>();
            var submeshTriangles = new List<int[]>();
            var sourceMaterials = sourceRenderer.sharedMaterials;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int start = (int)mesh.GetIndexStart(sub) / 3;
                int count = (int)mesh.GetIndexCount(sub) / 3;
                var indices = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int t = start + i;
                    if (!wanted.Contains(t)) continue;
                    indices.Add(newIndexOf[allTris[t * 3 + 0]]);
                    indices.Add(newIndexOf[allTris[t * 3 + 1]]);
                    indices.Add(newIndexOf[allTris[t * 3 + 2]]);
                }
                if (indices.Count == 0) continue;
                keptSubmeshes.Add(sub);
                submeshTriangles.Add(indices.ToArray());
            }

            result.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                result.SetTriangles(submeshTriangles[i], i, calculateBounds: false);
            }

            var materials = new Material[keptSubmeshes.Count];
            for (int i = 0; i < keptSubmeshes.Count; i++)
            {
                int sub = keptSubmeshes[i];
                materials[i] = sub < sourceMaterials.Length ? sourceMaterials[sub] : null;
            }

            // 4. スキニングウェイト (SkinnedMeshRenderer の場合)
            Transform[] keptBones = Array.Empty<Transform>();
            Transform[] weightedBones = Array.Empty<Transform>();
            Transform rootBone = null;

            if (skinned != null && skinned.bones != null && skinned.bones.Length > 0)
            {
                keptBones = CopyBonesAndWeights(mesh, result, skinned, oldOfNew, optimizeBones, out weightedBones);
                rootBone = ResolveRootBone(skinned, keptBones);
            }

            // 5. BlendShape
            if (keepBlendShapes)
            {
                CopyBlendShapes(mesh, result, oldOfNew);
            }

            result.RecalculateBounds();

            return new MeshSplitResult
            {
                Mesh = result,
                Materials = materials,
                Bones = keptBones,
                WeightedBones = weightedBones,
                RootBone = rootBone,
                TriangleCount = keptTriangles,
                VertexCount = oldOfNew.Count
            };
        }

        private static void CopyVertexAttributes(Mesh src, Mesh dst, List<int> oldOfNew)
        {
            int n = oldOfNew.Count;

            var srcVerts = src.vertices;
            var verts = new Vector3[n];
            for (int i = 0; i < n; i++) verts[i] = srcVerts[oldOfNew[i]];
            dst.vertices = verts;

            var srcNormals = src.normals;
            if (srcNormals != null && srcNormals.Length == src.vertexCount)
            {
                var normals = new Vector3[n];
                for (int i = 0; i < n; i++) normals[i] = srcNormals[oldOfNew[i]];
                dst.normals = normals;
            }

            var srcTangents = src.tangents;
            if (srcTangents != null && srcTangents.Length == src.vertexCount)
            {
                var tangents = new Vector4[n];
                for (int i = 0; i < n; i++) tangents[i] = srcTangents[oldOfNew[i]];
                dst.tangents = tangents;
            }

            var srcColors = src.colors;
            if (srcColors != null && srcColors.Length == src.vertexCount)
            {
                var colors = new Color[n];
                for (int i = 0; i < n; i++) colors[i] = srcColors[oldOfNew[i]];
                dst.colors = colors;
            }

            for (int channel = 0; channel < 8; channel++)
            {
                var attribute = UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel;
                if (!src.HasVertexAttribute(attribute)) continue;

                switch (src.GetVertexAttributeDimension(attribute))
                {
                    case 2:
                    {
                        var buffer = new List<Vector2>();
                        src.GetUVs(channel, buffer);
                        if (buffer.Count != src.vertexCount) break;
                        var uvs = new List<Vector2>(n);
                        for (int i = 0; i < n; i++) uvs.Add(buffer[oldOfNew[i]]);
                        dst.SetUVs(channel, uvs);
                        break;
                    }
                    case 3:
                    {
                        var buffer = new List<Vector3>();
                        src.GetUVs(channel, buffer);
                        if (buffer.Count != src.vertexCount) break;
                        var uvs = new List<Vector3>(n);
                        for (int i = 0; i < n; i++) uvs.Add(buffer[oldOfNew[i]]);
                        dst.SetUVs(channel, uvs);
                        break;
                    }
                    default:
                    {
                        var buffer = new List<Vector4>();
                        src.GetUVs(channel, buffer);
                        if (buffer.Count != src.vertexCount) break;
                        var uvs = new List<Vector4>(n);
                        for (int i = 0; i < n; i++) uvs.Add(buffer[oldOfNew[i]]);
                        dst.SetUVs(channel, uvs);
                        break;
                    }
                }
            }
        }

        private static Transform[] CopyBonesAndWeights(
            Mesh src, Mesh dst, SkinnedMeshRenderer source, List<int> oldOfNew, bool optimizeBones,
            out Transform[] weightedBones)
        {
            weightedBones = Array.Empty<Transform>();

            var sourceBones = source.bones;
            var bindposes = src.bindposes;

            if (sourceBones == null || sourceBones.Length == 0 || bindposes == null || bindposes.Length == 0)
            {
                return Array.Empty<Transform>();
            }

            var bonesPerVertex = src.GetBonesPerVertex();
            var allWeights = src.GetAllBoneWeights();
            if (bonesPerVertex.Length != src.vertexCount || allWeights.Length == 0)
            {
                return Array.Empty<Transform>();
            }

            var offsets = new int[src.vertexCount];
            int running = 0;
            for (int v = 0; v < src.vertexCount; v++)
            {
                offsets[v] = running;
                running += bonesPerVertex[v];
            }

            // 実際にウェイトが載っているボーンを集計する。
            // PhysBone のパージ判定と未使用ボーンの除去は、どちらもこの集合を基準にする。
            var boneUsed = new bool[sourceBones.Length];
            foreach (int oldV in oldOfNew)
            {
                int count = bonesPerVertex[oldV];
                for (int k = 0; k < count; k++)
                {
                    var bw = allWeights[offsets[oldV] + k];
                    if (bw.weight > WeightEpsilon && bw.boneIndex >= 0 && bw.boneIndex < boneUsed.Length)
                    {
                        boneUsed[bw.boneIndex] = true;
                    }
                }
            }

            var weighted = new List<Transform>();
            for (int b = 0; b < sourceBones.Length; b++)
            {
                if (boneUsed[b] && sourceBones[b] != null) weighted.Add(sourceBones[b]);
            }
            weightedBones = weighted.ToArray();

            if (!optimizeBones)
            {
                // 未使用ボーンを残す場合: 元のボーン配列・bindposes をそのまま維持する
                dst.bindposes = bindposes;

                var newPerVertex = new List<byte>(oldOfNew.Count);
                var newWeights = new List<BoneWeight1>(oldOfNew.Count * 4);

                foreach (int oldV in oldOfNew)
                {
                    int count = bonesPerVertex[oldV];
                    byte written = 0;
                    float total = 0f;
                    int weightStart = newWeights.Count;

                    for (int k = 0; k < count; k++)
                    {
                        var bw = allWeights[offsets[oldV] + k];
                        if (bw.weight <= WeightEpsilon) continue;
                        if (bw.boneIndex >= 0 && bw.boneIndex < sourceBones.Length)
                        {
                            newWeights.Add(bw);
                            total += bw.weight;
                            written++;
                        }
                    }

                    if (written == 0)
                    {
                        newWeights.Add(new BoneWeight1 { boneIndex = 0, weight = 1f });
                        written = 1;
                    }
                    else if (total > 0f && !Mathf.Approximately(total, 1f))
                    {
                        for (int k = 0; k < written; k++)
                        {
                            var w = newWeights[weightStart + k];
                            w.weight /= total;
                            newWeights[weightStart + k] = w;
                        }
                    }

                    newPerVertex.Add(written);
                }

                var perVertexArray = new NativeArray<byte>(newPerVertex.ToArray(), Allocator.Temp);
                var weightArray = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
                try
                {
                    dst.SetBoneWeights(perVertexArray, weightArray);
                }
                finally
                {
                    perVertexArray.Dispose();
                    weightArray.Dispose();
                }

                return (Transform[])sourceBones.Clone();
            }

            // 未使用ボーンを除去する場合: ウェイトのあるボーンだけを詰める
            var newBoneIndexOf = new int[sourceBones.Length];
            var keptBones = new List<Transform>();
            var keptBindposes = new List<Matrix4x4>();
            for (int b = 0; b < sourceBones.Length; b++)
            {
                if (boneUsed[b] && sourceBones[b] != null)
                {
                    newBoneIndexOf[b] = keptBones.Count;
                    keptBones.Add(sourceBones[b]);
                    keptBindposes.Add(b < bindposes.Length ? bindposes[b] : Matrix4x4.identity);
                }
                else
                {
                    newBoneIndexOf[b] = -1;
                }
            }

            if (keptBones.Count == 0) return Array.Empty<Transform>();

            var optPerVertex = new List<byte>(oldOfNew.Count);
            var optWeights = new List<BoneWeight1>(oldOfNew.Count * 4);
            foreach (int oldV in oldOfNew)
            {
                int count = bonesPerVertex[oldV];
                byte written = 0;
                float total = 0f;
                int weightStart = optWeights.Count;

                for (int k = 0; k < count; k++)
                {
                    var bw = allWeights[offsets[oldV] + k];
                    if (bw.weight <= WeightEpsilon) continue;
                    int newBone = bw.boneIndex >= 0 && bw.boneIndex < newBoneIndexOf.Length
                        ? newBoneIndexOf[bw.boneIndex]
                        : -1;
                    if (newBone < 0) continue;

                    optWeights.Add(new BoneWeight1 { boneIndex = newBone, weight = bw.weight });
                    total += bw.weight;
                    written++;
                }

                if (written == 0)
                {
                    optWeights.Add(new BoneWeight1 { boneIndex = 0, weight = 1f });
                    written = 1;
                }
                else if (total > 0f && !Mathf.Approximately(total, 1f))
                {
                    for (int k = 0; k < written; k++)
                    {
                        var w = optWeights[weightStart + k];
                        w.weight /= total;
                        optWeights[weightStart + k] = w;
                    }
                }

                optPerVertex.Add(written);
            }

            dst.bindposes = keptBindposes.ToArray();

            var optPerVertexArray = new NativeArray<byte>(optPerVertex.ToArray(), Allocator.Temp);
            var optWeightArray = new NativeArray<BoneWeight1>(optWeights.ToArray(), Allocator.Temp);
            try
            {
                dst.SetBoneWeights(optPerVertexArray, optWeightArray);
            }
            finally
            {
                optPerVertexArray.Dispose();
                optWeightArray.Dispose();
            }

            return keptBones.ToArray();
        }

        private static void CopyBlendShapes(Mesh src, Mesh dst, List<int> oldOfNew)
        {
            int n = oldOfNew.Count;
            var dv = new Vector3[src.vertexCount];
            var dn = new Vector3[src.vertexCount];
            var dt = new Vector3[src.vertexCount];

            for (int shape = 0; shape < src.blendShapeCount; shape++)
            {
                string name = src.GetBlendShapeName(shape);
                int frameCount = src.GetBlendShapeFrameCount(shape);

                var frames = new List<BlendShapeFrame>(frameCount);
                bool anyNonZero = false;

                for (int frame = 0; frame < frameCount; frame++)
                {
                    src.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);

                    var fv = new Vector3[n];
                    var fn = new Vector3[n];
                    var ft = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        int old = oldOfNew[i];
                        fv[i] = dv[old];
                        fn[i] = dn[old];
                        ft[i] = dt[old];
                        if (!anyNonZero && fv[i].sqrMagnitude > 1e-12f) anyNonZero = true;
                    }

                    frames.Add(new BlendShapeFrame
                    {
                        Weight = src.GetBlendShapeFrameWeight(shape, frame),
                        DeltaVertices = fv,
                        DeltaNormals = fn,
                        DeltaTangents = ft
                    });
                }

                if (!anyNonZero) continue;

                foreach (var f in frames)
                {
                    dst.AddBlendShapeFrame(name, f.Weight, f.DeltaVertices, f.DeltaNormals, f.DeltaTangents);
                }
            }
        }

        private struct BlendShapeFrame
        {
            public float Weight;
            public Vector3[] DeltaVertices;
            public Vector3[] DeltaNormals;
            public Vector3[] DeltaTangents;
        }

        private static Transform ResolveRootBone(SkinnedMeshRenderer source, Transform[] keptBones)
        {
            if (keptBones == null || keptBones.Length == 0) return null;
            if (source.rootBone != null && Array.IndexOf(keptBones, source.rootBone) >= 0) return source.rootBone;

            Transform best = keptBones[0];
            int bestDepth = Depth(best);
            for (int i = 1; i < keptBones.Length; i++)
            {
                if (keptBones[i] == null) continue;
                int d = Depth(keptBones[i]);
                if (d < bestDepth) { best = keptBones[i]; bestDepth = d; }
            }
            return best;
        }

        private static int Depth(Transform t)
        {
            int d = 0;
            while (t != null) { d++; t = t.parent; }
            return d;
        }
    }
}
