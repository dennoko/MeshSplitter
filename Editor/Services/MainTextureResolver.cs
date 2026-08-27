using System;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// UV プレビューの背景に敷くテクスチャと、UV0 からテクスチャ座標への変換 (_MainTex_ST)。
    /// </summary>
    public readonly struct UvBackground : IEquatable<UvBackground>
    {
        public static readonly UvBackground None = default;

        public readonly Texture2D Texture;
        public readonly Vector2 Scale;
        public readonly Vector2 Offset;

        public UvBackground(Texture2D texture, Vector2 scale, Vector2 offset)
        {
            Texture = texture;
            Scale = scale;
            Offset = offset;
        }

        /// <summary>破棄済みのテクスチャも無効として扱う。</summary>
        public bool IsValid => Texture != null;

        public bool Equals(UvBackground other)
        {
            return ReferenceEquals(Texture, other.Texture) && Scale == other.Scale && Offset == other.Offset;
        }

        public override bool Equals(object obj) => obj is UvBackground other && Equals(other);

        public override int GetHashCode()
        {
            int hash = ReferenceEquals(Texture, null) ? 0 : Texture.GetInstanceID();
            return (hash * 397) ^ (Scale.GetHashCode() * 31 + Offset.GetHashCode());
        }
    }

    /// <summary>
    /// サブメッシュに割り当てられたマテリアルから、UV0 にそのまま対応するメインテクスチャを取り出す。
    ///
    /// lilToon は _MainTex を必ず uv0 から引く (lilCalcUV(input.uv0, _MainTex_ST) = uv0 * ST.xy + ST.zw)。
    /// Poiyomi も式は同じ (poiUV(poiMesh.uv[_MainTexUV], _MainTex_ST)) だが、参照する UV を
    /// _MainTexUV で切り替えられるので、そこだけ確認すればよい。
    /// どちらも Unity 標準の _MainTex / _MainTex_ST の慣習に沿っているため、
    /// ここでは特定シェーダーを名前で判別せず「UV を切り替えるプロパティが UV0 を指しているか」で判定する。
    /// </summary>
    public static class MainTextureResolver
    {
        private const string MainTexProperty = "_MainTex";

        /// <summary>
        /// メインテクスチャの参照 UV をマテリアル側で切り替えられるシェーダーのプロパティ名。
        /// Poiyomi の _MainTexUV は 0=UV0 / 1-3=UV1-UV3 / 4 以上は Panosphere やワールド座標などの
        /// 手続き的な UV なので、0 以外ならメッシュの UV0 とは一致せず、重ねて表示できない。
        /// lilToon には該当プロパティが無く、その場合は UV0 とみなす。
        /// </summary>
        private static readonly string[] UvSelectorProperties = { "_MainTexUV" };

        /// <summary>
        /// 指定サブメッシュのメインテクスチャを解決する。重ねて表示できない場合は <see cref="UvBackground.None"/>。
        /// </summary>
        public static UvBackground Resolve(Renderer renderer, int submeshIndex, int uvChannel)
        {
            // サブメッシュを絞っていない (-1) とマテリアルが一意に決まらない。
            // UV0 以外を見ているときは、メインテクスチャの参照 UV と一致しない。
            if (renderer == null || submeshIndex < 0 || uvChannel != 0) return UvBackground.None;

            var materials = renderer.sharedMaterials;
            if (submeshIndex >= materials.Length) return UvBackground.None;

            var material = materials[submeshIndex];
            if (material == null || !material.HasProperty(MainTexProperty)) return UvBackground.None;
            if (!ReferencesUv0(material)) return UvBackground.None;

            // Cubemap や Texture2DArray はプレビューに敷けないので Texture2D だけを受け付ける
            if (!(material.GetTexture(MainTexProperty) is Texture2D texture)) return UvBackground.None;

            return new UvBackground(
                texture,
                material.GetTextureScale(MainTexProperty),
                material.GetTextureOffset(MainTexProperty));
        }

        private static bool ReferencesUv0(Material material)
        {
            foreach (string property in UvSelectorProperties)
            {
                if (material.HasProperty(property) && !Mathf.Approximately(material.GetFloat(property), 0f))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
