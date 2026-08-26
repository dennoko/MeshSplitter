using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    [Serializable]
    public sealed class MmState
    {
        public Renderer Source;
        public int SourceSubmesh = -1; // -1: 全て

        public MeshTopology Topology;
        public string TopologyError;

        public MmPickMode PickMode = MmPickMode.UvIsland;
        public MmSelectionMode SelectionMode = MmSelectionMode.Add;
        public HashSet<int> Selection = new HashSet<int>();

        public string PartName = "MeshPart";
        public string OutputFolder = MmPaths.DefaultOutputFolder;

        // コンポーネントの維持方針
        public MmComponentPolicy ComponentPolicy = MmComponentPolicy.KeepAll;
        public bool RemoveOtherRenderers = true;       // 切り出し対象以外の Renderer を除去
        public bool KeepPhysBones = true;              // 不要な PhysBone は方針によらず常に除去される
        public bool KeepConstraints = true;

        // メッシュ設定
        public bool KeepBlendShapes = true;
        public bool TrimUnusedBones = false;           // デフォルト: ウェイトの無いボーンも残す
        public bool RecalculateBounds = false;         // デフォルト: 元のBoundsをそのまま維持

        // 出力設定
        public bool AddBoneProxy = false;
        public HumanBodyBones BoneProxyTarget = HumanBodyBones.Head;
        public bool AutoInstantiate = true;

        // シーン連携
        public bool SceneSelectionEnabled = true;
        public bool SceneOverlayEnabled = true;
        public bool SceneOverlayXray = true;

        public string LastError;
        public string LastMessage;

        public MmState Clone()
        {
            return new MmState
            {
                Source = Source,
                SourceSubmesh = SourceSubmesh,
                Topology = Topology,
                TopologyError = TopologyError,
                PickMode = PickMode,
                SelectionMode = SelectionMode,
                Selection = new HashSet<int>(Selection),
                PartName = PartName,
                OutputFolder = OutputFolder,
                ComponentPolicy = ComponentPolicy,
                RemoveOtherRenderers = RemoveOtherRenderers,
                KeepPhysBones = KeepPhysBones,
                KeepConstraints = KeepConstraints,
                KeepBlendShapes = KeepBlendShapes,
                TrimUnusedBones = TrimUnusedBones,
                RecalculateBounds = RecalculateBounds,
                AddBoneProxy = AddBoneProxy,
                BoneProxyTarget = BoneProxyTarget,
                AutoInstantiate = AutoInstantiate,
                SceneSelectionEnabled = SceneSelectionEnabled,
                SceneOverlayEnabled = SceneOverlayEnabled,
                SceneOverlayXray = SceneOverlayXray,
                LastError = LastError,
                LastMessage = LastMessage
            };
        }
    }
}
