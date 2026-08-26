# Mesh Modularizer

A general-purpose mesh splitting and prefab isolation tool for Unity and VRChat avatars.
Easily select and isolate any part of a mesh (costumes, accessories, body parts, etc.) into a standalone
Prefab and Mesh asset while preserving the original transforms, hierarchies, bounds, **and components**.

---

## How It Works: Subtractive Extraction

The tool **duplicates the Prefab that owns the source mesh and then strips away what is no longer needed**.

1. Build a new Mesh asset from the selected polygons.
2. Duplicate the **Prefab that owns the source mesh** — the nearest prefab instance root, not the whole avatar.
3. Assign the extracted mesh to the duplicated Renderer.
4. Delete the objects and components that are no longer needed.
5. Save the result as a Prefab.

Because the hierarchy is duplicated rather than rebuilt, transform values, renderer settings, and every
component's values and references survive without any copy-and-remap step.

> The scope is widened automatically — to the smallest common ancestor that contains them — only when
> bones live outside that Prefab. The window reports it when this happens.

---

## Key Features

1. **Hierarchy & Transform Preservation**:
   - Preserves original local Transforms, hierarchy structure, rootBone, bones, and localBounds.
   - Dropping the prefab under the original parent aligns it perfectly with no offset.

2. **Selectable Component Policy**:
   - **Keep All**: everything survives except renderers other than the target and PhysBones that are no
     longer needed — VRC Contacts, Constraints, Modular Avatar components and custom scripts included.
   - **Mesh Dependencies Only** (Module Creator equivalent): keeps only Renderer / MeshFilter /
     PhysBone / PhysBoneCollider / Constraint and removes everything else.

3. **Automatic PhysBone Purging**:
   - Under either policy, PhysBones that no longer affect any weighted bone of the extracted mesh are removed.
   - The decision uses the bones that actually carry weight, so it works even when the bone hierarchy is kept intact.
   - PhysBoneColliders referenced by a surviving PhysBone are kept automatically by following references.

4. **Reference Tracking**:
   - Transitively follows the Transform/component references held by surviving components (constraint
     sources included) so nothing they point at gets deleted.
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
   - **Modular Avatar Bone Proxy**: automatically attach an MA Bone Proxy.
   - **Batch Submesh Extraction**: split all submeshes into individual prefabs in one click.

---

## How to Use

1. Open `Window > dennokoworks > Mesh Modularizer` (or `Tools > dennokoworks > Mesh Modularizer`).
2. Assign the target Renderer (SkinnedMeshRenderer or MeshRenderer). It must be an object placed in a
   scene — Prefab assets cannot be processed directly.
3. Select the desired mesh region in the UV Preview or SceneView.
4. Pick a component policy under **コンポーネントの維持** (Component Retention).
5. Configure the part name and output folder.
6. Click **"選択範囲をパーツPrefabとして書き出し" (Extract Part Prefab)** to generate the assets.

---

## Notes

- The generated Prefab is a standalone Prefab, fully unpacked from the source Prefab (not a Variant).
- Even under "Keep All", renderers other than the extraction target are removed by default, since the
  point is to isolate a single part.
- References that pointed outside the duplicated scope (for example a Probe Anchor or constraint source
  on the avatar's armature) are lost when the Prefab is saved. The count is shown in the window.
- The original objects and mesh assets are never modified.
