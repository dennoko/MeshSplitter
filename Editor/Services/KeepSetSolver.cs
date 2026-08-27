using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    public sealed class KeepSetOptions
    {
        public bool KeepPhysBones = true;          // 切り出したメッシュに効く PhysBone を維持する
        public bool KeepPhysBoneColliders = true;  // 維持した PhysBone が参照する Collider を維持する
        public bool KeepConstraints = true;        // 維持したボーンを駆動する Constraint を維持する
    }

    public sealed class KeepSet
    {
        public readonly HashSet<GameObject> Objects = new HashSet<GameObject>();
        public readonly HashSet<Component> Components = new HashSet<Component>();

        /// <summary>切り出したメッシュに影響しないため除去された PhysBone。</summary>
        public readonly List<Component> PurgedPhysBones = new List<Component>();

        /// <summary>複製範囲の外を指していた参照の数 (Prefab では外れる)。</summary>
        public int ExternalReferenceCount;
    }

    /// <summary>
    /// 減算方式の中核。複製したヒエラルキーから「残すべきオブジェクト / コンポーネント」を決定する。
    ///
    /// Module Creator (https://github.com/Tliks/ModuleCreator) と同じホワイトリスト方式を採る。
    /// 起点は切り出し対象の Renderer とそのボーンだけで、そこから
    /// PhysBone / PhysBoneCollider / Constraint という「メッシュの見た目に効く」種別のみを辿って足す。
    /// 任意のコンポーネントの参照を推移的に辿ることはしないため、
    /// VRCAvatarDescriptor や Animator のような無関係なコンポーネントが混ざることはない。
    /// </summary>
    public static class KeepSetSolver
    {
        /// <param name="rendererBones">複製後の Renderer が参照するボーン (bones / rootBone)。</param>
        /// <param name="weightedBones">実際にウェイトが載っているボーン。依存追跡の起点になる。</param>
        public static KeepSet Solve(
            Transform scopeRoot,
            Renderer targetRenderer,
            MeshFilter targetFilter,
            IReadOnlyCollection<Transform> rendererBones,
            IReadOnlyCollection<Transform> weightedBones,
            KeepSetOptions options)
        {
            var solver = new Solver(scopeRoot, weightedBones, options);
            return solver.Run(targetRenderer, targetFilter, rendererBones);
        }

        /// <summary>
        /// 残さないオブジェクトを末端から削除する。子孫が残る場合は中間ノードも残す。
        /// </summary>
        public static int DeleteUnkeptObjects(GameObject root, HashSet<GameObject> kept)
        {
            int removed = 0;
            DeleteRecursive(root, kept, ref removed, isRoot: true);
            return removed;
        }

        /// <summary>
        /// 残ったオブジェクト上の、残さないコンポーネントを削除する。
        /// RequireComponent による依存で失敗した場合に備え、進捗がある限り繰り返す。
        /// </summary>
        public static int RemoveUnkeptComponents(GameObject root, HashSet<Component> kept)
        {
            var targets = new List<Component>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var components = transform.GetComponents<Component>();
                // 依存される側が先に消えないよう、後ろのコンポーネントから並べる。
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    var component = components[i];
                    if (component == null || component is Transform) continue;
                    if (kept.Contains(component)) continue;
                    targets.Add(component);
                }
            }

            int removed = 0;
            bool progress = true;
            while (targets.Count > 0 && progress)
            {
                progress = false;
                for (int i = 0; i < targets.Count; i++)
                {
                    var component = targets[i];
                    if (component == null)
                    {
                        targets.RemoveAt(i--);
                        progress = true;
                        continue;
                    }

                    try
                    {
                        UnityEngine.Object.DestroyImmediate(component, true);
                    }
                    catch (Exception)
                    {
                        // 依存関係で消せなかった場合は次の周回に回す。
                    }

                    if (component == null)
                    {
                        removed++;
                        targets.RemoveAt(i--);
                        progress = true;
                    }
                }
            }
            return removed;
        }

        public static int RemoveMissingScripts(GameObject root)
        {
            int removed = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            }
            return removed;
        }

        private static void DeleteRecursive(GameObject go, HashSet<GameObject> kept, ref int removed, bool isRoot)
        {
            var transform = go.transform;
            var children = new List<GameObject>(transform.childCount);
            for (int i = 0; i < transform.childCount; i++) children.Add(transform.GetChild(i).gameObject);
            foreach (var child in children) DeleteRecursive(child, kept, ref removed, false);

            if (isRoot) return;
            if (kept.Contains(go)) return;
            if (transform.childCount > 0) return; // 残る子孫がいるので中間ノードとして残す

            UnityEngine.Object.DestroyImmediate(go, true);
            removed++;
        }

        private sealed class Solver
        {
            /// <summary>Constraint の相互参照が循環した場合に備えた反復上限。</summary>
            private const int ConstraintIterationLimit = 64;

            private readonly Transform _scopeRoot;
            private readonly KeepSetOptions _options;
            private readonly HashSet<Transform> _weightedBones = new HashSet<Transform>();
            private readonly KeepSet _result = new KeepSet();

            public Solver(
                Transform scopeRoot, IReadOnlyCollection<Transform> weightedBones, KeepSetOptions options)
            {
                _scopeRoot = scopeRoot;
                _options = options ?? new KeepSetOptions();
                if (weightedBones != null)
                {
                    foreach (var bone in weightedBones)
                    {
                        if (bone != null && IsInScope(bone)) _weightedBones.Add(bone);
                    }
                }
            }

            public KeepSet Run(
                Renderer targetRenderer, MeshFilter targetFilter, IReadOnlyCollection<Transform> rendererBones)
            {
                // 1. 切り出し対象そのもの。
                KeepComponent(targetRenderer);
                KeepComponent(targetFilter);

                if (targetRenderer != null)
                {
                    KeepReference(targetRenderer.probeAnchor);
                    if (targetRenderer is SkinnedMeshRenderer skinned) KeepReference(skinned.rootBone);
                }

                // 2. Renderer が参照するボーンと、ウェイトの載っているボーン。
                if (rendererBones != null)
                {
                    foreach (var bone in rendererBones) KeepReference(bone);
                }
                foreach (var bone in _weightedBones) KeepObject(bone.gameObject);

                // 3. メッシュの見た目に効く種別だけを辿って足す。
                CollectPhysBones();
                CollectConstraints();

                return _result;
            }

            /// <summary>
            /// 切り出したメッシュのウェイトに影響する PhysBone だけを残す。
            /// 揺れの形が変わらないよう、影響するボーンから先の単一チェーンも維持する。
            /// </summary>
            private void CollectPhysBones()
            {
                foreach (var physBone in PhysBoneBridge.FindPhysBones(_scopeRoot))
                {
                    if (physBone == null) continue;

                    if (!_options.KeepPhysBones)
                    {
                        _result.PurgedPhysBones.Add(physBone);
                        continue;
                    }

                    var chain = new HashSet<Transform>();
                    foreach (var affected in PhysBoneBridge.GetAffectedTransforms(physBone))
                    {
                        if (_weightedBones.Contains(affected)) AddSingleChain(affected, chain);
                    }

                    if (chain.Count == 0)
                    {
                        // ウェイトに一切影響しないので、残しても揺れる先が無い。
                        _result.PurgedPhysBones.Add(physBone);
                        continue;
                    }

                    KeepComponent(physBone);
                    KeepReference(PhysBoneBridge.GetRoot(physBone));
                    foreach (var transform in chain) KeepObject(transform.gameObject);

                    if (!_options.KeepPhysBoneColliders) continue;
                    foreach (var collider in PhysBoneBridge.GetColliders(physBone))
                    {
                        if (collider == null) continue;
                        if (!IsInScope(collider.transform))
                        {
                            _result.ExternalReferenceCount++;
                            continue;
                        }
                        KeepComponent(collider);
                        KeepReference(PhysBoneBridge.GetRoot(collider));
                    }
                }
            }

            /// <summary>
            /// 維持したボーン (またはその子孫) を駆動する Constraint を残す。
            /// Constraint の source 側も維持対象に加わるため、そこから更に別の Constraint が
            /// 有効になることがある。新規に見つからなくなるまで繰り返す。
            /// </summary>
            private void CollectConstraints()
            {
                if (!_options.KeepConstraints) return;

                var constraints = new List<ConstraintInfo>();
                foreach (var component in _scopeRoot.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || !ComponentReflection.IsConstraint(component)) continue;
                    constraints.Add(new ConstraintInfo(_scopeRoot, component));
                }
                if (constraints.Count == 0) return;

                // 駆動されると見た目が変わるボーンの集合。祖先も含める。
                var drivenBones = new HashSet<Transform>();
                foreach (var bone in _weightedBones) AddAncestors(bone, drivenBones);

                for (int iteration = 0; iteration < ConstraintIterationLimit; iteration++)
                {
                    var discovered = new HashSet<Transform>();

                    foreach (var info in constraints)
                    {
                        if (info.Constraint == null || info.Target == null) continue;
                        if (!drivenBones.Contains(info.Target) && !info.TargetDescendants.Overlaps(drivenBones)) continue;

                        KeepComponent(info.Constraint);
                        KeepReference(info.Target);

                        foreach (var source in info.SourceAncestors)
                        {
                            foreach (var transform in source)
                            {
                                KeepObject(transform.gameObject);
                                discovered.Add(transform);
                            }
                        }
                        _result.ExternalReferenceCount += info.ExternalSourceCount;
                        info.ExternalSourceCount = 0;
                    }

                    discovered.ExceptWith(drivenBones);
                    if (discovered.Count == 0) return;
                    drivenBones.UnionWith(discovered);
                }

                Debug.LogWarning(
                    "[Mesh Splitter] Constraint の依存解決が上限に達しました。参照が循環している可能性があります。");
            }

            /// <summary>
            /// PhysBone の揺れ単位である「子が 1 つだけ続く連鎖」を末端まで辿る。
            /// 途中で分岐したらそこで打ち切る (Module Creator と同じ挙動)。
            /// </summary>
            private static void AddSingleChain(Transform transform, HashSet<Transform> result)
            {
                while (transform != null && result.Add(transform) && transform.childCount == 1)
                {
                    transform = transform.GetChild(0);
                }
            }

            private void AddAncestors(Transform transform, HashSet<Transform> result)
            {
                for (var current = transform; current != null; current = current.parent)
                {
                    result.Add(current);
                    if (current == _scopeRoot) return;
                }
            }

            /// <summary>複製範囲の中なら残し、外なら「外れる参照」として数える。</summary>
            private void KeepReference(Transform transform)
            {
                if (transform == null) return;
                if (IsInScope(transform)) KeepObject(transform.gameObject);
                else if (transform.gameObject.scene.IsValid()) _result.ExternalReferenceCount++;
            }

            private void KeepObject(GameObject go)
            {
                if (go == null || !IsInScope(go.transform)) return;
                _result.Objects.Add(go);
            }

            private void KeepComponent(Component component)
            {
                if (component == null || component is Transform) return;
                if (!IsInScope(component.transform)) return;
                _result.Components.Add(component);
                _result.Objects.Add(component.gameObject);
            }

            private bool IsInScope(Transform transform)
            {
                return transform != null && transform.IsChildOf(_scopeRoot);
            }

            /// <summary>Constraint 1 件分の判定に必要な情報を事前計算したもの。</summary>
            private sealed class ConstraintInfo
            {
                public readonly Component Constraint;
                public readonly Transform Target;
                public readonly HashSet<Transform> TargetDescendants = new HashSet<Transform>();
                public readonly List<List<Transform>> SourceAncestors = new List<List<Transform>>();
                public int ExternalSourceCount;

                public ConstraintInfo(Transform scopeRoot, Component constraint)
                {
                    Constraint = constraint;
                    Target = ComponentReflection.GetConstraintTarget(constraint);

                    if (Target != null)
                    {
                        foreach (var child in Target.GetComponentsInChildren<Transform>(true))
                        {
                            TargetDescendants.Add(child);
                        }
                    }

                    foreach (var source in ComponentReflection.GetConstraintSources(constraint))
                    {
                        var chain = AncestorChain(scopeRoot, source);
                        if (chain == null) ExternalSourceCount++;
                        else SourceAncestors.Add(chain);
                    }
                }

                /// <summary>source から複製範囲のルートまでの経路。範囲外なら null。</summary>
                private static List<Transform> AncestorChain(Transform scopeRoot, Transform source)
                {
                    var chain = new List<Transform>();
                    for (var current = source; current != null; current = current.parent)
                    {
                        chain.Add(current);
                        if (current == scopeRoot) return chain;
                    }
                    return null;
                }
            }
        }
    }
}
