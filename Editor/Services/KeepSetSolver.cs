using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    public sealed class KeepSetOptions
    {
        public MmComponentPolicy Policy = MmComponentPolicy.KeepAll;
        public bool KeepPhysBones = true;
        public bool KeepConstraints = true;
        public bool RemoveOtherRenderers = true;
    }

    public sealed class KeepSet
    {
        public readonly HashSet<GameObject> Objects = new HashSet<GameObject>();
        public readonly HashSet<Component> Components = new HashSet<Component>();
        public readonly List<Component> PurgedPhysBones = new List<Component>();

        /// <summary>複製範囲の外を指していた参照の数 (Prefab 化すると外れる)。</summary>
        public int ExternalReferenceCount;
    }

    /// <summary>
    /// 減算方式の中核。複製したヒエラルキーから「残すべきオブジェクト / コンポーネント」を決定する。
    /// </summary>
    public static class KeepSetSolver
    {
        public static KeepSet Solve(
            Transform scopeRoot,
            Renderer targetRenderer,
            MeshFilter targetFilter,
            IReadOnlyCollection<Transform> requiredTransforms,
            IReadOnlyCollection<Transform> weightedBones,
            KeepSetOptions options)
        {
            var solver = new Solver(scopeRoot, targetRenderer, targetFilter, weightedBones, options);
            return solver.Run(requiredTransforms);
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
            private readonly Transform _scopeRoot;
            private readonly Renderer _targetRenderer;
            private readonly MeshFilter _targetFilter;
            private readonly HashSet<Transform> _weightedBones;
            private readonly KeepSetOptions _options;

            private readonly KeepSet _result = new KeepSet();
            private readonly HashSet<Component> _dropped = new HashSet<Component>();
            private readonly Queue<Component> _pending = new Queue<Component>();
            private readonly List<Component> _all = new List<Component>();
            private readonly List<Component> _constraints = new List<Component>();

            public Solver(
                Transform scopeRoot, Renderer targetRenderer, MeshFilter targetFilter,
                IReadOnlyCollection<Transform> weightedBones, KeepSetOptions options)
            {
                _scopeRoot = scopeRoot;
                _targetRenderer = targetRenderer;
                _targetFilter = targetFilter;
                _options = options ?? new KeepSetOptions();
                _weightedBones = new HashSet<Transform>();
                if (weightedBones != null)
                {
                    foreach (var bone in weightedBones)
                    {
                        if (bone != null) _weightedBones.Add(bone);
                    }
                }
            }

            public KeepSet Run(IReadOnlyCollection<Transform> requiredTransforms)
            {
                // 1. 全コンポーネントを収集し、方針で除去するものを先に確定させる。
                foreach (var component in _scopeRoot.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component is Transform) continue;
                    _all.Add(component);
                    if (ComponentReflection.IsConstraint(component)) _constraints.Add(component);
                }
                foreach (var component in _all)
                {
                    if (ShouldDrop(component)) _dropped.Add(component);
                }

                // 2. 種を蒔く。
                AddComponent(_targetRenderer);
                AddComponent(_targetFilter);
                if (requiredTransforms != null)
                {
                    foreach (var transform in requiredTransforms)
                    {
                        if (transform != null) AddObject(transform.gameObject);
                    }
                }
                if (_options.Policy == MmComponentPolicy.KeepAll)
                {
                    // 全維持方針では、残るコンポーネントを持つオブジェクトは全て生存させる。
                    foreach (var component in _all) AddComponent(component);
                }

                // 3. 参照の推移閉包を取る。
                while (true)
                {
                    DrainPending();
                    if (!PullConstraints()) break;
                }

                return _result;
            }

            private bool ShouldDrop(Component component)
            {
                if (ReferenceEquals(component, _targetRenderer)) return false;
                if (_targetFilter != null && ReferenceEquals(component, _targetFilter)) return false;

                if (PhysBoneBridge.IsPhysBone(component))
                {
                    if (!_options.KeepPhysBones) return true;
                    if (!PhysBoneBridge.AffectsAny(component, _weightedBones))
                    {
                        // 切り出したメッシュのウェイトに一切影響しない PhysBone は方針によらず除去する。
                        _result.PurgedPhysBones.Add(component);
                        return true;
                    }
                    return false;
                }

                if (ComponentReflection.IsRendererLike(component))
                {
                    return _options.RemoveOtherRenderers
                           || _options.Policy == MmComponentPolicy.MeshDependenciesOnly;
                }

                bool isConstraint = ComponentReflection.IsConstraint(component);
                if (isConstraint && !_options.KeepConstraints) return true;

                if (_options.Policy == MmComponentPolicy.MeshDependenciesOnly)
                {
                    // ホワイトリスト: Constraint と PhysBoneCollider のみ生存候補として残す。
                    // 実際に残るかは参照の推移閉包で決まる。
                    if (isConstraint) return false;
                    if (PhysBoneBridge.IsCollider(component)) return false;
                    return true;
                }

                return false;
            }

            private void DrainPending()
            {
                while (_pending.Count > 0)
                {
                    var component = _pending.Dequeue();
                    foreach (var reference in ComponentReflection.EnumerateObjectReferences(component))
                    {
                        switch (reference)
                        {
                            case Transform transform:
                                VisitObject(transform.gameObject);
                                break;
                            case GameObject go:
                                VisitObject(go);
                                break;
                            case Component other:
                                VisitComponent(other);
                                break;
                        }
                    }
                }
            }

            /// <summary>
            /// Constraint は「駆動先のオブジェクトが残るなら残す」。
            /// 祖先は必ず残るため、駆動先の子孫が残るケースも駆動先自身が残っていることで判定できる。
            /// </summary>
            private bool PullConstraints()
            {
                if (!_options.KeepConstraints) return false;
                if (_options.Policy == MmComponentPolicy.KeepAll) return false; // 既に全て投入済み

                bool added = false;
                foreach (var constraint in _constraints)
                {
                    if (constraint == null) continue;
                    if (_dropped.Contains(constraint)) continue;
                    if (_result.Components.Contains(constraint)) continue;

                    var target = ComponentReflection.GetConstraintTarget(constraint);
                    if (target == null || !_result.Objects.Contains(target.gameObject)) continue;

                    AddComponent(constraint);
                    added = true;
                }
                return added;
            }

            private void VisitObject(GameObject go)
            {
                if (go == null) return;
                if (IsInScope(go.transform)) AddObject(go);
                else if (go.scene.IsValid()) _result.ExternalReferenceCount++;
            }

            private void VisitComponent(Component component)
            {
                if (component == null) return;
                if (IsInScope(component.transform)) AddComponent(component);
                else if (component.gameObject.scene.IsValid()) _result.ExternalReferenceCount++;
            }

            private void AddObject(GameObject go)
            {
                if (go == null) return;
                var transform = go.transform;
                if (!IsInScope(transform)) return;
                if (!_result.Objects.Add(go)) return;

                if (_options.Policy == MmComponentPolicy.KeepAll)
                {
                    foreach (var component in go.GetComponents<Component>()) AddComponent(component);
                }

                if (transform.parent != null) AddObject(transform.parent.gameObject);
            }

            private void AddComponent(Component component)
            {
                if (component == null || component is Transform) return;
                if (!IsInScope(component.transform)) return;
                if (_dropped.Contains(component)) return;
                if (!_result.Components.Add(component)) return;

                AddObject(component.gameObject);
                _pending.Enqueue(component);
            }

            private bool IsInScope(Transform transform)
            {
                return transform != null && transform.IsChildOf(_scopeRoot);
            }
        }
    }
}
