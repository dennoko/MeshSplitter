# Mesh Splitter

A mesh splitting and Prefab separation tool for Unity and VRChat.
Easily select and isolate any part of a mesh (costumes, accessories, hair, body parts, etc.) into a standalone
Prefab and Mesh asset that accurately preserves original transforms, hierarchies, bounds, and necessary components (PhysBones, Constraints, etc.).

Original meshes and avatar assets are never modified.

---

## Quick Start (3 Steps to Split)

### Step 1: Open the Tool & Assign Target Mesh
1. Open `dennokoworks > Mesh Splitter` from the top menu bar.
2. Select the object in the Hierarchy and click **"From Selection"** (or drag & drop the Renderer directly into the "Target Mesh" field).

### Step 2: Select the Mesh Region
1. Choose a selection unit mode:
   - **UV Island**: Select by continuous UV islands.
   - **Connected Polygons**: Select by 3D connected polygon clusters.
2. Select polygons directly in the **UV Preview** (left-drag to marquee select, right-drag to pan, scroll wheel to zoom) or in the **3D Scene View** (click / drag directly on the mesh).

### Step 3: Export as Prefab
1. Enter your desired name in **"Prefab Name"**.
2. Click **"Export Selection as Part Prefab"**.
3. A new Mesh asset and standalone Prefab are generated in the specified folder and automatically placed at the exact original position in your scene.

---

## Screen Layout & Features Reference

### 1. Top Bar
- **Version Display**: Displays current version and highlights when an update is available.
- **↻ (Recheck Button)**: Manually checks for new releases on GitHub.
- **Language Toggle (`EN` / `JA`)**: Switches the UI display language between English and Japanese (preferences are saved).

### 2. Target Mesh
Section to configure the source mesh.
- **Target Mesh**: Specify the target SkinnedMeshRenderer or MeshRenderer to split from.
- **From Selection**: Automatically assigns the Renderer currently selected in the Scene / Hierarchy.
- **↻ (Reload)**: Re-analyzes mesh topology and submesh information.
- **Select Submesh**: Limits selection to a specific submesh/material index.
- **UV Channel (`UV0` / `UV1` ...)**: Chooses which UV channel is used to detect UV islands. Only the channels the mesh actually has are listed (`UV0` is fine in most cases).

### 3. Range Selection
Section to choose which polygons to extract.
- **UV Island**: Selects mesh portions grouped by UV islands.
- **Connected Polygons**: Selects portions connected in 3D mesh space.
- **Select All / Clear / Invert**: Batch operations on the current selection.
- **UV Preview**: Inspect and marquee select on the 2D UV layout. The gray outline marks the 0-1 UV range.
- **Main Texture Display**: When a **submesh is selected** and the **UV channel is `UV0`**, the main texture of that submesh's material is drawn behind the UV layout, so you can see which part of the texture your selection covers.
  - Works with materials that read their main texture (`_MainTex`) from UV0, such as lilToon, Poiyomi and the Unity standard shaders. Tiling and offset are applied, but the texture is drawn only once rather than repeated.
  - The texture is not shown when: the submesh is set to **All**, a channel of `UV1` or later is selected, or a Poiyomi material has its main texture UV set to anything other than `UV0`.
  - UV animation settings such as scrolling and rotation are not reflected.
- **Scene Click Selection (ON/OFF)**: Enables direct clicking and painting on the mesh in the 3D Scene view (Press **Esc** to disable).
- **Wireframe X-Ray**: Enables see-through wireframe rendering through the object for better visibility.

### 4. Create Prefab
Section to export the selected mesh as new assets.
- **Prefab Name**: Name of the generated Prefab and Mesh asset.
- **Output Folder**: Destination folder in your project (default: `Assets/MS_splitted_mesh`).
- **Export Selection as Part Prefab**: Exports the selected polygons as a single standalone Prefab and Mesh asset.
- **Batch Export All Submeshes as Individual Prefabs**: Exports all submeshes into separate individual Prefabs in one click.

---

## Key Highlights & Design Safety

- **Zero Alignment Offset**: Original Transform values, bone hierarchies, and localBounds are precisely preserved, so dropping the exported Prefab under its original parent aligns it perfectly.
- **Automatic Component Filtering**: Automatically keeps only the bones, PhysBones, PhysBoneColliders, and Constraints relevant to the extracted mesh. Unrelated avatar components (such as VRC Avatar Descriptor or Animator) will never be mixed in.
- **Non-Destructive**: Never modifies original avatars or source mesh assets.
- **Supports Both Skinned and Static Meshes**: Works seamlessly with SkinnedMeshRenderers (clothes, hair, body) and static MeshRenderers (accessories, props).
