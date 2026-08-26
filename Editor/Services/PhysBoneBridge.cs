using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// VRCPhysBone / DynamicBone を型参照なしで安全に検出・移植・パージするブリッジ。
    /// </summary>
    public static class PhysBoneBridge
    {
        private static readonly string[] PhysBoneTypeNames =
        {
            "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone",
            "DynamicBone"
        };

        private static readonly string[] ColliderTypeNames =
        {
            "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider",
            "DynamicBoneCollider"
        };

        private static readonly string[] RootPaths = { "rootTransform", "m_Root" };
        private static readonly string[] IgnorePaths = { "ignoreTransforms", "m_Exclusions" };

        public static bool IsAvailable => MmTypeCache.FindAny(PhysBoneTypeNames) != null;

        public static bool IsPhysBone(Component component)
        {
            if (component == null) return false;
            foreach (var name in PhysBoneTypeNames)
            {
                var type = MmTypeCache.Find(name);
                if (type != null && type.IsInstanceOfType(component)) return true;
            }
            return false;
        }

        public static bool IsCollider(Component component)
        {
            if (component == null) return false;
            foreach (var name in ColliderTypeNames)
            {
                var type = MmTypeCache.Find(name);
                if (type != null && type.IsInstanceOfType(component)) return true;
            }
            return false;
        }

        public static List<Component> FindPhysBones(Transform root, bool includeInactive = true)
        {
            var result = new List<Component>();
            if (root == null) return result;

            foreach (var component in root.GetComponentsInChildren<Component>(includeInactive))
            {
                if (IsPhysBone(component)) result.Add(component);
            }
            return result;
        }

        public static List<Component> FindColliders(Transform root, bool includeInactive = true)
        {
            var result = new List<Component>();
            if (root == null) return result;

            foreach (var component in root.GetComponentsInChildren<Component>(includeInactive))
            {
                if (IsCollider(component)) result.Add(component);
            }
            return result;
        }

        public static Transform GetRoot(Component physBone)
        {
            if (physBone == null) return null;
            foreach (var path in RootPaths)
            {
                var t = ComponentReflection.ReadTransform(physBone, path);
                if (t != null) return t;
            }
            return physBone.transform;
        }

        public static HashSet<Transform> GetAffectedTransforms(Component physBone)
        {
            var affected = new HashSet<Transform>();
            var root = GetRoot(physBone);
            if (root == null) return affected;

            var ignored = new HashSet<Transform>();
            foreach (var path in IgnorePaths)
            {
                foreach (var t in ReadTransformArray(physBone, path)) ignored.Add(t);
            }

            void Walk(Transform t)
            {
                if (t == null || ignored.Contains(t)) return;
                affected.Add(t);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }

            Walk(root);
            return affected;
        }

        public static bool AffectsAny(Component physBone, HashSet<Transform> keptBones)
        {
            if (physBone == null || keptBones == null || keptBones.Count == 0) return false;

            foreach (var t in GetAffectedTransforms(physBone))
            {
                if (keptBones.Contains(t)) return true;
            }
            return false;
        }

        private static IEnumerable<Transform> ReadTransformArray(Component component, string propertyPath)
        {
            var so = new UnityEditor.SerializedObject(component);
            var array = so.FindProperty(propertyPath);
            if (array == null || !array.isArray) yield break;

            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue is Transform t) yield return t;
            }
        }
    }
}
