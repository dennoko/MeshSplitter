namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// 切り出し先 Prefab にどのコンポーネントを残すかの方針。
    /// いずれの方針でも、切り出したメッシュに影響しなくなった PhysBone は除去される。
    /// </summary>
    public enum MmComponentPolicy
    {
        /// <summary>
        /// 全コンポーネント維持。
        /// 切り出し対象以外の Renderer と不要な PhysBone 以外は元のまま残す。
        /// </summary>
        KeepAll = 0,

        /// <summary>
        /// メッシュ依存のみ (Module Creator 相当)。
        /// Renderer / MeshFilter / PhysBone / PhysBoneCollider / Constraint だけを残し、
        /// それ以外のコンポーネントは全て除去する。
        /// </summary>
        MeshDependenciesOnly = 1
    }
}
