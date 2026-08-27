using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.MeshModularizer
{
    public sealed class MeshModularizerWindow : EditorWindow
    {
        [MenuItem("dennokoworks/Mesh Splitter", false, 200)]
        public static void Open()
        {
            var window = GetWindow<MeshModularizerWindow>("Mesh Splitter");
            window.minSize = new Vector2(380, 560);
            window.Show();
        }

        private const int UvChannelCount = 8;

        private MmState _state = new MmState();

        // ドメインリロード (スクリプト再コンパイル / Play モードの出入り) をまたいで
        // 対象メッシュとサブメッシュ指定を保持する。EditorWindow は ScriptableObject
        // なので、[SerializeField] を付けたフィールドだけがリロード後も残る。
        //
        // MmState 全体は保持しない: MeshTopology も Selection (HashSet) も
        // シリアライズできず、中途半端に欠けた状態を復元することになるため。
        // ポリゴンの選択はリセットし、トポロジは復元時に解析し直す。
        [SerializeField] private Renderer _persistedSource;
        [SerializeField] private int _persistedSubmesh = -1; // -1: すべて
        [SerializeField] private int _persistedUvChannel = 0; // 0: UV0
        private SceneSelectionOverlay _sceneOverlay;
        private UvPreviewElement _uvPreview;

        private ObjectField _sourceField;
        private DropdownField _submeshDropdown;
        private DropdownField _uvDropdown;
        private Button _pickUvBtn;
        private Button _pickPolyBtn;
        private Button _sceneSelectToggleBtn;
        private Toggle _sceneXrayToggle;
        private Label _sourceInfoLabel;
        private Label _selectionInfoLabel;
        private TextField _partNameField;
        private TextField _outputFolderField;
        private Button _extractBtn;
        private Button _extractSubmeshBtn;
        private Label _extractStatusLabel;

        private Label _versionLabel;
        private Button _versionReloadBtn;
        private Button _langButton;
        // ローカル版はハードコードせず version.json から読む
        // (取得できなかった場合は MeshModularizerVersion 側でフォールバックされる)
        private DennokoVersionChecker.Result _versionResult = new DennokoVersionChecker.Result
        {
            State = DennokoVersionChecker.State.Checking,
            LocalVersion = MeshModularizerVersion.Current
        };

        private void OnEnable()
        {
            _sceneOverlay = new SceneSelectionOverlay(Dispatch);
            MmLocalization.OnLanguageChanged += HandleLanguageChanged;
        }

        private void OnDisable()
        {
            MmLocalization.OnLanguageChanged -= HandleLanguageChanged;
            _sceneOverlay?.Dispose();
            _sceneOverlay = null;
        }

        private void HandleLanguageChanged()
        {
            ApplyLocalization();
            Render();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("dennoko-root");

            // スタイルのロード
            LoadStyle("DennokoTheme.uss");
            LoadStyle("MeshModularizerStyles.uss");

            // UXML ロード
            var uxml = FindAsset<VisualTreeAsset>("MeshModularizerWindow.uxml");
            if (uxml != null)
            {
                uxml.CloneTree(rootVisualElement);
            }
            else
            {
                rootVisualElement.Add(new Label("MeshModularizerWindow.uxml が見つかりません。"));
                return;
            }

            DennokoUIFont.Apply(rootVisualElement);
            BindUI();
            ApplyLocalization();
            StartVersionCheck();
            RestorePersistedSource();
            Render();
        }

        private void LoadStyle(string name)
        {
            var sheet = FindAsset<StyleSheet>(name);
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
        }

        private static T FindAsset<T>(string fileName) where T : UnityEngine.Object
        {
            // 他の dennokoworks ツールが同名アセット (DennokoTheme.uss 等) を同梱するため、
            // プロジェクト全体ではなく本ツールのフォルダ配下から解決する。
            return MmAssets.Find<T>(fileName);
        }

        private void BindUI()
        {
            var root = rootVisualElement;

            _versionLabel = root.Q<Label>("version-label");
            _versionReloadBtn = root.Q<Button>("version-reload-button");
            if (_versionReloadBtn != null)
            {
                _versionReloadBtn.clicked += () =>
                {
                    MeshModularizerVersion.ForceRecheck();
                    LoadVersionResultFromSessionState();
                };
            }

            _langButton = root.Q<Button>("lang-button");
            if (_langButton != null)
            {
                _langButton.clicked += () => MmLocalization.ToggleLanguage();
            }

            _sourceField = root.Q<ObjectField>("source-field");
            _sourceField.objectType = typeof(Renderer);
            _sourceField.RegisterValueChangedCallback(evt => Dispatch(new SetSource(evt.newValue as Renderer)));

            _submeshDropdown = root.Q<DropdownField>("submesh-dropdown");
            _submeshDropdown.RegisterValueChangedCallback(OnSubmeshDropdownChanged);

            _uvDropdown = root.Q<DropdownField>("uv-dropdown");
            if (_uvDropdown != null)
            {
                _uvDropdown.RegisterValueChangedCallback(OnUvDropdownChanged);
            }

            root.Q<Button>("source-from-selection").clicked += () => Dispatch(new CmdPickSourceFromSelection());

            var sourceReloadBtn = root.Q<Button>("source-reload-button");
            if (sourceReloadBtn != null)
            {
                sourceReloadBtn.clicked += () => Dispatch(new CmdAnalyzeSource());
            }

            _pickUvBtn = root.Q<Button>("pick-uv");
            _pickUvBtn.clicked += () => Dispatch(new SetPickMode(MmPickMode.UvIsland));

            _pickPolyBtn = root.Q<Button>("pick-poly");
            _pickPolyBtn.clicked += () => Dispatch(new SetPickMode(MmPickMode.ConnectedPolygon));

            root.Q<Button>("select-all").clicked += () => Dispatch(new SelectAllGroups());
            root.Q<Button>("select-none").clicked += () => Dispatch(new ClearSelection());
            root.Q<Button>("select-invert").clicked += () => Dispatch(new InvertSelection());

            _uvPreview = new UvPreviewElement();
            _uvPreview.style.flexGrow = 1f;
            _uvPreview.SelectionRequested += (groups, add) => Dispatch(new ModifySelection(groups, add));
            root.Q<VisualElement>("uv-preview-host").Add(_uvPreview);

            _sceneSelectToggleBtn = root.Q<Button>("scene-select-toggle");
            _sceneSelectToggleBtn.clicked += () => Dispatch(new ToggleSceneSelection());

            _sceneXrayToggle = root.Q<Toggle>("opt-scene-xray");
            _sceneXrayToggle.RegisterValueChangedCallback(evt => Dispatch(new SetSceneOverlayXray(evt.newValue)));

            _sourceInfoLabel = root.Q<Label>("source-info");
            _selectionInfoLabel = root.Q<Label>("selection-info");

            _partNameField = root.Q<TextField>("part-name");
            _partNameField.RegisterValueChangedCallback(evt => Dispatch(new SetPartName(evt.newValue)));

            _outputFolderField = root.Q<TextField>("output-folder");
            _outputFolderField.RegisterValueChangedCallback(evt => Dispatch(new SetOutputFolder(evt.newValue)));

            _extractBtn = root.Q<Button>("extract-button");
            _extractBtn.clicked += () => Dispatch(new CmdExtractPart());

            _extractSubmeshBtn = root.Q<Button>("extract-submesh-button");
            _extractSubmeshBtn.clicked += () => Dispatch(new CmdExtractPerSubmesh());

            _extractStatusLabel = root.Q<Label>("extract-status");
        }

        private void Dispatch(IMmAction action)
        {
            var next = _state.Clone();
            switch (action)
            {
                case SetSource a:
                    next.Source = a.Source;
                    next.SourceSubmesh = -1;
                    next.SourceUvChannel = 0;
                    next.Selection.Clear();
                    AnalyzeMesh(next);
                    break;

                case SetSourceSubmesh a:
                    next.SourceSubmesh = a.SubmeshIndex;
                    next.Selection.Clear();
                    AnalyzeMesh(next);
                    break;

                case SetSourceUvChannel a:
                    next.SourceUvChannel = a.UvChannel;
                    next.Selection.Clear();
                    AnalyzeMesh(next);
                    break;

                case CmdPickSourceFromSelection _:
                    var sel = Selection.activeGameObject;
                    if (sel != null)
                    {
                        var r = sel.GetComponent<Renderer>();
                        if (r != null)
                        {
                            next.Source = r;
                            next.SourceSubmesh = -1;
                            next.SourceUvChannel = 0;
                            next.Selection.Clear();
                            AnalyzeMesh(next);
                        }
                    }
                    break;

                case CmdAnalyzeSource _:
                    if (next.Source == null && _sourceField != null && _sourceField.value != null)
                    {
                        next.Source = _sourceField.value as Renderer;
                    }
                    AnalyzeMesh(next);
                    break;

                case SetPickMode a:
                    next.PickMode = a.Mode;
                    break;

                case SetSelectionMode a:
                    next.SelectionMode = a.Mode;
                    break;

                case ModifySelection a:
                    foreach (int g in a.Groups)
                    {
                        if (a.Add) next.Selection.Add(g);
                        else next.Selection.Remove(g);
                    }
                    break;

                case SelectAllGroups _:
                    if (next.Topology != null)
                    {
                        int count = next.Topology.GroupCount(next.PickMode);
                        for (int i = 0; i < count; i++) next.Selection.Add(i);
                    }
                    break;

                case ClearSelection _:
                    next.Selection.Clear();
                    break;

                case InvertSelection _:
                    if (next.Topology != null)
                    {
                        int count = next.Topology.GroupCount(next.PickMode);
                        var inv = new HashSet<int>();
                        for (int i = 0; i < count; i++)
                        {
                            if (!next.Selection.Contains(i)) inv.Add(i);
                        }
                        next.Selection = inv;
                    }
                    break;

                case SetPartName a:
                    next.PartName = a.Value;
                    break;

                case SetOutputFolder a:
                    next.OutputFolder = a.Value;
                    break;

                case ToggleSceneSelection _:
                    next.SceneSelectionEnabled = !next.SceneSelectionEnabled;
                    break;

                case SetSceneOverlayXray a:
                    next.SceneOverlayXray = a.Value;
                    break;

                case CmdExtractPart _:
                    ExtractPart(next);
                    break;

                case CmdExtractPerSubmesh _:
                    ExtractPerSubmesh(next);
                    break;
            }

            _state = next;
            _persistedSource = _state.Source;
            _persistedSubmesh = _state.SourceSubmesh;
            _persistedUvChannel = _state.SourceUvChannel;
            Render();
        }

        /// <summary>
        /// ドメインリロード前に選んでいた対象メッシュとサブメッシュ指定を復元する。
        /// トポロジはシリアライズできないので、ここで解析し直す。
        /// </summary>
        private void RestorePersistedSource()
        {
            // 破棄済みのオブジェクトは Unity の null 判定で弾かれる
            if (_persistedSource == null || _state.Source != null) return;

            _state.Source = _persistedSource;
            _state.SourceSubmesh = ClampSubmesh(_persistedSubmesh, GetSharedMesh(_persistedSource));
            _state.SourceUvChannel = _persistedUvChannel;
            // UV チャンネルの丸めは AnalyzeMesh 側でまとめて行うので、保持値の書き戻しは解析後。
            AnalyzeMesh(_state);
            _persistedSubmesh = _state.SourceSubmesh;
            _persistedUvChannel = _state.SourceUvChannel;
        }

        /// <summary>
        /// 保持していたサブメッシュ番号を現在のメッシュに合わせる。
        /// リロードを挟む間に Renderer へ別のメッシュが差し替わっていると範囲外になり得るため、
        /// その場合は「すべて」(-1) に戻す。範囲外のまま解析すると
        /// 「対象サブメッシュに三角形がありません」で止まってしまう。
        /// </summary>
        private static int ClampSubmesh(int submesh, Mesh mesh)
        {
            if (submesh < 0 || mesh == null) return -1;
            return submesh < mesh.subMeshCount ? submesh : -1;
        }

        /// <summary>
        /// 指定された UV チャンネルを現在のメッシュに合わせる。
        /// メッシュが差し替わって該当チャンネルが無くなっていると、解析は「UV なし」扱いに落ちる一方で
        /// ドロップダウンは別のチャンネルを表示してしまい、表示と状態が食い違うため、
        /// 実在する最小のチャンネルへ戻す。UV を一切持たないメッシュでは 0 を返す。
        /// </summary>
        private static int ClampUvChannel(int channel, Mesh mesh)
        {
            if (mesh == null) return 0;
            if (HasUvChannel(mesh, channel)) return channel;

            for (int ch = 0; ch < UvChannelCount; ch++)
            {
                if (HasUvChannel(mesh, ch)) return ch;
            }
            return 0;
        }

        /// <summary>
        /// メッシュが指定 UV チャンネルを持つか。
        /// 頂点属性の宣言を見るだけなので、Read/Write 無効なメッシュでも安全に呼べる。
        /// </summary>
        private static bool HasUvChannel(Mesh mesh, int channel)
        {
            if (mesh == null || channel < 0 || channel >= UvChannelCount) return false;
            return mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel);
        }

        /// <summary>Renderer に割り当てられているメッシュ。取得できなければ null。</summary>
        private static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer == null) return null;
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private void AnalyzeMesh(MmState state)
        {
            state.Topology = null;
            state.TopologyError = null;

            if (state.Source == null) return;

            Mesh mesh = GetSharedMesh(state.Source);

            if (mesh == null)
            {
                state.TopologyError = MmLocalization.Tr("error_no_mesh");
                return;
            }

            state.SourceUvChannel = ClampUvChannel(state.SourceUvChannel, mesh);
            state.Topology = MeshIslandAnalyzer.Analyze(mesh, state.SourceSubmesh, state.SourceUvChannel, out string error);
            state.TopologyError = error;
        }

        private void ExtractPart(MmState state)
        {
            if (state.Source == null || state.Topology == null || state.Selection.Count == 0) return;

            var triangles = state.Topology.ResolveTriangles(state.PickMode, state.Selection);
            if (triangles.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    MmLocalization.Tr("dialog_error_title"),
                    MmLocalization.Tr("dialog_no_triangles"),
                    "OK");
                return;
            }

            var request = new ModularizeRequest
            {
                SourceRenderer = state.Source,
                TriangleIndices = triangles,
                PartName = state.PartName,
                OutputFolder = state.OutputFolder,
                KeepConstraints = state.KeepConstraints,
                RecalculateBounds = state.RecalculateBounds,
                TrimUnusedBones = state.TrimUnusedBones,
                KeepBlendShapes = state.KeepBlendShapes,
                KeepPhysBones = state.KeepPhysBones,
                KeepPhysBoneColliders = state.KeepPhysBoneColliders,
                AutoInstantiate = state.AutoInstantiate
            };

            var res = MeshModularizerService.Execute(request);
            if (res.Ok)
            {
                state.LastMessage = DescribeResult(res);
                state.LastError = null;
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(res.PrefabPath));
            }
            else
            {
                state.LastError = res.Error;
                EditorUtility.DisplayDialog(
                    MmLocalization.Tr("dialog_extract_failed"),
                    res.Error,
                    "OK");
            }
        }

        private void ExtractPerSubmesh(MmState state)
        {
            if (state.Source == null) return;
            Mesh mesh = GetSharedMesh(state.Source);

            if (mesh == null || mesh.subMeshCount <= 1) return;

            int count = mesh.subMeshCount;
            int success = 0;
            string firstError = null;

            try
            {
                for (int sub = 0; sub < count; sub++)
                {
                    // 1 件ごとに複製範囲 (多くの場合アバター階層全体) を複製して Prefab 化するため、
                    // 進捗を出さないと Unity がフリーズしたように見える。
                    bool canceled = EditorUtility.DisplayCancelableProgressBar(
                        MmLocalization.Tr("btn_extract_submesh"),
                        MmLocalization.Tr("submesh_batch_progress_format", sub + 1, count),
                        (float)sub / count);
                    if (canceled) break;

                    int indexStart = (int)mesh.GetIndexStart(sub);
                    int indexCount = (int)mesh.GetIndexCount(sub);
                    var triangles = new List<int>(indexCount / 3);
                    for (int i = 0; i < indexCount; i += 3)
                    {
                        triangles.Add((indexStart + i) / 3);
                    }

                    string name = $"{state.PartName}_Submesh{sub}";
                    var req = new ModularizeRequest
                    {
                        SourceRenderer = state.Source,
                        TriangleIndices = triangles,
                        PartName = name,
                        OutputFolder = state.OutputFolder,
                        KeepConstraints = state.KeepConstraints,
                        RecalculateBounds = state.RecalculateBounds,
                        TrimUnusedBones = state.TrimUnusedBones,
                        KeepBlendShapes = state.KeepBlendShapes,
                        KeepPhysBones = state.KeepPhysBones,
                        KeepPhysBoneColliders = state.KeepPhysBoneColliders,
                        AutoInstantiate = state.AutoInstantiate
                    };

                    var res = MeshModularizerService.Execute(req);
                    if (res.Ok) success++;
                    else if (firstError == null) firstError = res.Error;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            state.LastMessage = MmLocalization.Tr("submesh_batch_success_format", success, count);
            // 前回の切り出し失敗が残っているとステータス欄が古いエラーを表示し続けるため、
            // 1 件でも成功したらクリアし、全滅した場合だけ原因を残す。
            state.LastError = success == 0 ? firstError : null;

            // 失敗した理由を握り潰すと「0/5 出力しました」だけが出て原因が分からなくなる。
            string detail = firstError != null
                ? state.LastMessage + "\n" + firstError
                : state.LastMessage;

            EditorUtility.DisplayDialog(
                MmLocalization.Tr("dialog_complete_title"),
                detail,
                "OK");
        }

        private void Render()
        {
            if (_sourceField == null) return;

            if (_sourceField.value != _state.Source) _sourceField.SetValueWithoutNotify(_state.Source);

            RenderSubmeshChoices();
            RenderUvChoices();

            _sourceInfoLabel.text = DescribeSource();
            _selectionInfoLabel.text = DescribeSelection();

            SetButtonActive(_pickUvBtn, _state.PickMode == MmPickMode.UvIsland);
            SetButtonActive(_pickPolyBtn, _state.PickMode == MmPickMode.ConnectedPolygon);

            _sceneSelectToggleBtn.text = _state.SceneSelectionEnabled
                ? MmLocalization.Tr("btn_scene_select_on")
                : MmLocalization.Tr("btn_scene_select_off");
            SetButtonActive(_sceneSelectToggleBtn, _state.SceneSelectionEnabled);
            _sceneXrayToggle.SetValueWithoutNotify(_state.SceneOverlayXray);

            _partNameField.SetValueWithoutNotify(_state.PartName);
            _outputFolderField.SetValueWithoutNotify(_state.OutputFolder);

            _extractBtn.SetEnabled(_state.Topology != null && _state.Selection.Count > 0);

            Mesh mesh = GetSharedMesh(_state.Source);
            _extractSubmeshBtn.SetEnabled(mesh != null && mesh.subMeshCount > 1);

            _extractStatusLabel.text = _state.LastError ?? _state.LastMessage ?? "";
            _extractStatusLabel.style.color = _state.LastError != null ? new StyleColor(new Color(1f, 0.4f, 0.4f)) : new StyleColor(new Color(0.7f, 0.7f, 0.7f));

            _uvPreview.SetSource(
                _state.Topology, _state.PickMode, _state.Selection, _state.SelectionMode == MmSelectionMode.Add,
                MainTextureResolver.Resolve(_state.Source, _state.SourceSubmesh, _state.SourceUvChannel));
            _sceneOverlay?.Render(_state);
        }

        private void OnSubmeshDropdownChanged(ChangeEvent<string> evt)
        {
            if (_submeshDropdown.choices == null) return;
            int index = _submeshDropdown.choices.IndexOf(evt.newValue);
            Dispatch(new SetSourceSubmesh(index <= 0 ? -1 : index - 1));
        }

        private static string UvChannelLabel(int channel) => $"UV{channel}";

        private void OnUvDropdownChanged(ChangeEvent<string> evt)
        {
            // 選択肢は実在するチャンネルだけを並べているので、番号はラベルから取り出す
            for (int ch = 0; ch < UvChannelCount; ch++)
            {
                if (evt.newValue == UvChannelLabel(ch))
                {
                    Dispatch(new SetSourceUvChannel(ch));
                    return;
                }
            }
        }

        private void RenderSubmeshChoices()
        {
            Mesh mesh = GetSharedMesh(_state.Source);

            int count = mesh != null ? mesh.subMeshCount : 0;
            var choices = new List<string> { MmLocalization.Tr("submesh_choice_all") };
            var materials = _state.Source != null ? _state.Source.sharedMaterials : Array.Empty<Material>();
            for (int i = 0; i < count; i++)
            {
                string matName = i < materials.Length && materials[i] != null ? materials[i].name : "(no material)";
                choices.Add($"{i}: {matName}");
            }
            _submeshDropdown.choices = choices;

            int selected = _state.SourceSubmesh < 0 ? 0 : _state.SourceSubmesh + 1;
            if (selected < 0 || selected >= choices.Count) selected = 0;
            _submeshDropdown.SetValueWithoutNotify(choices[selected]);
            _submeshDropdown.SetEnabled(count > 1);
        }

        private void RenderUvChoices()
        {
            if (_uvDropdown == null) return;

            Mesh mesh = GetSharedMesh(_state.Source);
            var choices = new List<string>();
            for (int ch = 0; ch < UvChannelCount; ch++)
            {
                if (HasUvChannel(mesh, ch)) choices.Add(UvChannelLabel(ch));
            }
            // UV を持たないメッシュでも空のドロップダウンにはしない
            if (choices.Count == 0) choices.Add(UvChannelLabel(0));
            _uvDropdown.choices = choices;

            string selectedValue = UvChannelLabel(_state.SourceUvChannel);
            if (!choices.Contains(selectedValue)) selectedValue = choices[0];
            _uvDropdown.SetValueWithoutNotify(selectedValue);
            _uvDropdown.SetEnabled(choices.Count > 1);
        }

        private string DescribeSource()
        {
            if (_state.Source == null) return MmLocalization.Tr("source_info_empty");
            if (_state.TopologyError != null) return _state.TopologyError;
            if (_state.Topology == null) return MmLocalization.Tr("source_info_analyzing");

            var t = _state.Topology;
            return MmLocalization.Tr("source_info_format", t.Triangles.Length, t.UvIslandCount, t.PolyGroupCount)
                   + (t.HasUv ? "" : MmLocalization.Tr("source_info_no_uv", t.UvChannel));
        }

        private string DescribeSelection()
        {
            if (_state.Topology == null) return MmLocalization.Tr("selection_info_unanalyzed");
            if (_state.Selection.Count == 0) return MmLocalization.Tr("selection_info_unselected");

            int triangles = _state.Topology.CountTriangles(_state.PickMode, _state.Selection);
            string unit = _state.PickMode == MmPickMode.UvIsland ? MmLocalization.Tr("unit_island") : MmLocalization.Tr("unit_group");
            return MmLocalization.Tr("selection_info_format", _state.Selection.Count, unit, triangles);
        }

        private static string DescribeResult(ModularizeResult result)
        {
            string text = MmLocalization.Tr("extract_success_format",
                result.PrefabPath,
                result.TriangleCount,
                result.VertexCount,
                result.ScopeRootName,
                result.RemovedObjectCount,
                result.RemovedComponentCount,
                result.PurgedPhysBoneCount);

            foreach (var note in result.Notes) text += "\n" + note;
            return text;
        }

        private static void SetButtonActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList("dennoko-button-active");
            else btn.RemoveFromClassList("dennoko-button-active");
        }

        private void ApplyLocalization()
        {
            var root = rootVisualElement;
            if (root == null) return;

            if (_langButton != null)
            {
                _langButton.text = MmLocalization.Tr("lang_button_text");
                _langButton.tooltip = MmLocalization.Tr("lang_button_tooltip");
            }

            var titleLabel = root.Q<Label>("title-label");
            if (titleLabel != null) titleLabel.text = MmLocalization.Tr("header_title");

            if (_versionReloadBtn != null) _versionReloadBtn.tooltip = MmLocalization.Tr("ver_reload_tooltip");

            var hTarget = root.Q<TextElement>("header-target-mesh");
            if (hTarget != null) hTarget.text = MmLocalization.Tr("section_target_mesh");

            var srcFromSel = root.Q<Button>("source-from-selection");
            if (srcFromSel != null)
            {
                srcFromSel.text = MmLocalization.Tr("btn_pick_source");
                srcFromSel.tooltip = MmLocalization.Tr("tooltip_pick_source");
            }

            var srcReload = root.Q<Button>("source-reload-button");
            if (srcReload != null) srcReload.tooltip = MmLocalization.Tr("tooltip_reload_source");

            if (_submeshDropdown != null) _submeshDropdown.label = MmLocalization.Tr("label_submesh");

            if (_uvDropdown != null) _uvDropdown.tooltip = MmLocalization.Tr("tooltip_uv_channel");

            var hSelection = root.Q<TextElement>("header-selection");
            if (hSelection != null) hSelection.text = MmLocalization.Tr("section_selection");

            if (_pickUvBtn != null) _pickUvBtn.text = MmLocalization.Tr("btn_pick_uv");
            if (_pickPolyBtn != null) _pickPolyBtn.text = MmLocalization.Tr("btn_pick_poly");

            var btnSelAll = root.Q<Button>("select-all");
            if (btnSelAll != null) btnSelAll.text = MmLocalization.Tr("btn_select_all");

            var btnSelNone = root.Q<Button>("select-none");
            if (btnSelNone != null) btnSelNone.text = MmLocalization.Tr("btn_select_none");

            var btnSelInv = root.Q<Button>("select-invert");
            if (btnSelInv != null) btnSelInv.text = MmLocalization.Tr("btn_select_invert");

            if (_sceneXrayToggle != null) _sceneXrayToggle.label = MmLocalization.Tr("toggle_scene_xray");

            var hPrefab = root.Q<TextElement>("header-prefab-output");
            if (hPrefab != null) hPrefab.text = MmLocalization.Tr("section_prefab_output");

            if (_partNameField != null) _partNameField.label = MmLocalization.Tr("label_part_name");
            if (_outputFolderField != null) _outputFolderField.label = MmLocalization.Tr("label_output_folder");

            if (_extractBtn != null) _extractBtn.text = MmLocalization.Tr("btn_extract_part");
            if (_extractSubmeshBtn != null) _extractSubmeshBtn.text = MmLocalization.Tr("btn_extract_submesh");

            ApplyVersionLabel();
        }

        private void StartVersionCheck()
        {
            LoadVersionResultFromSessionState();
            MeshModularizerVersion.StartCheckBackgroundTask();
        }

        internal void LoadVersionResultFromSessionState()
        {
            string local  = MeshModularizerVersion.Current;
            string latest = SessionState.GetString(MeshModularizerVersion.VerCheckLatestKey, string.Empty);
            bool   done   = SessionState.GetBool(MeshModularizerVersion.VerCheckDoneKey, false);
            bool   error  = SessionState.GetBool(MeshModularizerVersion.VerCheckErrorKey, false);

            DennokoVersionChecker.State state;
            if (!done)
                state = DennokoVersionChecker.State.Checking;
            else if (error || string.IsNullOrEmpty(latest))
                state = DennokoVersionChecker.State.Error;
            else if (DennokoVersionChecker.IsUpdateAvailable(latest, local))
                state = DennokoVersionChecker.State.UpdateAvailable;
            else
                state = DennokoVersionChecker.State.UpToDate;

            _versionResult = new DennokoVersionChecker.Result
            {
                State = state,
                LocalVersion = local,
                LatestVersion = latest,
                Url = SessionState.GetString(MeshModularizerVersion.VerCheckUrlKey, string.Empty),
                Message = SessionState.GetString(MeshModularizerVersion.VerCheckMessageKey, string.Empty)
            };
            ApplyVersionLabel();
        }

        private void ApplyVersionLabel()
        {
            if (_versionLabel == null) return;

            var r = _versionResult;
            string baseText = "v" + r.LocalVersion;
            string text;
            bool update = false, error = false;
            switch (r.State)
            {
                case DennokoVersionChecker.State.UpdateAvailable:
                    text = $"{baseText}  {MmLocalization.Tr("ver_update", r.LatestVersion)}";
                    update = true;
                    break;
                case DennokoVersionChecker.State.Error:
                    text = $"{baseText}  {MmLocalization.Tr("ver_error")}";
                    error = true;
                    break;
                case DennokoVersionChecker.State.Checking:
                    text = $"{baseText}  {MmLocalization.Tr("ver_checking")}";
                    break;
                default: // UpToDate
                    text = baseText;
                    break;
            }
            _versionLabel.text = text;
            _versionLabel.EnableInClassList("dennoko-version-label--update", update);
            _versionLabel.EnableInClassList("dennoko-version-label--error", error);
        }
    }
}
