using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// レストポーズ時の頂点位置とボーン姿勢、BlendShape変形を考慮した
    /// SkinnedMeshRenderer用の正確な localBounds を算出する。
    /// </summary>
    public static class BoundsCalculator
    {
        /// <param name="rendererTransform">
        /// スキニング情報が無いメッシュを扱うために必要。頂点はメッシュローカル空間なので、
        /// rootBone ローカルへ移すには Renderer の localToWorld を挟まなければならない。
        /// </param>
        public static Bounds CalculateSkinnedLocalBounds(
            Mesh mesh, Transform[] bones, Transform rootBone, Transform rendererTransform,
            float padding = 0.05f)
        {
            if (mesh == null) return new Bounds(Vector3.zero, Vector3.one);
            if (rootBone == null) return mesh.bounds;

            var vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0) return mesh.bounds;

            var bindposes = mesh.bindposes;
            var bonesPerVertex = mesh.GetBonesPerVertex();
            var allWeights = mesh.GetAllBoneWeights();

            bool hasSkinning = bones != null && bones.Length > 0 &&
                               bindposes != null && bindposes.Length > 0 &&
                               bonesPerVertex.Length == vertices.Length &&
                               allWeights.Length > 0;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            Matrix4x4 rootWorldToLocal = rootBone.worldToLocalMatrix;

            // スキニングされていない頂点用: メッシュローカル → ワールド → rootBone ローカル。
            // Renderer の localToWorld を挟まずに rootWorldToLocal を直接掛けると空間が混ざり、
            // rootBone が原点から離れているほど localBounds が大きくずれる。
            Matrix4x4 meshToRoot = rendererTransform != null
                ? rootWorldToLocal * rendererTransform.localToWorldMatrix
                : rootWorldToLocal;

            if (hasSkinning)
            {
                var boneMatrices = new Matrix4x4[bones.Length];
                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] != null && b < bindposes.Length)
                    {
                        boneMatrices[b] = rootWorldToLocal * bones[b].localToWorldMatrix * bindposes[b];
                    }
                    else
                    {
                        boneMatrices[b] = Matrix4x4.identity;
                    }
                }

                int weightOffset = 0;
                for (int v = 0; v < vertices.Length; v++)
                {
                    int count = bonesPerVertex[v];
                    Vector3 vPos = vertices[v];
                    Vector3 skinnedPos = Vector3.zero;
                    float totalWeight = 0f;

                    for (int k = 0; k < count; k++)
                    {
                        var bw = allWeights[weightOffset + k];
                        if (bw.boneIndex >= 0 && bw.boneIndex < boneMatrices.Length)
                        {
                            skinnedPos += boneMatrices[bw.boneIndex].MultiplyPoint3x4(vPos) * bw.weight;
                            totalWeight += bw.weight;
                        }
                    }

                    if (totalWeight > 0.0001f)
                    {
                        if (Mathf.Abs(totalWeight - 1f) > 0.001f)
                        {
                            skinnedPos /= totalWeight;
                        }
                    }
                    else
                    {
                        skinnedPos = meshToRoot.MultiplyPoint3x4(vPos);
                    }

                    min = Vector3.Min(min, skinnedPos);
                    max = Vector3.Max(max, skinnedPos);

                    weightOffset += count;
                }

                // BlendShape も考慮
                int blendShapeCount = mesh.blendShapeCount;
                if (blendShapeCount > 0)
                {
                    var deltaVertices = new Vector3[vertices.Length];
                    var deltaNormals = new Vector3[vertices.Length];
                    var deltaTangents = new Vector3[vertices.Length];

                    for (int shape = 0; shape < blendShapeCount; shape++)
                    {
                        int frameCount = mesh.GetBlendShapeFrameCount(shape);
                        for (int frame = 0; frame < frameCount; frame++)
                        {
                            mesh.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);

                            weightOffset = 0;
                            for (int v = 0; v < vertices.Length; v++)
                            {
                                int count = bonesPerVertex[v];
                                Vector3 dv = deltaVertices[v];
                                if (dv.sqrMagnitude < 1e-8f)
                                {
                                    weightOffset += count;
                                    continue;
                                }

                                Vector3 vPos = vertices[v] + dv;
                                Vector3 skinnedPos = Vector3.zero;
                                float totalWeight = 0f;

                                for (int k = 0; k < count; k++)
                                {
                                    var bw = allWeights[weightOffset + k];
                                    if (bw.boneIndex >= 0 && bw.boneIndex < boneMatrices.Length)
                                    {
                                        skinnedPos += boneMatrices[bw.boneIndex].MultiplyPoint3x4(vPos) * bw.weight;
                                        totalWeight += bw.weight;
                                    }
                                }

                                if (totalWeight > 0.0001f)
                                {
                                    if (Mathf.Abs(totalWeight - 1f) > 0.001f)
                                    {
                                        skinnedPos /= totalWeight;
                                    }
                                }
                                else
                                {
                                    skinnedPos = meshToRoot.MultiplyPoint3x4(vPos);
                                }

                                min = Vector3.Min(min, skinnedPos);
                                max = Vector3.Max(max, skinnedPos);

                                weightOffset += count;
                            }
                        }
                    }
                }
            }
            else
            {
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vector3 pt = meshToRoot.MultiplyPoint3x4(vertices[v]);
                    min = Vector3.Min(min, pt);
                    max = Vector3.Max(max, pt);
                }
            }

            if (min.x > max.x) return mesh.bounds;

            Vector3 size = max - min;
            Vector3 pad = Vector3.Max(size * 0.15f, new Vector3(padding, padding, padding));
            min -= pad;
            max += pad;

            var result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }
    }
}
