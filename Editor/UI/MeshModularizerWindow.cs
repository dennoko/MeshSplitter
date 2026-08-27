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

        private MmState _state = new MmState();
        private SceneSelectionOverlay _sceneOverlay;
        private UvPreviewElement _uvPreview;

        private ObjectField _sourceField;
        private DropdownField _submeshDropdown;
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
        private DennokoVersionChecker.Result _versionResult =
            new DennokoVersionChecker.Result { State = DennokoVersionChecker.State.Checking, LocalVersion = "0.1.0" };

        private int _submeshChoiceCount = -1;

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
            _submeshChoiceCount = -1;
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
                    next.Selection.Clear();
                    AnalyzeMesh(next);
                    break;

                case SetSourceSubmesh a:
                    next.SourceSubmesh = a.SubmeshIndex;
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
                    _submeshChoiceCount = -1;
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
            Render();
        }

        private void AnalyzeMesh(MmState state)
        {
            state.Topology = null;
            state.TopologyError = null;

            if (state.Source == null) return;

            Mesh mesh = null;
            if (state.Source is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (state.Source.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;

            if (mesh == null)
            {
                state.TopologyError = MmLocalization.Tr("error_no_mesh");
                return;
            }

            state.Topology = MeshIslandAnalyzer.Analyze(mesh, state.SourceSubmesh, out string error);
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
            Mesh mesh = null;
            if (state.Source is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (state.Source.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;

            if (mesh == null || mesh.subMeshCount <= 1) return;

            int count = mesh.subMeshCount;
            int success = 0;

            for (int sub = 0; sub < count; sub++)
            {
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
            }

            state.LastMessage = MmLocalization.Tr("submesh_batch_success_format", success, count);
            EditorUtility.DisplayDialog(
                MmLocalization.Tr("dialog_complete_title"),
                state.LastMessage,
                "OK");
        }

        private void Render()
        {
            if (_sourceField == null) return;

            if (_sourceField.value != _state.Source) _sourceField.SetValueWithoutNotify(_state.Source);

            RenderSubmeshChoices();

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

            Mesh mesh = null;
            if (_state.Source is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (_state.Source != null && _state.Source.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;
            _extractSubmeshBtn.SetEnabled(mesh != null && mesh.subMeshCount > 1);

            _extractStatusLabel.text = _state.LastError ?? _state.LastMessage ?? "";
            _extractStatusLabel.style.color = _state.LastError != null ? new StyleColor(new Color(1f, 0.4f, 0.4f)) : new StyleColor(new Color(0.7f, 0.7f, 0.7f));

            _uvPreview.SetSource(_state.Topology, _state.PickMode, _state.Selection, _state.SelectionMode == MmSelectionMode.Add);
            _sceneOverlay?.Render(_state);
        }

        private void OnSubmeshDropdownChanged(ChangeEvent<string> evt)
        {
            if (_submeshDropdown.choices == null) return;
            int index = _submeshDropdown.choices.IndexOf(evt.newValue);
            Dispatch(new SetSourceSubmesh(index <= 0 ? -1 : index - 1));
        }

        private void RenderSubmeshChoices()
        {
            Mesh mesh = null;
            if (_state.Source is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (_state.Source != null && _state.Source.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;

            int count = mesh != null ? mesh.subMeshCount : 0;
            if (count != _submeshChoiceCount || _submeshDropdown.choices == null || _submeshDropdown.choices.Count == 0)
            {
                var choices = new List<string> { MmLocalization.Tr("submesh_choice_all") };
                var materials = _state.Source != null ? _state.Source.sharedMaterials : Array.Empty<Material>();
                for (int i = 0; i < count; i++)
                {
                    string matName = i < materials.Length && materials[i] != null ? materials[i].name : "(no material)";
                    choices.Add($"{i}: {matName}");
                }
                _submeshDropdown.choices = choices;
                _submeshChoiceCount = count;
            }

            var currentChoices = _submeshDropdown.choices;
            if (currentChoices != null && currentChoices.Count > 0)
            {
                int selected = _state.SourceSubmesh < 0 ? 0 : _state.SourceSubmesh + 1;
                if (selected < 0 || selected >= currentChoices.Count) selected = 0;
                _submeshDropdown.SetValueWithoutNotify(currentChoices[selected]);
            }
            _submeshDropdown.SetEnabled(count > 1);
        }

        private string DescribeSource()
        {
            if (_state.Source == null) return MmLocalization.Tr("source_info_empty");
            if (_state.TopologyError != null) return _state.TopologyError;
            if (_state.Topology == null) return MmLocalization.Tr("source_info_analyzing");

            var t = _state.Topology;
            return MmLocalization.Tr("source_info_format", t.Triangles.Length, t.UvIslandCount, t.PolyGroupCount)
                   + (t.HasUv ? "" : MmLocalization.Tr("source_info_no_uv"));
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
