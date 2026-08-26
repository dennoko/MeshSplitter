using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// 複製の粒度 (= 切り出し元メッシュを持つ Prefab) を解決し、その複製を作る。
    /// アバター全体ではなく、メッシュが属する最も内側の Prefab インスタンスを単位とする。
    /// </summary>
    public static class ExtractionScope
    {
        /// <summary>
        /// 複製範囲のルートを求める。
        /// ボーン等が Prefab の外にある場合のみ、それらを含む最小の共通祖先まで範囲を広げる。
        /// </summary>
        public static Transform Resolve(
            Renderer renderer,
            IReadOnlyCollection<Transform> required,
            out string note,
            out string error)
        {
            note = null;
            error = null;

            if (renderer == null)
            {
                error = "切り出し元の Renderer が指定されていません。";
                return null;
            }

            var go = renderer.gameObject;
            if (!go.scene.IsValid())
            {
                error = "シーン上に配置されたオブジェクトを指定してください (Prefab アセットは直接処理できません)。";
                return null;
            }

            Transform prefabRoot = null;
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (nearest != null) prefabRoot = nearest.transform;
            }

            var scope = prefabRoot != null ? prefabRoot : renderer.transform;

            if (required != null)
            {
                foreach (var transform in required)
                {
                    if (transform == null) continue;
                    var common = CommonAncestor(scope, transform);
                    if (common == null)
                    {
                        error = $"ボーン '{transform.name}' が切り出し元と同じ階層にありません。";
                        return null;
                    }
                    scope = common;
                }
            }

            if (prefabRoot == null)
            {
                note = $"'{go.name}' は Prefab インスタンスではないため、'{scope.name}' 以下を複製範囲としました。";
            }
            else if (scope != prefabRoot)
            {
                note = $"ボーンが Prefab '{prefabRoot.name}' の外にあるため、複製範囲を '{scope.name}' まで広げました。";
            }

            return scope;
        }

        /// <summary>
        /// 複製範囲を元の親の下に複製し、元 → 複製 の Transform 対応表を返す。
        /// 同じ親の下に作るのでローカル Transform は元と完全に一致する。
        /// </summary>
        public static GameObject Duplicate(Transform scopeRoot, out Dictionary<Transform, Transform> map)
        {
            var copy = Object.Instantiate(scopeRoot.gameObject, scopeRoot.parent);
            copy.name = scopeRoot.name;

            // Prefab との接続が残っている場合は完全に解除し、単独の Prefab として保存できるようにする。
            if (PrefabUtility.IsPartOfPrefabInstance(copy))
            {
                PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            // Instantiate は子の順序を完全に保つため、インデックスで 1:1 の対応が取れる。
            map = new Dictionary<Transform, Transform>();
            MapRecursive(scopeRoot, copy.transform, map);
            return copy;
        }

        /// <summary>
        /// UI 表示用に、現在の設定で複製されることになる範囲を説明する。
        /// </summary>
        public static string Describe(Renderer renderer)
        {
            if (renderer == null) return string.Empty;

            var required = new List<Transform> { renderer.transform };
            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.bones != null) required.AddRange(skinned.bones);
                if (skinned.rootBone != null) required.Add(skinned.rootBone);
            }

            var scope = Resolve(renderer, required, out string note, out string error);
            if (scope == null) return error;

            string text = $"複製範囲: {scope.name}";
            return note != null ? text + "\n" + note : text;
        }

        private static void MapRecursive(Transform original, Transform copy, Dictionary<Transform, Transform> map)
        {
            map[original] = copy;
            int count = Mathf.Min(original.childCount, copy.childCount);
            for (int i = 0; i < count; i++)
            {
                MapRecursive(original.GetChild(i), copy.GetChild(i), map);
            }
        }

        private static Transform CommonAncestor(Transform a, Transform b)
        {
            if (a == null) return b;
            if (b == null) return a;

            var ancestors = new HashSet<Transform>();
            for (var t = a; t != null; t = t.parent) ancestors.Add(t);
            for (var t = b; t != null; t = t.parent)
            {
                if (ancestors.Contains(t)) return t;
            }
            return null;
        }
    }
}
