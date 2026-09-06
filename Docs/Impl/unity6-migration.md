# MeshSplitter — Unity 6 移行調査

- 調査日: 2026-09-06
- 現行: Unity 2022.3.22f1 / Built-in RP
- 目標: Unity 6 (6000.0 LTS) / **BiRP 維持**
- 共通調査: [`../../../Docs/Impl/unity6-migration-overview.md`](../../../Docs/Impl/unity6-migration-overview.md)

## 判定

✅ **対応済** — Unity 6 非対応の API は **0 件**。修正すべきコードはない。
VRChat SDK への依存もすべてリフレクション経由のため、**SDK の Unity 6 対応を待たずに検証できる**。

## 構成

| 項目 | 内容 |
|---|---|
| 規模 | C# 29 ファイル / 約 6,759 行 |
| asmdef | `dennokoworks.MeshSplitter.Editor`（Editor 専用、**外部参照なし**） |
| エントリ | `MenuItem("dennokoworks/Mesh Splitter")` |
| UI | **UI Toolkit**（`.uxml` 1 / `.uss` 2）+ SceneView オーバーレイ |
| 外部依存 | **VRChat SDK**（**リフレクション経由のみ**。asmdef 参照はゼロ） |

**asmdef が外部を一切参照していない**ため、VRChat SDK / NDMF なしでもコンパイルが通る。
移行観点では非常に有利な構成。

## 検出事項

### 1. VRChat SDK 型のリフレクション解決（✅ 影響なし・良い実装）

`Editor/Services/PhysBoneBridge.cs:8,14,20`

```csharp
/// VRCPhysBone / DynamicBone を型参照なしで安全に検出・移植・パージするブリッジ。
"VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone",
"VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider",
```

`Editor/Services/ComponentReflection.cs:15`

```csharp
private const string VrcConstraintBaseTypeName = "VRC.Dynamics.VRCConstraintBase";
```

- **完全修飾型名の文字列＋リフレクション**で解決。SDK の型を一切直接参照していない。
- Unity 6 で SDK のアセンブリ構成が変わっても、**型名が維持される限り動作する**。
- `VRCConstraintBase` にも対応しており、VRChat Constraints にも追従済み。

**修正不要。** SDK 非対応期間でもコンパイルできる、移行観点で堅牢な実装。

### 2. `NativeArray` によるボーンウェイト操作（✅ 影響なし）

`Editor/Core/MeshSplitter.cs:3,341-342,419-420`

```csharp
using Unity.Collections;
var perVertexArray    = new NativeArray<byte>(newPerVertex.ToArray(), Allocator.Temp);
var weightArray       = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
var optPerVertexArray = new NativeArray<byte>(optPerVertex.ToArray(), Allocator.Temp);
var optWeightArray    = new NativeArray<BoneWeight1>(optWeights.ToArray(), Allocator.Temp);
```

- `NativeArray<T>` + `BoneWeight1` + `Mesh.SetBoneWeights` の API 形状は Unity 6 で **不変**。
- Unity 6 は `com.unity.collections` 2.x を使うが、`Allocator.Temp` の意味論は変わらない。
- `Allocator.Temp` は 1 フレーム有効。Editor 拡張の同期処理内で完結しているため問題ない。
- ワークスペース内で最も `NativeArray` を使う箇所（4 箇所）。

**修正不要。** ただし Unity 6 で `NativeArray` のリーク検出が厳格になっている可能性があるため、
分割処理の実行後に Console に leak 警告が出ていないか確認する。

### 3. コンポーネント保持ロジック（✅ 影響なし）

`Editor/Services/KeepSetSolver.cs:34`

```csharp
/// VRCAvatarDescriptor や Animator のような無関係なコンポーネントが混ざることはない。
```

`Editor/Services/MeshModularizerService.cs:57`

```csharp
/// だけが残り、VRCAvatarDescriptor や Animator などのアバター全体向けコンポーネントは残らない。
```

- コメント内の型名参照のみで、**コード上は型に直接依存していない**。
- `GetComponent` 系と `ComponentReflection` による型名判定で処理している。
- **`Object.FindObjectsOfType` は使っていない**ため、共通調査 3.1 節の Obsolete 置換対象に該当しない。

**修正不要。**

### 4. UI Toolkit（🔍 視覚検証のみ）

`Editor/UI/MeshModularizerWindow.cs`
UXML: `Editor/UI/MeshModularizerWindow.uxml`
USS: `Editor/UI/DennokoTheme.uss`, `Editor/UI/MeshModularizerStyles.uss`

- Unity 6 で Obsolete 化する `ExecuteDefaultAction` / `ExecuteDefaultActionAtTarget` /
  `PreventDefault` は **未使用**。
- `UxmlFactory` / `UxmlTraits` も **未使用**（カスタム要素は C# で直接構築）。
- **コード修正不要。**

`Editor/UI/UvPreviewElement.cs` は UV を描画するカスタム `VisualElement`。
`generateVisualContent` によるメッシュ描画の API は Unity 6 で変更なし。

**対応**: Unity 6 の既定 USS 変更による見た目のずれのみ確認する。

