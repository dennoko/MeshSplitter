using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// dennokoworks UI の標準フォント（OS のメイリオ）を SDF FontAsset として生成・保持する。
    /// </summary>
    [InitializeOnLoad]
    internal static class DennokoUIFont
    {
        private const string FamilyName = "Meiryo";
        private const string StyleName = "Regular";
        private const string AssetName = "Dennoko_UIFont_Meiryo";
        private const double TickIntervalSec = 2.0;

        private const string WarmupAscii =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~";

        private const string WarmupJapanese =
            "適用保存解除追加削除設定選択中対象有効無効表示非切替更新確認取消閉開始了" +
            "はいいえ完了失敗警告情報エラー成功準備実行処理読込書出" +
            "パーツモジュールメッシュサブメッシュアイランドポリゴン色合わせ反転ベイク同期コライダーアバタープリセット三角形元操作未配置階層最適化維持プロキシ" +
            "つながった透過枠指定";

        private static FontAsset _font;
        private static bool _unavailable;
        private static readonly List<VisualElement> _roots = new List<VisualElement>();
        private static bool _tickHooked;
        private static double _nextTick;

        static DennokoUIFont()
        {
            AssemblyReloadEvents.afterAssemblyReload += Revalidate;
            EditorApplication.playModeStateChanged += _ => Revalidate();
            EditorApplication.projectChanged += Revalidate;
        }

        public static void Apply(VisualElement root)
        {
            if (root == null) return;

            if (!_roots.Contains(root))
            {
                _roots.Add(root);
                root.RegisterCallback<AttachToPanelEvent>(_ => ApplyTo(root));
                root.RegisterCallback<DetachFromPanelEvent>(_ => _roots.Remove(root));
            }

            HookTick(true);
            ApplyTo(root);
        }

        private static void ApplyTo(VisualElement root)
        {
            var font = Get();
            root.style.unityFontDefinition = font != null
                ? new StyleFontDefinition(FontDefinition.FromSDFFont(font))
                : new StyleFontDefinition(StyleKeyword.Null);
        }

        private static FontAsset Get()
        {
            if (IsAlive(_font))
            {
                Protect(_font);
                return _font;
            }

            if (_unavailable) return null;

            _font = FindExisting() ?? Create();
            if (_font == null)
            {
                _unavailable = true;
                return null;
            }
            return _font;
        }

        private static FontAsset FindExisting()
        {
            foreach (var fa in Resources.FindObjectsOfTypeAll<FontAsset>())
            {
                if (fa == null || fa.name != AssetName || !IsAlive(fa)) continue;
                Protect(fa);
                return fa;
            }
            return null;
        }

        private static FontAsset Create()
        {
            try
            {
                var fa = FontAsset.CreateFontAsset(FamilyName, StyleName);
                if (fa == null) return null;

                fa.name = AssetName;
                Protect(fa);
                PreWarm(fa);
                Protect(fa);
                return fa;
            }
            catch
            {
                return null;
            }
        }

        private static void PreWarm(FontAsset fa)
        {
            try { fa.TryAddCharacters(WarmupAscii + WarmupJapanese, out _); }
            catch { }
        }

        private static void Protect(FontAsset fa)
        {
            if (fa == null) return;
            fa.hideFlags = HideFlags.HideAndDontSave;
            if (fa.material != null) fa.material.hideFlags = HideFlags.HideAndDontSave;

            var atlasTextures = fa.atlasTextures;
            if (atlasTextures == null) return;
            foreach (var tex in atlasTextures)
            {
                if (tex != null) tex.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private static bool IsAlive(FontAsset fa)
        {
            if (fa == null) return false;
            if (fa.material == null) return false;
            var atlasTextures = fa.atlasTextures;
            return atlasTextures != null && atlasTextures.Length > 0 && atlasTextures[0] != null;
        }

        private static void Revalidate()
        {
            if (IsAlive(_font))
            {
                Protect(_font);
                return;
            }

            _font = null;
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                var root = _roots[i];
                if (root == null || root.panel == null) { _roots.RemoveAt(i); continue; }
                ApplyTo(root);
            }
        }

        private static void HookTick(bool on)
        {
            if (on == _tickHooked) return;
            if (on) EditorApplication.update += Tick;
            else EditorApplication.update -= Tick;
            _tickHooked = on;
        }

        private static void Tick()
        {
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                var root = _roots[i];
                if (root == null || root.panel == null) _roots.RemoveAt(i);
            }
            if (_roots.Count == 0) { HookTick(false); return; }

            if (EditorApplication.timeSinceStartup < _nextTick) return;
            _nextTick = EditorApplication.timeSinceStartup + TickIntervalSec;

            Revalidate();
        }
    }
}
