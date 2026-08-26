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
        [MenuItem("Window/dennokoworks/Mesh Modularizer", false, 200)]
        [MenuItem("Tools/dennokoworks/Mesh Modularizer", false, 200)]
        public static void Open()
        {
            var window = GetWindow<MeshModularizerWindow>("Mesh Modularizer");
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
        private Button _selAddBtn;
        private Button _selRemoveBtn;
        private Button _sceneSelectToggleBtn;
        private Toggle _sceneXrayToggle;
        private Label _sourceInfoLabel;
        private Label _selectionInfoLabel;
        private TextField _partNameField;
        private TextField _outputFolderField;
        private Button _extractBtn;
        private Button _extractSubmeshBtn;
        private Label _extractStatusLabel;

        private Button _policyKeepAllBtn;
        private Button _policyDepsOnlyBtn;
        private Label _policyNoteLabel;
        private Label _scopeInfoLabel;
        private Toggle _keepPhysBonesToggle;
        private Toggle _keepConstraintsToggle;
        private Toggle _removeOtherRenderersToggle;
        private Toggle _trimUnusedBonesToggle;
        private Toggle _keepBlendShapesToggle;
        private Toggle _recalculateBoundsToggle;
        private Toggle _addBoneProxyToggle;
        private EnumField _boneProxyTargetField;
        private Toggle _autoInstantiateToggle;

        private int _submeshChoiceCount = -1;

        private void OnEnable()
        {
            _sceneOverlay = new SceneSelectionOverlay(Dispatch);
        }

        private void OnDisable()
        {
            _sceneOverlay?.Dispose();
            _sceneOverlay = null;
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
            Render();
        }

        private void LoadStyle(string name)
        {
            var sheet = FindAsset<StyleSheet>(name);
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
        }

        private static T FindAsset<T>(string fileName) where T : UnityEngine.Object
        {
            var guids = AssetDatabase.FindAssets(System.IO.Path.GetFileNameWithoutExtension(fileName));
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return AssetDatabase.LoadAssetAtPath<T>(path);
                }
            }
            return null;
        }

        private void BindUI()
        {
            var root = rootVisualElement;

            _sourceField = root.Q<ObjectField>("source-field");
            _sourceField.objectType = typeof(Renderer);
            _sourceField.RegisterValueChangedCallback(evt => Dispatch(new SetSource(evt.newValue as Renderer)));

            _submeshDropdown = root.Q<DropdownField>("submesh-dropdown");
            _submeshDropdown.RegisterValueChangedCallback(OnSubmeshDropdownChanged);

            root.Q<Button>("source-from-selection").clicked += () => Dispatch(new CmdPickSourceFromSelection());
            root.Q<Button>("analyze-button").clicked += () => Dispatch(new CmdAnalyzeSource());

            _pickUvBtn = root.Q<Button>("pick-uv");
            _pickUvBtn.clicked += () => Dispatch(new SetPickMode(MmPickMode.UvIsland));

            _pickPolyBtn = root.Q<Button>("pick-poly");
            _pickPolyBtn.clicked += () => Dispatch(new SetPickMode(MmPickMode.ConnectedPolygon));

            _selAddBtn = root.Q<Button>("sel-add");
            _selAddBtn.clicked += () => Dispatch(new SetSelectionMode(MmSelectionMode.Add));

            _selRemoveBtn = root.Q<Button>("sel-remove");
            _selRemoveBtn.clicked += () => Dispatch(new SetSelectionMode(MmSelectionMode.Remove));

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

            _policyKeepAllBtn = root.Q<Button>("policy-keep-all");
            _policyKeepAllBtn.clicked += () => Dispatch(new SetComponentPolicy(MmComponentPolicy.KeepAll));

            _policyDepsOnlyBtn = root.Q<Button>("policy-deps-only");
            _policyDepsOnlyBtn.clicked += () => Dispatch(new SetComponentPolicy(MmComponentPolicy.MeshDependenciesOnly));

            _policyNoteLabel = root.Q<Label>("policy-note");
            _scopeInfoLabel = root.Q<Label>("scope-info");

            _keepPhysBonesToggle = BindToggle(root, "opt-keep-physbones", v => new SetKeepPhysBones(v));
            _keepConstraintsToggle = BindToggle(root, "opt-keep-constraints", v => new SetKeepConstraints(v));
            _removeOtherRenderersToggle = BindToggle(root, "opt-remove-other-renderers", v => new SetRemoveOtherRenderers(v));
            _trimUnusedBonesToggle = BindToggle(root, "opt-trim-unused-bones", v => new SetTrimUnusedBones(v));
            _keepBlendShapesToggle = BindToggle(root, "opt-keep-blendshapes", v => new SetKeepBlendShapes(v));
            _recalculateBoundsToggle = BindToggle(root, "opt-recalculate-bounds", v => new SetRecalculateBounds(v));
            _addBoneProxyToggle = BindToggle(root, "opt-add-bone-proxy", v => new SetAddBoneProxy(v));
            _autoInstantiateToggle = BindToggle(root, "opt-auto-instantiate", v => new SetAutoInstantiate(v));

            _boneProxyTargetField = root.Q<EnumField>("opt-bone-proxy-target");
            _boneProxyTargetField.Init(HumanBodyBones.Head);
            _boneProxyTargetField.RegisterValueChangedCallback(
                evt => Dispatch(new SetBoneProxyTarget((HumanBodyBones)evt.newValue)));

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

                case SetComponentPolicy a:
                    next.ComponentPolicy = a.Value;
                    break;

                case SetRemoveOtherRenderers a:
                    next.RemoveOtherRenderers = a.Value;
                    break;

                case SetRecalculateBounds a:
                    next.RecalculateBounds = a.Value;
                    break;

                case SetTrimUnusedBones a:
                    next.TrimUnusedBones = a.Value;
                    break;

                case SetKeepBlendShapes a:
                    next.KeepBlendShapes = a.Value;
                    break;

                case SetKeepPhysBones a:
                    next.KeepPhysBones = a.Value;
                    break;

                case SetKeepConstraints a:
                    next.KeepConstraints = a.Value;
                    break;

                case SetAddBoneProxy a:
                    next.AddBoneProxy = a.Value;
                    break;

                case SetBoneProxyTarget a:
                    next.BoneProxyTarget = a.Value;
                    break;

                case SetAutoInstantiate a:
                    next.AutoInstantiate = a.Value;
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
                state.TopologyError = "Renderer にメッシュが割り当てられていません。";
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
                EditorUtility.DisplayDialog("エラー", "選択された三角形がありません。", "OK");
                return;
            }

            var request = new ModularizeRequest
            {
                SourceRenderer = state.Source,
                TriangleIndices = triangles,
                PartName = state.PartName,
                OutputFolder = state.OutputFolder,
                ComponentPolicy = state.ComponentPolicy,
                RemoveOtherRenderers = state.RemoveOtherRenderers,
                KeepConstraints = state.KeepConstraints,
                RecalculateBounds = state.RecalculateBounds,
                TrimUnusedBones = state.TrimUnusedBones,
                KeepBlendShapes = state.KeepBlendShapes,
                KeepPhysBones = state.KeepPhysBones,
                AddBoneProxy = state.AddBoneProxy,
                BoneProxyTarget = state.BoneProxyTarget,
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
                EditorUtility.DisplayDialog("切り出し失敗", res.Error, "OK");
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
                    ComponentPolicy = state.ComponentPolicy,
                    RemoveOtherRenderers = state.RemoveOtherRenderers,
                    KeepConstraints = state.KeepConstraints,
                    RecalculateBounds = state.RecalculateBounds,
                    TrimUnusedBones = state.TrimUnusedBones,
                    KeepBlendShapes = state.KeepBlendShapes,
                    KeepPhysBones = state.KeepPhysBones,
                    AddBoneProxy = state.AddBoneProxy,
                    BoneProxyTarget = state.BoneProxyTarget,
                    AutoInstantiate = state.AutoInstantiate
                };

                var res = MeshModularizerService.Execute(req);
                if (res.Ok) success++;
            }

            state.LastMessage = $"{success}/{count} 個のサブメッシュを個別Prefabとして出力しました。";
            EditorUtility.DisplayDialog("完了", state.LastMessage, "OK");
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
            SetButtonActive(_selAddBtn, _state.SelectionMode == MmSelectionMode.Add);
            SetButtonActive(_selRemoveBtn, _state.SelectionMode == MmSelectionMode.Remove);

            _sceneSelectToggleBtn.text = _state.SceneSelectionEnabled ? "シーンでクリック選択: ON" : "シーンでクリック選択: OFF";
            SetButtonActive(_sceneSelectToggleBtn, _state.SceneSelectionEnabled);
            _sceneXrayToggle.SetValueWithoutNotify(_state.SceneOverlayXray);

            _partNameField.SetValueWithoutNotify(_state.PartName);
            _outputFolderField.SetValueWithoutNotify(_state.OutputFolder);

            _extractBtn.SetEnabled(_state.Topology != null && _state.Selection.Count > 0);

            Mesh mesh = null;
            if (_state.Source is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (_state.Source != null && _state.Source.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;
            _extractSubmeshBtn.SetEnabled(mesh != null && mesh.subMeshCount > 1);

            SetButtonActive(_policyKeepAllBtn, _state.ComponentPolicy == MmComponentPolicy.KeepAll);
            SetButtonActive(_policyDepsOnlyBtn, _state.ComponentPolicy == MmComponentPolicy.MeshDependenciesOnly);
            _policyNoteLabel.text = DescribePolicy();
            _scopeInfoLabel.text = ExtractionScope.Describe(_state.Source);

            _keepPhysBonesToggle.SetValueWithoutNotify(_state.KeepPhysBones);
            _keepConstraintsToggle.SetValueWithoutNotify(_state.KeepConstraints);
            _removeOtherRenderersToggle.SetValueWithoutNotify(_state.RemoveOtherRenderers);
            _trimUnusedBonesToggle.SetValueWithoutNotify(_state.TrimUnusedBones);
            _keepBlendShapesToggle.SetValueWithoutNotify(_state.KeepBlendShapes);
            _recalculateBoundsToggle.SetValueWithoutNotify(_state.RecalculateBounds);
            _addBoneProxyToggle.SetValueWithoutNotify(_state.AddBoneProxy);
            _autoInstantiateToggle.SetValueWithoutNotify(_state.AutoInstantiate);
            _boneProxyTargetField.SetValueWithoutNotify(_state.BoneProxyTarget);

            // メッシュ依存のみの方針では対象以外の Renderer は常に除去される。
            _removeOtherRenderersToggle.SetEnabled(_state.ComponentPolicy == MmComponentPolicy.KeepAll);
            _addBoneProxyToggle.SetEnabled(ModularAvatarBridge.IsAvailable);
            _boneProxyTargetField.SetEnabled(_state.AddBoneProxy && ModularAvatarBridge.IsAvailable);

            _extractStatusLabel.text = _state.LastError ?? _state.LastMessage ?? "";
            _extractStatusLabel.style.color = _state.LastError != null ? new StyleColor(new Color(1f, 0.4f, 0.4f)) : new StyleColor(new Color(0.7f, 0.7f, 0.7f));

            _uvPreview.SetSource(_state.Topology, _state.PickMode, _state.Selection, _state.SelectionMode == MmSelectionMode.Add);
            _sceneOverlay?.Render(_state);
        }

        private void OnSubmeshDropdownChanged(ChangeEvent<string> evt)
        {
            int index = _submeshDropdown.choices.IndexOf(evt.newValue);
            Dispatch(new SetSourceSubmesh(index <= 0 ? -1 : index - 1));
        }

        private void RenderSubmeshChoices()
        {
            Mesh mesh = null;
            if (_state.Source is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (_state.Source != null && _state.Source.GetComponent<MeshFilter>() is MeshFilter mf) mesh = mf.sharedMesh;

            int count = mesh != null ? mesh.subMeshCount : 0;
            if (count != _submeshChoiceCount)
            {
                var choices = new List<string> { "すべて" };
                var materials = _state.Source != null ? _state.Source.sharedMaterials : Array.Empty<Material>();
                for (int i = 0; i < count; i++)
                {
                    string matName = i < materials.Length && materials[i] != null ? materials[i].name : "(no material)";
                    choices.Add($"{i}: {matName}");
                }
                _submeshDropdown.choices = choices;
                _submeshChoiceCount = count;
            }

            int selected = _state.SourceSubmesh < 0 ? 0 : _state.SourceSubmesh + 1;
            if (selected >= _submeshDropdown.choices.Count) selected = 0;
            _submeshDropdown.SetValueWithoutNotify(_submeshDropdown.choices[selected]);
            _submeshDropdown.SetEnabled(count > 1);
        }

        private string DescribeSource()
        {
            if (_state.Source == null) return "切り出し元の Renderer (SkinnedMeshRenderer / MeshRenderer) を指定してください。";
            if (_state.TopologyError != null) return _state.TopologyError;
            if (_state.Topology == null) return "解析中...";

            var t = _state.Topology;
            return $"三角形 {t.Triangles.Length} / UVアイランド {t.UvIslandCount} / 連結ポリゴン {t.PolyGroupCount}"
                   + (t.HasUv ? "" : " (UV0 なし)");
        }

        private string DescribeSelection()
        {
            if (_state.Topology == null) return "未解析";
            if (_state.Selection.Count == 0) return "未選択";

            int triangles = _state.Topology.CountTriangles(_state.PickMode, _state.Selection);
            string unit = _state.PickMode == MmPickMode.UvIsland ? "アイランド" : "グループ";
            return $"{_state.Selection.Count} {unit} / {triangles} 三角形を選択中";
        }

        private Toggle BindToggle(VisualElement root, string name, Func<bool, IMmAction> factory)
        {
            var toggle = root.Q<Toggle>(name);
            if (toggle != null) toggle.RegisterValueChangedCallback(evt => Dispatch(factory(evt.newValue)));
            return toggle;
        }

        private string DescribePolicy()
        {
            if (_state.ComponentPolicy == MmComponentPolicy.MeshDependenciesOnly)
            {
                return "Renderer / PhysBone / PhysBoneCollider / Constraint だけを残します (Module Creator 相当)。";
            }
            return "切り出し対象以外の Renderer と不要な PhysBone 以外は、全てのコンポーネントを残します。";
        }

        private static string DescribeResult(ModularizeResult result)
        {
            string text = $"パーツを出力しました: {result.PrefabPath}\n"
                          + $"△{result.TriangleCount} / 頂点{result.VertexCount} / 複製範囲 {result.ScopeRootName}\n"
                          + $"除去: オブジェクト {result.RemovedObjectCount} / コンポーネント {result.RemovedComponentCount}"
                          + $" (うち不要な PhysBone {result.PurgedPhysBoneCount})";

            foreach (var note in result.Notes) text += "\n" + note;
            return text;
        }

        private static void SetButtonActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList("dennoko-button-active");
            else btn.RemoveFromClassList("dennoko-button-active");
        }
    }
}