### 5. SceneView オーバーレイ（🔍 軽微な視覚検証）

`Editor/UI/SceneSelectionOverlay.cs:54,59`

```csharp
SceneView.duringSceneGui += OnSceneGui;
SceneView.duringSceneGui -= OnSceneGui;
```

- 登録／解除が対称。`SceneView.duringSceneGui` は Unity 6 で変更なし。
- Unity 6 は SceneView のオーバーレイ UI が刷新されているため、
  **表示位置が既存のオーバーレイと重なる可能性がある**。

**対応**: 目視確認し、干渉していれば座標オフセットのみ調整する。

### 6. フォント生成（`UnityEngine.TextCore.Text.FontAsset`）（🔍 実機確認）

`Editor/UI/DennokoUIFont.cs:88`

```csharp
foreach (var fa in Resources.FindObjectsOfTypeAll<FontAsset>())
```

- `UnityEngine.TextCore.Text` はビルトインモジュールであり、Unity 6 の
  TextMeshPro パッケージ統合の**影響を受けない**（共通調査 3.5 節）。
- `Resources.FindObjectsOfTypeAll<T>()` は **Obsolete ではない**。
  `Object.FindObjectsOfType` と混同して置換しないこと。**修正不要。**

**対応**: Unity 6 で日本語が正しく表示されるか実機確認する。

### 7. バージョンチェッカー（✅ 影響なし）

`Editor/Services/MeshModularizerVersion.cs:167` —
`Resources.FindObjectsOfTypeAll<MeshModularizerWindow>()`（Obsolete 対象外）

`Editor/Services/DennokoVersionChecker.cs:133` — `#if UNITY_2020_2_OR_NEWER`（Unity 6 でも true）

**いずれも修正不要。**

## 非該当の確認

| 確認項目 | 結果 |
|---|---|
| `Object.FindObjectsOfType` / `FindObjectOfType` | **なし**（`Resources.FindObjectsOfTypeAll` のみ = Obsolete 対象外） |
| UnityEditor（Unity 本体）内部 API へのリフレクション | **なし**（VRC SDK 型名のみ） |
| NDMF 内部 API へのリフレクション | **なし** |
| `GraphicsFormat.DepthAuto` / `ShadowAuto` / `VideoAuto` | **なし** |
| UI Toolkit の Obsolete API | **なし** |
| IMGUI テーマ（共有 `EditorStyles` 書き換え） | **なし**（UI Toolkit 主体） |
| `RenderTexture` / `Graphics.Blit` / Compute シェーダ | **なし** |
| カスタムシェーダ | **なし** |
| `Lightmapping` / 物理 API | **なし** |

## 移行手順

### フェーズ 1（Unity 2022.3.22f1 のまま実施可）

- [ ] `Editor/UI/DennokoUIFont.cs:88` と `Editor/Services/MeshModularizerVersion.cs:167` の
      `Resources.FindObjectsOfTypeAll` を **置換しない**ことを確認（誤置換の防止）
- [ ] `MenuItem("Tools/Your Tool Name")` というテンプレート由来の未整理メニュー項目の確認

### フェーズ 2（Unity 6 検証プロジェクト・先行実施可）

**asmdef が外部を一切参照していないため、SDK の Unity 6 対応を待たずに
検証を開始できる。** Unity 6 の空プロジェクトに `MeshSplitter/` をコピーして検証する。
**共通調査フェーズ 2 の先行検証対象。**

### フェーズ 3（外部依存の Unity 6 対応後）

VRChat SDK 対応版で PhysBone / Constraint 関連の動作を確認する。

## 検証チェックリスト（Unity 6）

### SDK なしで確認できる項目（先行検証）

- [ ] コンパイルエラー・警告が 0 件
- [ ] `dennokoworks/Mesh Splitter` からウィンドウが開き、**日本語が正しく表示される**
- [ ] UI Toolkit のレイアウトが崩れていない
- [ ] **UV プレビュー**（`UvPreviewElement`）が正しく描画される
- [ ] SceneView オーバーレイが表示され、**Unity 6 の刷新されたオーバーレイと重なっていない**
- [ ] メッシュ分割が完走し、分割後のメッシュが正しい
- [ ] **分割後のボーンウェイトが保持されている**（`NativeArray<BoneWeight1>` 経路）
- [ ] ボーンウェイト最適化（`optWeights` 経路）が正しく動作する
- [ ] 分割実行後に Console へ `NativeArray` の leak 警告が出ていない
- [ ] コンポーネント保持ロジックで、モジュール側に必要なコンポーネントだけが残る
- [ ] `VRCAvatarDescriptor` / `Animator` などアバター全体向けコンポーネントが
      モジュール側に混入していない

### SDK 対応版で確認する項目

- [ ] `VRCPhysBone` / `VRCPhysBoneCollider` の検出・移植・パージが動作する
- [ ] `VRCConstraintBase` 派生コンポーネントが正しく扱われる
- [ ] DynamicBone（導入時）の検出が動作する
- [ ] 分割後のアバターがアップロードできる
