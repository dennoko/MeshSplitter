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

        /// <summary>プロジェクトルート（Assets の親）の絶対パス。区切りは "/"、末尾に "/" は付かない。</summary>
        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');

        /// <summary>
        /// ユーザー入力されたフォルダパスを Unity のアセットパス（"Assets/..." または "Packages/..."）に正規化する。
        /// 空文字ならデフォルト、相対パスなら "Assets/" を付与、プロジェクト内の絶対パスならプロジェクト相対に変換する。
        /// プロジェクト外の絶対パスや不正な文字を含むパスはアセットパスにできないので false を返す。
        /// </summary>
        public static bool TryNormalizeAssetFolder(string folder, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(folder))
            {
                normalized = DefaultOutputFolder;
                return true;
            }

            string path = folder.Replace('\\', '/').Trim().TrimEnd('/');
            if (path.Length == 0) return false;
            // Path.IsPathRooted は不正文字で例外を投げるため、先に弾いておく。
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

            // プロジェクト内の絶対パスはプロジェクト相対に畳む。
            string projectRoot = ProjectRoot;
            if (path.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(projectRoot.Length + 1);
            }
            else if (Path.IsPathRooted(path))
            {
                // プロジェクトルート自身や UNC パスもここに落ちる。Assets の外なので出力先にできない。
                return false;
            }

            string[] parts = path.Split('/');
            char[] invalidFileChars = Path.GetInvalidFileNameChars();
            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) return false;
                // ".." を通すと Assets の外へ抜けてしまう。
                if (part == "." || part == "..") return false;
                if (part.IndexOfAny(invalidFileChars) >= 0) return false;
            }

            // 先頭セグメントで書き込み可能なルートを判定し、大文字小文字を Unity の表記へ揃える。
            if (parts[0].Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                parts[0] = "Assets";
            }
            else if (parts[0].Equals("Packages", StringComparison.OrdinalIgnoreCase))
            {
                // "Packages" 単体はパッケージ名が無く、フォルダを作れない。
                if (parts.Length < 2) return false;
                parts[0] = "Packages";
            }
            else
            {
                // ルート指定のない相対パスは Assets からとみなす。
                normalized = "Assets/" + string.Join("/", parts);
                return true;
            }

            normalized = string.Join("/", parts);
            return true;
        }

        /// <summary>
        /// 正規化を試み、できなければ入力をそのまま返す。表示の整形用。
        /// 出力先として使えるかの判定が要る場面では <see cref="TryNormalizeAssetFolder"/> を使うこと。
        /// </summary>
        public static string NormalizeAssetFolder(string folder)
            => TryNormalizeAssetFolder(folder, out string normalized) ? normalized : folder;

        /// <summary>
        /// アセットフォルダを（必要なら親ごと）作成する。作成できた場合と既にある場合に true。
        /// immutable なパッケージ配下など AssetDatabase が作成を拒む場所では false を返す。
        /// </summary>
        public static bool EnsureFolderExists(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath)) return false;
            if (!TryNormalizeAssetFolder(assetFolderPath, out string normalized)) return false;
            if (AssetDatabase.IsValidFolder(normalized)) return true;

            string[] parts = normalized.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    // CreateFolder は失敗すると空の GUID を返す（immutable package など）。
                    if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[i]))) return false;
                }
                current = next;
            }
            return true;
        }

        public static string SubFolder(string parentFolder, string subFolderName)
        {
            string parent = NormalizeAssetFolder(parentFolder);
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
            if (!File.Exists(path) && !AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)) return path;

            for (int i = 1; i < 100000; i++)
            {
                path = $"{folder}/{sanitized} {i}{extension}";
                if (!File.Exists(path) && !AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)) return path;
            }
            return $"{folder}/{sanitized} {Guid.NewGuid().ToString().Substring(0, 8)}{extension}";
        }
    }
}
