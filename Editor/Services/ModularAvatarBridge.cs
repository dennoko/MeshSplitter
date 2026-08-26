using System;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// Modular Avatar との連携ブリッジ。
    /// Modular Avatar がインストールされていなくても安全に動作する。
    /// </summary>
    public static class ModularAvatarBridge
    {
        private const string BoneProxyTypeName = "nadena.dev.modular_avatar.core.ModularAvatarBoneProxy";
        private const string AttachmentModeTypeName = "nadena.dev.modular_avatar.core.BoneProxyAttachmentMode";
        private const string AttachmentModeAsChildAtRoot = "AsChildAtRoot";

        public static bool IsAvailable => MmTypeCache.Find(BoneProxyTypeName) != null;

        public static bool AddBoneProxy(
            GameObject target, HumanBodyBones bone = HumanBodyBones.Head, bool recordUndo = true)
        {
            var type = MmTypeCache.Find(BoneProxyTypeName);
            if (type == null || target == null) return false;

            var component = target.GetComponent(type);
            if (component == null)
            {
                component = recordUndo ? Undo.AddComponent(target, type) : target.AddComponent(type);
                if (component == null) return false;
            }

            var so = new SerializedObject(component);
            var boneRefProp = so.FindProperty("boneReference");
            if (boneRefProp != null) boneRefProp.intValue = (int)bone;

            var subPath = so.FindProperty("subPath");
            if (subPath != null) subPath.stringValue = string.Empty;

            SetAttachmentMode(so);
            so.ApplyModifiedProperties();
            return true;
        }

        private static void SetAttachmentMode(SerializedObject so)
        {
            var property = so.FindProperty("attachmentMode");
            if (property == null) return;

            var modeType = MmTypeCache.Find(AttachmentModeTypeName);
            if (modeType != null && modeType.IsEnum)
            {
                try
                {
                    property.intValue = (int)Enum.Parse(modeType, AttachmentModeAsChildAtRoot);
                    return;
                }
                catch (Exception) { }
            }

            int index = Array.IndexOf(property.enumNames, AttachmentModeAsChildAtRoot);
            if (index >= 0) property.enumValueIndex = index;
        }
    }
}
