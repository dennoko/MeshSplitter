using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// パス操作・ファイル名サニタイズ・フォルダ作成の純粋ユーティリティ。
    /// </summary>
    public static class MmPaths
    {
        public const string DefaultOutputFolder = "Assets/MS_splitted_mesh";
        public const string MeshesSubFolder = "Meshes";
        public const string PrefabsSubFolder = "Prefabs";

        public static void EnsureFolderExists(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath)) return;
            if (AssetDatabase.IsValidFolder(assetFolderPath)) return;

            string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = normalized.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        public static string SubFolder(string parentFolder, string subFolderName)
        {
            string parent = string.IsNullOrEmpty(parentFolder) ? DefaultOutputFolder : parentFolder.Replace('\\', '/').TrimEnd('/');
            string path = $"{parent}/{subFolderName}";
            EnsureFolderExists(path);
            return path;
        }

        public static string SanitizeFileName(string rawName, string fallback = "Part")
        {
            if (string.IsNullOrWhiteSpace(rawName)) return fallback;
            string invalid = new string(Path.GetInvalidFileNameChars()) + "/\\:*?\"<>|";
            string pattern = $"[{Regex.Escape(invalid)}]";
            string sanitized = Regex.Replace(rawName, pattern, "_").Trim();
            return string.IsNullOrEmpty(sanitized) ? fallback : sanitized;
        }

        public static string UniqueAssetPath(string folder, string baseName, string extension)
        {
            EnsureFolderExists(folder);
            string sanitized = SanitizeFileName(baseName);
            if (!extension.StartsWith(".")) extension = "." + extension;

            string path = $"{folder}/{sanitized}{extension}";
            if (!File.Exists(path)) return path;

            for (int i = 1; i < 10000; i++)
            {
                path = $"{folder}/{sanitized}_{i}{extension}";
                if (!File.Exists(path)) return path;
            }
            return $"{folder}/{sanitized}_{Guid.NewGuid().ToString().Substring(0, 8)}{extension}";
        }
    }
}
