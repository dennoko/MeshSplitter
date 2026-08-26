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
        private static readonly string[] ConstraintTargetPaths = { "TargetTransform", "targetTransform" };

        public static Transform ReadTransform(Component component, string propertyPath)
        {
            if (component == null) return null;
            var property = new SerializedObject(component).FindProperty(propertyPath);
            return property?.objectReferenceValue as Transform;
        }

        public static bool IsRendererLike(Component component)
        {
            return component is Renderer || component is MeshFilter;
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
        /// コンポーネントが保持する全てのオブジェクト参照を列挙する。
        /// 非表示フィールドも対象にするため NextVisible ではなく Next を使う。
        /// </summary>
        public static IEnumerable<UnityEngine.Object> EnumerateObjectReferences(Component component)
        {
            if (component == null) yield break;

            var iterator = new SerializedObject(component).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (iterator.propertyPath == "m_Script") continue;
                if (iterator.propertyPath == "m_GameObject") continue;

                var value = iterator.objectReferenceValue;
                if (value != null) yield return value;
            }
        }
    }
}
