using System;
using System.IO;
using UnityEditor;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// 本ツールに同梱されたアセットを、自分のフォルダ配下に限定して解決する。
    ///
    /// dennokoworks の他ツールも DennokoTheme.uss や Localization.json といった
    /// 同名ファイルを同梱するため、プロジェクト全体をファイル名で検索すると
    /// 他ツールのアセットを掴んでしまう。掴んだ先が別バージョンのテーマや
    /// 別ツールの辞書だと、スタイル崩れや「翻訳キーがそのまま表示される」状態になる。
    /// </summary>
    public static class MmAssets
    {
        /// <summary>ツールのルート特定に使う asmdef ファイル名 (プロジェクト内で一意)。</summary>
        private const string AsmdefFileName = "dennokoworks.MeshSplitter.Editor.asmdef";

        private static string _rootFolder;
        private static bool _rootResolved;

        /// <summary>
        /// ツールのルートフォルダ (例: "Assets/dennokoworks/MeshSplitter")。
        /// 特定できなければ null。
        /// </summary>
        public static string RootFolder
        {
            get
            {
                if (_rootResolved) return _rootFolder;
                _rootResolved = true;
                _rootFolder = ResolveRootFolder();
                return _rootFolder;
            }
        }

        /// <summary>同梱アセットのパスを返す。見つからなければ null。</summary>
        public static string FindAssetPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);

            // 1) 自分のフォルダ配下だけを検索する
            string root = RootFolder;
            if (!string.IsNullOrEmpty(root) && AssetDatabase.IsValidFolder(root))
            {
                string path = Search(nameOnly, fileName, new[] { root });
                if (path != null) return path;
            }

            // 2) ルートを特定できなかった場合のみ、プロジェクト全体へフォールバックする
            return Search(nameOnly, fileName, null);
        }

        /// <summary>同梱アセットをロードする。見つからなければ null。</summary>
        public static T Find<T>(string fileName) where T : UnityEngine.Object
        {
            string path = FindAssetPath(fileName);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string Search(string nameOnly, string fileName, string[] folders)
        {
            var guids = folders != null
                ? AssetDatabase.FindAssets(nameOnly, folders)
                : AssetDatabase.FindAssets(nameOnly);

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            return null;
        }

        /// <summary>
        /// asmdef の位置からツールのルートを求める。asmdef 名はプロジェクト内で一意なので、
        /// フォルダごと移動・リネームされていても正しく追従できる。
        /// </summary>
        private static string ResolveRootFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) ||
                    !path.EndsWith(AsmdefFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // <root>/Editor/xxx.asmdef → <root>
                string editorFolder = Path.GetDirectoryName(path);
                string root = string.IsNullOrEmpty(editorFolder) ? null : Path.GetDirectoryName(editorFolder);
                return string.IsNullOrEmpty(root) ? null : root.Replace('\\', '/');
            }
            return null;
        }
    }
}
