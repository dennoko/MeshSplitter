using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// Mesh Splitter のローカライズ管理クラス。
    /// Localization.json を読み込み、日本語 (ja) と英語 (en) の切り替えを提供する。
    /// </summary>
    public static class MmLocalization
    {
        private const string PrefsKey = "MeshSplitter_Language";
        private const string DefaultLanguage = "ja";

        public static event Action OnLanguageChanged;

        private static string _currentLang = null;
        private static LocalizationRoot _data = null;
        private static readonly Dictionary<string, Dictionary<string, string>> _dictCache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        [Serializable]
        public class LanguagePack
        {
            public string header_title;
            public string ver_checking;
            public string ver_update;
            public string ver_error;
            public string ver_reload_tooltip;
            public string lang_button_text;
            public string lang_button_tooltip;

            public string section_target_mesh;
            public string btn_pick_source;
            public string tooltip_pick_source;
            public string tooltip_reload_source;
            public string label_submesh;
            public string tooltip_uv_channel;
            public string submesh_choice_all;
            public string source_info_empty;
            public string source_info_analyzing;
            public string source_info_format;
            public string source_info_no_uv;
            public string error_no_mesh;

            public string section_selection;
            public string btn_pick_uv;
            public string btn_pick_poly;
            public string btn_select_all;
            public string btn_select_none;
            public string btn_select_invert;
            public string btn_scene_select_on;
            public string btn_scene_select_off;
            public string toggle_scene_xray;
            public string btn_color_options;
            public string header_color_options;
            public string btn_color_back;
            public string header_uv_colors;
            public string label_uv_wire_color;
            public string label_uv_selected_color;
            public string header_scene_colors;
            public string label_scene_hover_color;
            public string label_scene_selected_color;
            public string btn_color_reset;
            public string btn_color_close;
            public string selection_info_unanalyzed;
            public string selection_info_unselected;
            public string selection_info_format;
            public string unit_island;
            public string unit_group;

            public string section_prefab_output;
            public string label_part_name;
            public string label_output_folder;
            public string btn_extract_part;
            public string btn_extract_submesh;

            // Core / Services が返すエラー・注記 (ダイアログとステータス欄に表示される)
            public string err_mesh_not_found;
            public string err_mesh_not_readable;
            public string err_no_triangles_in_submesh;
            public string err_no_source_renderer;
            public string err_source_mesh_not_found;
            public string err_no_selection;
            public string err_selection_not_in_mesh;
            public string err_not_scene_object;
            public string err_bone_outside_hierarchy;
            public string err_request_null;
            public string err_renderer_not_mapped;
            public string err_prefab_save_failed;
            public string err_unknown;
            public string note_scope_not_prefab;
            public string note_scope_expanded;
            public string note_probe_anchor_external;
            public string note_missing_scripts;
            public string note_external_references;
            public string scope_describe_format;

            public string dialog_error_title;
            public string dialog_no_triangles;
            public string dialog_extract_failed;
            public string dialog_complete_title;
            public string extract_success_format;
            public string submesh_batch_success_format;
            public string submesh_batch_progress_format;
        }

        [Serializable]
        private class LocalizationRoot
        {
            public LanguagePack ja;
            public LanguagePack en;
        }

        public static string CurrentLanguage
        {
            get
            {
                if (_currentLang == null)
                {
                    _currentLang = EditorPrefs.GetString(PrefsKey, DefaultLanguage);
                    if (_currentLang != "ja" && _currentLang != "en") _currentLang = DefaultLanguage;
                }
                return _currentLang;
            }
            set
            {
                string next = (value == "en") ? "en" : "ja";
                if (_currentLang != next)
                {
                    _currentLang = next;
                    EditorPrefs.SetString(PrefsKey, next);
                    OnLanguageChanged?.Invoke();
                }
            }
        }

        public static bool IsJapanese => CurrentLanguage == "ja";

        public static void ToggleLanguage()
        {
            CurrentLanguage = (CurrentLanguage == "ja") ? "en" : "ja";
        }

        public static string Tr(string key, params object[] args)
        {
            EnsureLoaded();
            string lang = CurrentLanguage;
            if (!_dictCache.TryGetValue(lang, out var dict) || !dict.TryGetValue(key, out string text))
            {
                // フォールバック: 日本語辞書で試す
                if (_dictCache.TryGetValue(DefaultLanguage, out var jaDict) && jaDict.TryGetValue(key, out string jaText))
                {
                    text = jaText;
                }
                else
                {
                    text = key;
                }
            }

            if (args != null && args.Length > 0)
            {
                try { return string.Format(text, args); }
                catch { return text; }
            }
            return text;
        }

        private static void EnsureLoaded()
        {
            if (_dictCache.Count > 0) return;

            string json = LoadJsonContent();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    _data = JsonUtility.FromJson<LocalizationRoot>(json);
                    if (_data != null)
                    {
                        if (_data.ja != null) _dictCache["ja"] = PackToDict(_data.ja);
                        if (_data.en != null) _dictCache["en"] = PackToDict(_data.en);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MmLocalization] JSONパース失敗: {e.Message}");
                }
            }

            // 万が一JSONが空または読み込み失敗時の安全策
            if (!_dictCache.ContainsKey("ja")) _dictCache["ja"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!_dictCache.ContainsKey("en")) _dictCache["en"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> PackToDict(LanguagePack pack)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fields = typeof(LanguagePack).GetFields();
            foreach (var f in fields)
            {
                if (f.FieldType == typeof(string))
                {
                    var val = (string)f.GetValue(pack);
                    if (val != null) dict[f.Name] = val;
                }
            }
            return dict;
        }

        private static string LoadJsonContent()
        {
            // 1) スクリプト位置起点の探索
            string path = ResolveJsonByScriptPath();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            // 2) AssetDatabase 探索 (本ツールのフォルダ配下に限定する。
            //    他の dennokoworks ツールも Localization.json を同梱しており、
            //    それを掴むと全てのラベルが翻訳キーのまま表示されてしまう)
            string assetPath = MmAssets.FindAssetPath("Localization.json");
            if (!string.IsNullOrEmpty(assetPath) && File.Exists(assetPath))
            {
                return File.ReadAllText(assetPath);
            }

            return null;
        }

        private static string ResolveJsonByScriptPath([CallerFilePath] string scriptPath = null)
        {
            if (string.IsNullOrEmpty(scriptPath)) return null;
            var dir = Path.GetDirectoryName(scriptPath);
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "UI", "Localization.json");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir, "Localization.json");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
