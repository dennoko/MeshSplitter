using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.MeshModularizer
{
    public interface IMmAction { }

    public sealed class SetSource : IMmAction
    {
        public Renderer Source { get; }
        public SetSource(Renderer source) => Source = source;
    }

    public sealed class SetSourceSubmesh : IMmAction
    {
        public int SubmeshIndex { get; }
        public SetSourceSubmesh(int submeshIndex) => SubmeshIndex = submeshIndex;
    }

    public sealed class SetPickMode : IMmAction
    {
        public MmPickMode Mode { get; }
        public SetPickMode(MmPickMode mode) => Mode = mode;
    }

    public sealed class SetSelectionMode : IMmAction
    {
        public MmSelectionMode Mode { get; }
        public SetSelectionMode(MmSelectionMode mode) => Mode = mode;
    }

    public sealed class ModifySelection : IMmAction
    {
        public IReadOnlyCollection<int> Groups { get; }
        public bool Add { get; }
        public ModifySelection(IReadOnlyCollection<int> groups, bool add)
        {
            Groups = groups;
            Add = add;
        }
    }

    public sealed class SelectAllGroups : IMmAction { }
    public sealed class ClearSelection : IMmAction { }
    public sealed class InvertSelection : IMmAction { }

    public sealed class SetPartName : IMmAction
    {
        public string Value { get; }
        public SetPartName(string value) => Value = value;
    }

    public sealed class SetOutputFolder : IMmAction
    {
        public string Value { get; }
        public SetOutputFolder(string value) => Value = value;
    }

    public sealed class ToggleSceneSelection : IMmAction { }
    public sealed class SetSceneOverlayEnabled : IMmAction
    {
        public bool Value { get; }
        public SetSceneOverlayEnabled(bool value) => Value = value;
    }

    public sealed class SetSceneOverlayXray : IMmAction
    {
        public bool Value { get; }
        public SetSceneOverlayXray(bool value) => Value = value;
    }

    public sealed class CmdAnalyzeSource : IMmAction { }
    public sealed class CmdPickSourceFromSelection : IMmAction { }
    public sealed class CmdExtractPart : IMmAction { }
    public sealed class CmdExtractPerSubmesh : IMmAction { }
}
