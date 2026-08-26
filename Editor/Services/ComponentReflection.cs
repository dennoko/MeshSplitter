using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// 型を直接知らなくても SerializedObject 経由でコンポーネントを解析するユーティリティ。
    /// VRChat SDK / Modular Avatar への静的参照を持たずに動作させるために使用する。
    /// </summary>
    public static class ComponentReflection
    {
        private const string VrcConstraintBaseTypeName = "VRC.Dynamics.VRCConstraintBase";
        private const string VrcConstraintSourceField = "SourceTransform";
        private static readonly string[] ConstraintTargetPaths = { "TargetTransform", "targetTransform" };

        public static Transform ReadTransform(Component component, string propertyPath)
        {
            if (component == null) return null;
            var property = new SerializedObject(component).FindProperty(propertyPath);
            return property?.objectReferenceValue as Transform;
        }

        /// <summary>
        /// Unity Constraint (IConstraint) と VRChat Constraint の双方を判定する。
        /// </summary>
        public static bool IsConstraint(Component component)
        {
            if (component == null) return false;
            if (component is IConstraint) return true;

            for (var type = component.GetType(); type != null; type = type.BaseType)
            {
                if (type.FullName == VrcConstraintBaseTypeName) return true;
            }
            return false;
        }

        /// <summary>
        /// Constraint が実際に駆動する Transform。
        /// VRChat Constraint の TargetTransform が未設定の場合は自身の Transform を返す。
        /// </summary>
        public static Transform GetConstraintTarget(Component constraint)
        {
            if (constraint == null) return null;
            foreach (var path in ConstraintTargetPaths)
            {
                var target = ReadTransform(constraint, path);
                if (target != null) return target;
            }
            return constraint.transform;
        }

        /// <summary>
        /// Constraint の source (駆動元) となる Transform を列挙する。
        /// Unity Constraint は型付き API から、VRChat Constraint は Sources 配列を
        /// SerializedObject 経由で読み取る。
        /// </summary>
        public static IEnumerable<Transform> GetConstraintSources(Component constraint)
        {
            if (constraint == null) yield break;

            if (constraint is IConstraint typed)
            {
                for (int i = 0; i < typed.sourceCount; i++)
                {
                    var source = typed.GetSource(i).sourceTransform;
                    if (source != null) yield return source;
                }
                yield break;
            }

            var target = GetConstraintTarget(constraint);
            var fallback = new List<Transform>();
            bool found = false;

            var iterator = new SerializedObject(constraint).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!(iterator.objectReferenceValue is Transform value)) continue;

                if (iterator.propertyPath.EndsWith(VrcConstraintSourceField, StringComparison.Ordinal))
                {
                    found = true;
                    yield return value;
                }
                else if (value != target)
                {
                    fallback.Add(value);
                }
            }

            // Sources 配列が見つからない構造だった場合のみ、Transform 参照全体を source と見なす。
            if (found) yield break;
            foreach (var value in fallback) yield return value;
        }
    }
}
