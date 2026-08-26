using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    public sealed class ModularizeRequest
    {
        public Renderer SourceRenderer;
        public IReadOnlyCollection<int> TriangleIndices;
        public string PartName;
        public string OutputFolder;

        // コンポーネントの維持方針
        public MmComponentPolicy ComponentPolicy = MmComponentPolicy.KeepAll;
        public bool RemoveOtherRenderers = true;   // 切り出し対象以外の Renderer / MeshFilter を除去
        public bool KeepPhysBones = true;          // false にすると PhysBone を全て除去
        public bool KeepConstraints = true;        // Constraint を維持するか

        // メッシュ側の設定
        public bool KeepBlendShapes = true;
        public bool TrimUnusedBones = false;       // ウェイトの無いボーンを bones 配列と階層から除去
        public bool RecalculateBounds = false;     // false なら元の localBounds をそのまま維持

        // 出力
        public bool AddBoneProxy = false;
        public HumanBodyBones BoneProxyTarget = HumanBodyBones.Head;
        public bool AutoInstantiate = true;
    }

    public sealed class ModularizeResult
    {
        public string PartName;
        public string PrefabPath;
        public string MeshPath;
        public GameObject PrefabInstance;
        public string ScopeRootName;
        public int TriangleCount;
        public int VertexCount;
        public int BoneCount;
        public int PurgedPhysBoneCount;
        public int RemovedObjectCount;
        public int RemovedComponentCount;
        public List<string> Notes = new List<string>();
        public string Error;
        public bool Ok => Error == null && !string.IsNullOrEmpty(PrefabPath);
    }

    /// <summary>
    /// 減算方式のメッシュ切り分けサービス。
    /// 切り出し元メッシュを持つ Prefab を複製し、切り出したメッシュを差し込んだ上で
    /// 不要になったオブジェクト / コンポーネントを削り落として Prefab として保存する。
    /// 元のオブジェクトやメッシュアセットには一切変更を加えない。
    /// </summary>
    public static class MeshModularizerService
    {
        public static ModularizeResult Execute(ModularizeRequest request)
        {
            if (request == null) return Fail("リクエストが null です。");
            if (request.SourceRenderer == null) return Fail("切り出し元の Renderer が指定されていません。");

            string partName = MmPaths.SanitizeFileName(request.PartName, "Part");
            string outputFolder = string.IsNullOrEmpty(request.OutputFolder)
                ? MmPaths.DefaultOutputFolder
                : request.OutputFolder;

            // 1. メッシュの切り出し (元アセットには触れない)
            var split = MeshSplitter.Split(
                request.SourceRenderer,
                request.TriangleIndices,
                request.KeepBlendShapes,
                request.TrimUnusedBones,
                out string splitError);

            if (split == null) return Fail(splitError);

            var notes = new List<string>();
            bool meshOwned = true;
            GameObject copy = null;

            try
            {
                // 2. 複製範囲 (切り出し元メッシュを持つ Prefab) の解決
                var required = new List<Transform> { request.SourceRenderer.transform };
                if (split.Bones != null) required.AddRange(split.Bones);
                if (split.RootBone != null) required.Add(split.RootBone);

                var scopeRoot = ExtractionScope.Resolve(
                    request.SourceRenderer, required, out string scopeNote, out string scopeError);
                if (scopeRoot == null) return Fail(scopeError);
                if (!string.IsNullOrEmpty(scopeNote)) notes.Add(scopeNote);

                // 3. 複製と対応表の構築
                copy = ExtractionScope.Duplicate(scopeRoot, out var map);
                copy.name = partName;

                var targetRenderer = MapComponent(request.SourceRenderer, map);
                if (targetRenderer == null) return Fail("複製後の Renderer を特定できませんでした。");

                // 4. 切り出したメッシュを差し込む
                var targetFilter = ApplySplitMesh(targetRenderer, split, map, request);

                if (targetRenderer.probeAnchor != null && !targetRenderer.probeAnchor.IsChildOf(copy.transform))
                {
                    notes.Add("Probe Anchor が複製範囲の外を指しているため、Prefab では参照が外れます。");
                }

                // 5. 残すオブジェクト / コンポーネントの決定
                var keepOptions = new KeepSetOptions
                {
                    Policy = request.ComponentPolicy,
                    KeepPhysBones = request.KeepPhysBones,
                    KeepConstraints = request.KeepConstraints,
                    RemoveOtherRenderers = request.RemoveOtherRenderers
                };

                var keep = KeepSetSolver.Solve(
                    copy.transform,
                    targetRenderer,
                    targetFilter,
                    MapTransforms(required, map),
                    ResolveWeightedBones(split, targetRenderer, map),
                    keepOptions);

                // 6. 減算
                int removedObjects = KeepSetSolver.DeleteUnkeptObjects(copy, keep.Objects);
                int removedComponents = KeepSetSolver.RemoveUnkeptComponents(copy, keep.Components);
                int missingScripts = KeepSetSolver.RemoveMissingScripts(copy);
                if (missingScripts > 0) notes.Add($"Missing Script を {missingScripts} 件除去しました。");
                if (keep.ExternalReferenceCount > 0)
                {
                    notes.Add($"複製範囲の外を指す参照が {keep.ExternalReferenceCount} 件あります (Prefab では外れます)。");
                }

                // 7. MA Bone Proxy の付与 (オプショナル)
                if (request.AddBoneProxy && ModularAvatarBridge.IsAvailable)
                {
                    ModularAvatarBridge.AddBoneProxy(copy, request.BoneProxyTarget, recordUndo: false);
                }

                // 8. メッシュアセットの保存
                string meshSubFolder = MmPaths.SubFolder(outputFolder, MmPaths.MeshesSubFolder);
                string meshPath = MmPaths.UniqueAssetPath(meshSubFolder, partName + "_Mesh", ".asset");
                AssetDatabase.CreateAsset(split.Mesh, meshPath);
                meshOwned = false;

                // 9. Prefab の保存
                string prefabPath = MmPaths.UniqueAssetPath(outputFolder, partName, ".prefab");
                var prefab = PrefabUtility.SaveAsPrefabAsset(copy, prefabPath);
                if (prefab == null)
                {
                    // 孤立したメッシュアセットを残さない。
                    AssetDatabase.DeleteAsset(meshPath);
                    return Fail($"Prefab の保存に失敗しました: {prefabPath}");
                }

                AssetDatabase.SaveAssets();

                // 10. シーンへの自動インスタンス化 (オプショナル)
                GameObject instance = null;
                if (request.AutoInstantiate)
                {
                    instance = (scopeRoot.parent != null
                        ? PrefabUtility.InstantiatePrefab(prefab, scopeRoot.parent)
                        : PrefabUtility.InstantiatePrefab(prefab)) as GameObject;
                    if (instance != null)
                    {
                        Undo.RegisterCreatedObjectUndo(instance, "Instantiate Modularized Part");
                        Selection.activeGameObject = instance;
                    }
                }

                return new ModularizeResult
                {
                    PartName = partName,
                    PrefabPath = prefabPath,
                    MeshPath = meshPath,
                    PrefabInstance = instance,
                    ScopeRootName = scopeRoot.name,
                    TriangleCount = split.TriangleCount,
                    VertexCount = split.VertexCount,
                    BoneCount = split.Bones != null ? split.Bones.Length : 0,
                    PurgedPhysBoneCount = keep.PurgedPhysBones.Count,
                    RemovedObjectCount = removedObjects,
                    RemovedComponentCount = removedComponents,
                    Notes = notes
                };
            }
            finally
            {
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                if (meshOwned && split.Mesh != null) UnityEngine.Object.DestroyImmediate(split.Mesh);
            }
        }

        /// <summary>
        /// 複製した Renderer に切り出し済みメッシュを差し込む。
        /// Renderer 自体は複製なので、bones / rootBone 以外の設定は元のまま維持される。
        /// </summary>
        private static MeshFilter ApplySplitMesh(
            Renderer target, MeshSplitResult split, Dictionary<Transform, Transform> map, ModularizeRequest request)
        {
            if (target is SkinnedMeshRenderer skinned)
            {
                skinned.sharedMesh = split.Mesh;
                skinned.sharedMaterials = split.Materials;

                if (split.Bones != null && split.Bones.Length > 0)
                {
                    var bones = new Transform[split.Bones.Length];
                    for (int i = 0; i < bones.Length; i++) bones[i] = MapTransform(split.Bones[i], map);
                    skinned.bones = bones;
                }

                var rootBone = MapTransform(split.RootBone, map);
                if (rootBone != null) skinned.rootBone = rootBone;

                if (request.RecalculateBounds)
                {
                    skinned.localBounds = BoundsCalculator.CalculateSkinnedLocalBounds(
                        split.Mesh, skinned.bones, skinned.rootBone);
                }
                return null;
            }

            var filter = target.GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = split.Mesh;
            target.sharedMaterials = split.Materials;
            return filter;
        }

        /// <summary>
        /// PhysBone のパージ判定に使う「実際にウェイトが載っているボーン」を複製側で求める。
        /// </summary>
        private static List<Transform> ResolveWeightedBones(
            MeshSplitResult split, Renderer target, Dictionary<Transform, Transform> map)
        {
            var result = new List<Transform>();
            if (split.WeightedBones != null)
            {
                foreach (var bone in split.WeightedBones)
                {
                    var mapped = MapTransform(bone, map);
                    if (mapped != null) result.Add(mapped);
                }
            }

            // スキニングされていない場合は Renderer 自身の位置をウェイト扱いにする。
            if (result.Count == 0) result.Add(target.transform);
            return result;
        }

        private static List<Transform> MapTransforms(
            IEnumerable<Transform> originals, Dictionary<Transform, Transform> map)
        {
            var result = new List<Transform>();
            foreach (var original in originals)
            {
                var mapped = MapTransform(original, map);
                if (mapped != null) result.Add(mapped);
            }
            return result;
        }

        private static Transform MapTransform(Transform original, Dictionary<Transform, Transform> map)
        {
            if (original == null) return null;
            return map.TryGetValue(original, out var copy) ? copy : null;
        }

        /// <summary>
        /// 元のコンポーネントに対応する複製側のコンポーネントを、同じ型の中での並び順で特定する。
        /// </summary>
        private static T MapComponent<T>(T source, Dictionary<Transform, Transform> map) where T : Component
        {
            if (source == null) return null;
            if (!map.TryGetValue(source.transform, out var host)) return null;

            var type = source.GetType();
            var originals = source.GetComponents(type);
            var copies = host.GetComponents(type);

            int index = Array.IndexOf(originals, (Component)source);
            if (index >= 0 && index < copies.Length) return copies[index] as T;
            return copies.Length > 0 ? copies[0] as T : null;
        }

        private static ModularizeResult Fail(string error)
        {
            return new ModularizeResult { Error = error ?? "不明なエラーが発生しました。" };
        }
    }
}
