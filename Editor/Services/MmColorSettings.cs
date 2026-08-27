using System;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// UV プレビューおよびシーンビューのワイヤーフレーム / 選択色を管理する設定クラス。
    /// EditorPrefs に永続化され、変更時は OnColorsChanged イベントを発火する。
    /// </summary>
    public static class MmColorSettings
    {
        private const string PrefKeyUvWire = "MeshSplitter_Color_UvWire";
        private const string PrefKeyUvSelected = "MeshSplitter_Color_UvSelected";
        private const string PrefKeySceneHover = "MeshSplitter_Color_SceneHover";
        private const string PrefKeySceneSelected = "MeshSplitter_Color_SceneSelected";

        public static readonly Color DefaultUvWireColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        public static readonly Color DefaultUvSelectedColor = new Color(0.184f, 0.427f, 0.659f, 1f);
        public static readonly Color DefaultSceneHoverColor = new Color(1f, 0.718f, 0.302f, 1f);
        public static readonly Color DefaultSceneSelectedColor = new Color(0.616f, 0.824f, 1f, 0.902f);

        public static event Action OnColorsChanged;

        private static bool _loaded;
        private static Color _uvWireColor;
        private static Color _uvSelectedColor;
        private static Color _sceneHoverColor;
        private static Color _sceneSelectedColor;

        public static Color UvWireColor
        {
            get
            {
                EnsureLoaded();
                return _uvWireColor;
            }
            set
            {
                EnsureLoaded();
                if (_uvWireColor != value)
                {
                    _uvWireColor = value;
                    SaveColor(PrefKeyUvWire, value);
                    OnColorsChanged?.Invoke();
                }
            }
        }

        public static Color UvSelectedColor
        {
            get
            {
                EnsureLoaded();
                return _uvSelectedColor;
            }
            set
            {
                EnsureLoaded();
                if (_uvSelectedColor != value)
                {
                    _uvSelectedColor = value;
                    SaveColor(PrefKeyUvSelected, value);
                    OnColorsChanged?.Invoke();
                }
            }
        }

        public static Color SceneHoverColor
        {
            get
            {
                EnsureLoaded();
                return _sceneHoverColor;
            }
            set
            {
                EnsureLoaded();
                if (_sceneHoverColor != value)
                {
                    _sceneHoverColor = value;
                    SaveColor(PrefKeySceneHover, value);
                    OnColorsChanged?.Invoke();
                }
            }
        }

        public static Color SceneSelectedColor
        {
            get
            {
                EnsureLoaded();
                return _sceneSelectedColor;
            }
            set
            {
                EnsureLoaded();
                if (_sceneSelectedColor != value)
                {
                    _sceneSelectedColor = value;
                    SaveColor(PrefKeySceneSelected, value);
                    OnColorsChanged?.Invoke();
                }
            }
        }

        public static void ResetToDefaults()
        {
            _uvWireColor = DefaultUvWireColor;
            _uvSelectedColor = DefaultUvSelectedColor;
            _sceneHoverColor = DefaultSceneHoverColor;
            _sceneSelectedColor = DefaultSceneSelectedColor;

            SaveColor(PrefKeyUvWire, _uvWireColor);
            SaveColor(PrefKeyUvSelected, _uvSelectedColor);
            SaveColor(PrefKeySceneHover, _sceneHoverColor);
            SaveColor(PrefKeySceneSelected, _sceneSelectedColor);

            OnColorsChanged?.Invoke();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _uvWireColor = LoadColor(PrefKeyUvWire, DefaultUvWireColor);
            _uvSelectedColor = LoadColor(PrefKeyUvSelected, DefaultUvSelectedColor);
            _sceneHoverColor = LoadColor(PrefKeySceneHover, DefaultSceneHoverColor);
            _sceneSelectedColor = LoadColor(PrefKeySceneSelected, DefaultSceneSelectedColor);
        }

        private static Color LoadColor(string key, Color defaultColor)
        {
            string hex = EditorPrefs.GetString(key, null);
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex, out var c))
            {
                return c;
            }
            return defaultColor;
        }

        private static void SaveColor(string key, Color color)
        {
            EditorPrefs.SetString(key, ColorUtility.ToHtmlStringRGBA(color));
        }
    }
}
