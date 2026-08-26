# Mesh Modularizer

A general-purpose mesh splitting and prefab isolation tool for Unity and VRChat avatars.
Easily select and isolate any part of a mesh (costumes, accessories, body parts, etc.) into a standalone
Prefab and Mesh asset that preserves the original transforms, hierarchies and bounds while carrying
**only the components that part actually needs**.

---

## How It Works: Subtractive Extraction

The tool **duplicates the Prefab that owns the source mesh and then strips away what is no longer needed**.

1. Build a new Mesh asset from the selected polygons.
2. Duplicate the **Prefab that owns the source mesh** — the nearest prefab instance root, not the whole avatar.
3. Assign the extracted mesh to the duplicated Renderer.
4. Keep only what the extracted mesh needs and delete every other object and component.
5. Save the result as a Prefab.

Because the hierarchy is duplicated rather than rebuilt, transform values, renderer settings, and the
surviving components' values and references carry over without any copy-and-remap step.

> The scope is widened automatically — to the smallest common ancestor that contains them — only when
> bones live outside that Prefab. The window reports it when this happens.

### What Survives (Whitelist)

What is kept is decided by a whitelist modelled on
[Module Creator](https://github.com/Tliks/ModuleCreator). The only seeds are the extracted Renderer and
its bones; from there the tool follows **only the component kinds that affect how the mesh looks**.

| Kept | Condition |
|---|---|
| The extracted Renderer / MeshFilter | Always |
| bones / rootBone / probeAnchor | Whatever the Renderer references |
| PhysBone | Only when it moves a bone that carries weight on the extracted mesh (plus the single-child chain below it) |
| PhysBoneCollider | Only when a surviving PhysBone references it |
| Constraint | Only when it drives a surviving bone or one of its descendants (plus the path to its sources) |
| Parents of the above | As intermediate nodes that preserve the hierarchy (they carry no components) |

Arbitrary component references are never followed transitively, so **VRC Avatar Descriptor, Animator,
Pipeline Manager, Modular Avatar components and other avatar-wide scripts never end up in the extracted
Prefab.**

---

## Key Features

1. **Hierarchy & Transform Preservation**:
   - Preserves original local Transforms, hierarchy structure, rootBone, bones, and localBounds.
   - Dropping the prefab under the original parent aligns it perfectly with no offset.

2. **Only the Necessary Components**:
   - Avatar-wide components (VRC Avatar Descriptor and friends) and renderers other than the target
     never survive.
   - Only the PhysBones, PhysBoneColliders and Constraints that affect the extracted mesh are tracked
     and kept.

3. **Automatic PhysBone Purging**:
   - PhysBones that no longer affect any weighted bone of the extracted mesh are removed.
   - The decision uses the bones that actually carry weight, so it works even when the bone hierarchy is kept intact.
   - PhysBoneColliders referenced by a surviving PhysBone are kept automatically.

4. **Constraint Tracking**:
   - Constraints that drive a surviving bone are kept along with the path to their source transforms,
     resolved repeatedly until no new dependency appears.
   - Reports how many references pointed outside the duplicated scope.

5. **Supports SkinnedMeshRenderer & MeshRenderer**:
   - Works with skinned meshes (clothes, hair, body) and static meshes (accessories, props).

6. **Intuitive Selection Modes**:
   - **UV Island Selection**: select by continuous UV islands.
   - **Connected Polygon Selection**: select by 3D connected mesh clusters.
   - **UV Preview**: interactive UV viewer supporting zoom, pan, and marquee drag selection.
   - **SceneView Overlay**: click and drag directly on the mesh in the 3D scene view.

7. **Optional Optimization**:
   - **Trim Unused Bones**: remove unweighted bones from both the bones array and the hierarchy.
   - **Bounds Recalculation**: recalculate tight localBounds considering skinning and BlendShapes.
   - **Batch Submesh Extraction**: split all submeshes into individual prefabs in one click.

---

## How to Use

1. Open `Window > dennokoworks > Mesh Modularizer` (or `Tools > dennokoworks > Mesh Modularizer`).
2. Assign the target Renderer (SkinnedMeshRenderer or MeshRenderer). It must be an object placed in a
   scene — Prefab assets cannot be processed directly.
3. Select the desired mesh region in the UV Preview or SceneView.
4. Configure the part name and output folder.
5. Click **"選択範囲をパーツPrefabとして書き出し" (Extract Part Prefab)** to generate the assets.

---

## Notes

- The generated Prefab is a standalone Prefab, fully unpacked from the source Prefab (not a Variant).
- References that pointed outside the duplicated scope (for example a Probe Anchor or constraint source
  on the avatar's armature) are lost when the Prefab is saved. The count is shown in the window.
- Avatar-wide components (VRC Avatar Descriptor, Animator, Modular Avatar, …) are not carried over.
  Add them to the generated Prefab by hand if you need them.
- The original objects and mesh assets are never modified.
