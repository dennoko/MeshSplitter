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
        public int SourceUvChannel = 0; // 0: UV0, 1: UV1, ...

        public MeshTopology Topology;
        public string TopologyError;

        public MmPickMode PickMode = MmPickMode.ConnectedPolygon;
        public MmSelectionMode SelectionMode = MmSelectionMode.Add;
        public HashSet<int> Selection = new HashSet<int>();

        public string PartName = "MS_Part_part";
        public string OutputFolder = MmPaths.DefaultOutputFolder;

        // コンポーネント・メッシュの内部設定 (スマート維持)
        // 切り出し対象から辿れるものだけを残すため、これらは「辿る種別」の許可設定にあたる。
        public bool KeepPhysBones = true;              // メッシュに効かない PhysBone は常に除去される
        public bool KeepPhysBoneColliders = true;
        public bool KeepConstraints = true;
        public bool KeepBlendShapes = true;
        public bool TrimUnusedBones = true;            // デフォルト: ウェイトの無い親以外のボーンを除去
        public bool RecalculateBounds = true;
        public bool AutoInstantiate = true;

        // シーン連携
        public bool SceneSelectionEnabled = true;
        public bool SceneOverlayEnabled = true;
        public bool SceneOverlayXray = false;

        public string LastError;
        public string LastMessage;

        public MmState Clone()
        {
            return new MmState
            {
                Source = Source,
                SourceSubmesh = SourceSubmesh,
                SourceUvChannel = SourceUvChannel,
                Topology = Topology,
                TopologyError = TopologyError,
                PickMode = PickMode,
                SelectionMode = SelectionMode,
                Selection = new HashSet<int>(Selection),
                PartName = PartName,
                OutputFolder = OutputFolder,
                KeepPhysBones = KeepPhysBones,
                KeepPhysBoneColliders = KeepPhysBoneColliders,
                KeepConstraints = KeepConstraints,
                KeepBlendShapes = KeepBlendShapes,
                TrimUnusedBones = TrimUnusedBones,
                RecalculateBounds = RecalculateBounds,
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
