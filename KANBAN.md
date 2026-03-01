# CMF Planner v1.0 — Master Kanban Board
> AI-Readable Project Document | Last updated: February 2026

---

## PROJECT OVERVIEW

| Field | Value |
|-------|-------|
| Project Name | CMF Planner |
| Version | 1.0 |
| Language | C# .NET 8 |
| UI Framework | WPF (Windows Presentation Foundation) |
| Plugin System | MEF (Managed Extensibility Framework) |
| Key Libraries | VTK.NET, fo-dicom, Helix Toolkit, MathNet.Numerics |
| Reference Software | IPS KLS Martin Case Designer, Nemo Studio |
| License | MIT (open source) |
| IDE | Visual Studio 2022 |
| GitHub | https://github.com/fdrlnz/CMFPlanner_C- |

**Clinical purpose:** Virtual Surgical Planning (VSP) for orthognathic surgery (LeFort I, BSSO, Genioplasty and custom multipart osteotomies).

**Full workflow — user-visible steps (in order):**
1. Import DICOM (CT or CBCT) + triplanar viewer
2. Align dental casts (STL arches onto CT model)
3. Orient model in Natural Head Position (NHP)
4. Plan osteotomies *(segmentation + skull/mandible separation run automatically on entry)*
5. Move bone segments in 3D + real-time soft tissue simulation
6. Generate surgical splints (intermediate + final)
7. Export STL files and PDF report

> **Note on segmentation and segment identification (Epics 3 & 4):** These algorithms still exist in code and are executed automatically (or semi-automatically) when the user enters the Osteotomies step. They are NOT shown as separate steps in the left workflow panel.

---

## HOW TO USE THIS FILE (FOR AI AGENTS)

1. Read this entire file at the start of every session.
2. Find the **first task with status `[ ] TODO`** whose dependencies are all `[x] DONE`.
3. Read its full card (Goal, Technical Details, Inputs, Outputs, Acceptance Criteria).
4. Implement it in C# .NET 8 using VTK.NET, fo-dicom, WPF.
5. Verify against Acceptance Criteria.
6. Mark the task `[x] DONE` and update the Progress Table.
7. Commit to GitHub: `"E{epic}-T{task}: {title}"` (e.g. `"E1-T1: Create GitHub repository"`).
8. Move to the next incomplete task.

**Status legend:**
- `[ ]` TODO — not started
- `[~]` IN PROGRESS — currently being worked on
- `[x]` DONE — completed and verified
- `[!]` BLOCKED — waiting for a dependency

---

## PROGRESS TRACKING

| Epic | Title | Status | Done | Total |
|------|-------|--------|------|-------|
| 1 | Project Setup | DONE | 5 | 5 |
| 2 | DICOM Import | DONE | 6 | 6 |
| 3 | Segmentation | IN PROGRESS | 2 | 3 |
| 4 | Anatomical Segments | TODO | 0 | 3 |
| 5 | Dental Cast Superimposition | TODO | 0 | 9 |
| 6 | Natural Head Position | TODO | 0 | 5 |
| 7 | Osteotomy Planning | TODO | 0 | 5 |
| 8 | Segment Movement + Soft Tissue | TODO | 0 | 4 |
| 9 | Splint Generation | TODO | 0 | 4 |
| 10 | User Interface | TODO | 0 | 4 |
| 11 | Export and Reporting | TODO | 0 | 3 |
| **TOTAL** | | | **13** | **51** |

---

---

# EPIC 1 — PROJECT SETUP

**Goal:** Create the project repository, folder structure, development environment, plugin architecture, and main UI shell so all subsequent epics have a working foundation.

---

### E1-T1 — Create GitHub Repository
- **Status:** [x] DONE
- **Goal:** Create a public GitHub repository named CMF-Planner.
- **Result:** https://github.com/fdrlnz/CMFPlanner_C-
- **Acceptance Criteria:** ✓. Repository publicly accessible.

---

### E1-T2 — Define Project Folder Structure
- **Status:** [x] DONE
- **Goal:** Create a clean, scalable folder structure for the C# solution.
- **Technical Details:**
  ```
  CMFPlanner/
  ├── CMFPlanner.slnx
  ├── src/
  │   ├── CMFPlanner.Core/
  │   ├── CMFPlanner.UI/
  │   ├── CMFPlanner.Dicom/
  │   ├── CMFPlanner.Segmentation/
  │   ├── CMFPlanner.Visualization/
  │   ├── CMFPlanner.Planning/
  │   ├── CMFPlanner.Splint/
  │   └── CMFPlanner.Plugins/
  ├── plugins/
  ├── tests/
  ├── docs/
  └── assets/
  ```
  Also create: `README.md`, `.gitignore` (C#/Visual Studio), `LICENSE` (MIT), `CONTRIBUTING.md`.
- **Inputs:** GitHub repository (E1-T1)
- **Outputs:** Full folder structure committed to repository
- **Dependencies:** E1-T1
- **Acceptance Criteria:** All folders exist; solution file opens in Visual Studio 2022 without errors.

---

### E1-T3 — Configure .NET 8 Projects and Install NuGet Packages
- **Status:** [x] DONE
- **Goal:** Initialize all C# projects with .NET 8 target and install all required NuGet packages.
- **Technical Details:**
  - Target framework: `net8.0-windows` (WPF requires Windows)
  - NuGet packages per project:
    - `CMFPlanner.Dicom` → `fo-dicom` (v5+)
    - `CMFPlanner.Visualization` → `Kitware.VTK`
    - `CMFPlanner.UI` → `Helix Toolkit WPF`, `MaterialDesignThemes`
    - `CMFPlanner.Core` → `MathNet.Numerics`, `System.Composition`
    - All projects → `Microsoft.Extensions.DependencyInjection`
  - Set up dependency injection container in `App.xaml.cs`
- **Inputs:** Folder structure (E1-T2)
- **Outputs:** Working .NET 8 solution that compiles without errors
- **Dependencies:** E1-T2
- **Acceptance Criteria:** `dotnet build` runs without errors; all NuGet packages resolve.

---

### E1-T4 — Configure Plugin Architecture (MEF)
- **Status:** [x] DONE
- **Goal:** Set up MEF-based plugin system so third-party developers can add modules without modifying core code.
- **Technical Details:**
  - Define interfaces in `CMFPlanner.Plugins`:
    - `IVisualizationPlugin`, `ISegmentationPlugin`, `IPlanningPlugin`, `IExportPlugin`
  - Each interface exposes: `Name`, `Version`, `Author`, `Description`
  - MEF discovers plugins from `/plugins` directory at runtime via `System.Composition` (MEF2)
  - Each plugin ships with a `plugin.json` metadata file
  - Write `PluginManager` class that scans, loads, and validates plugins
- **Result:** Interfaces + `PluginManager` in `CMFPlanner.Plugins`; `SampleVisualizationPlugin` auto-deploys to output `plugins/` dir on build; `PluginManager` wired into DI in `App.xaml.cs`.
- **Inputs:** Configured project (E1-T3)
- **Outputs:** Working plugin loader; sample plugin interface definitions
- **Dependencies:** E1-T3
- **Acceptance Criteria:** A test plugin placed in `/plugins` loads at runtime without recompiling core.

---

### E1-T5 — Create Main Application Window and UI Shell
- **Status:** [x] DONE
- **Goal:** Build the main WPF window with dark theme, toolbar, panels, and 3D viewport placeholder.
- **Technical Details:**
  - Dark theme via `MaterialDesignThemes` (BaseDark)
  - Colors: background `#1E1E1E`, panels `#252526`, accent `#0078D4`
  - Layout:
    - **Top:** MenuBar + Toolbar (NHP, Soft Tissue, Bite Reference buttons — always visible)
    - **Left:** Workflow step panel (numbered steps 1–9)
    - **Center:** 3D viewport (`HelixViewport3D` placeholder)
    - **Right:** Context-sensitive properties panel
    - **Bottom:** Status bar (active tool, measurements, errors)
  - All panels resizable via `GridSplitter`
- **Result:** MaterialDesignThemes BaseDark loaded in App.xaml; full shell with menu bar, toolbar (persistent NHP/SoftTissue/BiteRef toggles), left workflow panel (9 steps, code-behind), HelixViewport3D center, right properties panel, status bar; all panels GridSplitter-resizable; 0 build errors.
- **Inputs:** Configured project (E1-T3)
- **Outputs:** Compilable WPF app with dark-themed shell window
- **Dependencies:** E1-T3
- **Acceptance Criteria:** App launches; dark theme visible; all panels present; no console errors.

---
---

# EPIC 2 — DICOM IMPORT

**Goal:** Allow the user to load a CT or CBCT DICOM series from disk and reconstruct a 3D volume for display in the viewport.

---

### E2-T1 — DICOM Folder Loader
- **Status:** [x] DONE
- **Goal:** Implement UI and logic to select a folder of DICOM files and load the series.
- **Technical Details:**
  - "Open DICOM Series" button in toolbar and File menu
  - `FolderBrowserDialog` to select folder
  - Use fo-dicom to scan `.dcm` files, sort by `InstanceNumber` or `SliceLocation`
  - Validate same `SeriesInstanceUID` across all files
  - Show loading progress bar
  - Display patient name, study date, modality in status bar
  - Store in `DicomVolume` model class in `CMFPlanner.Core`
- **Inputs:** Folder path from user
- **Outputs:** `DicomVolume` object (ordered slices, voxel spacing, dimensions)
- **Dependencies:** E1-T5
- **Acceptance Criteria:** Real CT folder loads; patient metadata displays; no crash on malformed files.

---

### E2-T1b — DICOM Triplanar Viewer
- **Status:** [x] DONE
- **Goal:** Display the loaded DICOM series as three synchronized 2D slice views (axial, coronal, sagittal) in the main viewport immediately after loading.
- **Technical Details:**
  - Three views shown simultaneously in a 2×2 grid layout (axial top-left, coronal top-right, sagittal bottom-left; 3D viewport bottom-right placeholder)
  - Each 2D view renders a single interpolated slice using `WriteableBitmap` or a lightweight WPF `Image` backed by a raster render of the HU buffer
  - **Scroll to navigate:** mouse wheel in any view scrolls through slices in that axis
  - **Crosshair synchronization:** scrolling or clicking in any view updates the crosshair position in all three views in real time
  - **Double-click to expand:** double-clicking any 2D view or the 3D viewport expands it to fill the full viewport area; double-clicking again returns to triplanar grid layout
  - **Window/Level:** right-click drag adjusts Window (width) and Level (center) — defaults: W=400 L=40 (soft tissue preset); bone preset W=2000 L=400; user can toggle presets
  - Crosshair lines drawn as thin colored overlays (axial=blue, coronal=green, sagittal=red)
  - HU value under cursor displayed in status bar
- **Inputs:** `DicomVolume` (E2-T1) — metadata and sorted slice file paths
- **Outputs:** Interactive triplanar viewer active in main viewport; user can inspect all axes before 3D reconstruction
- **Dependencies:** E2-T1
- **Acceptance Criteria:** All three views display and synchronize; scrolling updates crosshair in other views; double-click expand/collapse works; W/L adjustment works.

---

### E2-T2 — 3D Volume Reconstruction from DICOM
- **Status:** [x] DONE
- **Goal:** Convert ordered DICOM slices into a 3D `vtkImageData` volume.
- **Technical Details:**
  - Apply `RescaleSlope` and `RescaleIntercept` to convert to Hounsfield Units (HU)
  - Build `vtkImageData` with correct spacing from `PixelSpacing` + `SliceThickness` tags
  - HU ranges reference: bone 400–1800 HU, soft tissue 0–400 HU
- **Inputs:** `DicomVolume` (E2-T1)
- **Outputs:** `VolumeData` (HU buffer) stored in session state
- **Dependencies:** E2-T1, E2-T1b
- **Acceptance Criteria:** Volume has correct dimensions and HU values; voxel spacing matches DICOM metadata.

---

### E2-T3 — Basic 3D Volume Renderer
- **Status:** [x] DONE
- **Goal:** Display the loaded DICOM volume in the 3D viewport using volume rendering.
- **Technical Details:**
  - Use `vtkSmartVolumeMapper`
  - Default transfer function: bone white/grey, soft tissue semi-transparent
  - Mouse controls: left-click rotate, right-click zoom, middle-click pan
  - Touchpad controls: 3-finger drag to rotate, 2-finger drag to translate
  - Add `vtkOrientationMarkerWidget` (axes indicator, bottom-left)
  - Optional axial/coronal/sagittal slice overlays
  - Camera reset button
- **Inputs:** `vtkImageData` (E2-T2)
- **Outputs:** Interactive 3D rendered volume in viewport
- **Dependencies:** E2-T2
- **Acceptance Criteria:** CT scan visible in 3D; user can rotate/zoom/pan; bone clearly visible.

---

### E2-T4 — Support for CT and CBCT Modalities
- **Status:** [x] DONE
- **Goal:** Handle differences between standard CT and cone-beam CT file formats.
- **Technical Details:**
  - Detect modality from DICOM tag `(0008,0060)`
  - CBCT fallback: sort by `ImagePositionPatient` Z coordinate if `InstanceNumber` missing
  - Test with at least one CT and one CBCT dataset
- **Result:** `DicomLoader` now extracts `Modality` tag and computes a geometry sort key (`N·IPP` — slice-normal projected onto Image Position Patient) as the primary sort key, handling both CT and CBCT regardless of whether `InstanceNumber` is present. Full fallback chain: GeometrySortKey → SliceLocation → InstanceNumber → FilePath. `ComputeSpacingZ` uses median geometry gaps → `SpacingBetweenSlices` → `SliceThickness` → 1.0. `DicomVolume` now carries `Modality`, `SpacingZ`, and `OriginX/Y/Z`. 24 unit tests covering sort fallback and HU conversion edge cases.
- **Inputs:** `DicomVolume` (E2-T1)
- **Outputs:** Correctly loaded volume regardless of CT/CBCT source
- **Dependencies:** E2-T1
- **Acceptance Criteria:** Both CT and CBCT datasets load and render correctly.

---

### E2-T5 — Metric Reference Grid
- **Status:** [x] DONE
- **Goal:** Display a 3D metric reference grid in the background of the 3D viewport to give the surgeon spatial scale context.
- **Technical Details:**
  - Grid is rendered on the XZ plane (floor) behind/below the model using WPF `MeshGeometry3D` or Helix Toolkit grid helper
  - **Adaptive resolution:** 10mm grid at default zoom; switches to 5mm grid when zooming in (threshold: ~50mm visible range); switches to 1mm grid when zooming in on detail (~10mm visible range)
  - Grid lines drawn as thin grey lines (`RGB(80,80,80)`) with every 10th line slightly brighter (`RGB(120,120,120)`) as a major gridline
  - **Always visible by default** when 3D viewport is active
  - **"Grid" toggle button** in the top-right corner of the 3D viewport overlay (small icon button, similar to the existing "Reset Camera" button) — clicking shows/hides grid without affecting the model
  - Grid updates its visible extent and cell size dynamically as the user zooms/pans so it always fills the viewport background
- **Inputs:** 3D viewport active with `HelixViewport3D` (E2-T3)
- **Outputs:** Adaptive metric grid rendered in 3D viewport background; toggle button in viewport corner
- **Dependencies:** E2-T3
- **Acceptance Criteria:** Grid visible at all standard zoom levels; cell size adapts correctly (10mm/5mm/1mm); toggle button shows/hides grid instantly; grid does not obscure the bone mesh.

---

---

# EPIC 3 — SEGMENTATION

**Goal:** Separate hard tissue (bone) and soft tissue from the DICOM volume using adjustable HU thresholds, with live preview and manual refinement.

---

### E3-T1 — Threshold-Based Auto-Segmentation with Live Slider
- **Status:** [x] DONE
- **Goal:** Real-time threshold slider showing bone and soft tissue segmentation as user adjusts it.
- **Technical Details:**
  - Two sliders in right panel (and live threshold slider in 3D preview):
    - Bone threshold: default 350–1000 HU
    - Soft tissue threshold: default -100 to 400 HU
  - Update `vtkMarchingCubes` iso-surface in real time (300ms debounce)
  - Bone = semi-transparent white mesh; Soft tissue = semi-transparent skin-tone mesh
  - Use `vtkDecimatePro` for real-time performance
- **Result:** `ISegmentationService` / `SegmentationService` in CMFPlanner.Segmentation wrapping `IMeshExtractor`. Two HU threshold sliders in right Properties panel (shown after DICOM loads). Bone (α=210, warm white) and soft tissue (α=60, skin tone) meshes extracted in parallel via `Task.WhenAll` with stride=3. 300ms debounce via `CancellationTokenSource` + `Task.Delay`. Correct WPF 3D render order: bone (opaque) before skin (transparent). Camera zoom-to-fit fires once on first load only. Status line shows vertex counts.
- **Inputs:** `VolumeData` (E2-T2 HU buffer)
- **Outputs:** Two `MeshData` objects: `BoneSurface`, `SoftTissueSurface` (preview quality, stride=3)
- **Dependencies:** E2-T3
- **Acceptance Criteria:** Moving slider updates 3D mesh within 300ms; bone and soft tissue visually distinct.

---

### E3-T2 — Final High-Resolution Segmentation on Confirmation
- **Status:** [x] DONE
- **Goal:** Generate full-resolution mesh when user confirms threshold.
- **Technical Details:**
  - "Apply Segmentation" button triggers full-resolution `vtkMarchingCubes`
  - Apply `vtkSmoothPolyDataFilter` (20 iterations)
  - Apply `vtkDecimatePro`: max 200,000 polygons (bone), 100,000 (soft tissue)
  - Show progress dialog (10–30 seconds expected)
- **Result:** `MeshProcessor` static class in `CMFPlanner.Visualization`: `Smooth()` (Taubin λ-μ, no adjacency list — pure triangle-accumulator pass, O(triangles)) + `Decimate()` (vertex clustering with 16-step binary search on cell size, deduplicates triangles). `ISegmentationService.ApplyFinalSegmentationAsync` runs 6-step pipeline (stride-1 MC → smooth bone → decimate bone ≤200k → stride-1 MC soft tissue → smooth ST → decimate ST ≤100k) reporting string progress. "Apply Segmentation" button in right panel: shows existing LoadingOverlay with step text, cancellable, switches to 3D view on completion. Final triangle counts shown in status line.
- **Inputs:** `VolumeData` + confirmed HU thresholds (E3-T1 sliders)
- **Outputs:** Final `BoneMesh` and `SoftTissueMesh` (MeshData), stored in MainWindow; pipeline handles smoothing+decimation internally
- **Dependencies:** E3-T1
- **Acceptance Criteria:** Final mesh is smooth, no holes, polygon count within limits.

---

### E3-T3 — Manual Segmentation Refinement (Brush/Eraser)
- **Status:** [ ] TODO
- **Goal:** Allow manual correction of segmentation errors using a brush tool on 2D slice views.
- **Technical Details:**
  - Add 2D axial slice viewer panel
  - Brush tool: paints voxels as bone or soft tissue; size 1–20 voxels radius
  - Eraser removes voxels from current mask
  - Changes reflected in 3D viewport (re-run marching cubes on modified region only)
  - Undo/redo stack (max 20 steps)
- **Inputs:** `vtkImageData`, current segmentation mask (E3-T2)
- **Outputs:** Refined `vtkPolyData` meshes
- **Dependencies:** E3-T2
- **Acceptance Criteria:** User can add/remove areas; 3D view updates in real time; undo works.

---
---

# EPIC 4 — ANATOMICAL SEGMENT IDENTIFICATION

**Goal:** Separate the bone mesh into skull, mandible, and teeth (visually distinct but anatomically attached to their arches, matching IPS Case Designer style).

---

### E4-T1 — Semi-Automatic Skull/Mandible Separation
- **Status:** [ ] TODO
- **Goal:** Separate mandible from skull as an independent 3D object.
- **Technical Details:**
  - User draws separation plane at condyle level OR clicks mandible for flood-fill via `vtkConnectivityFilter`
  - Results: `SkullMesh` and `MandibleMesh` (`vtkPolyData`)
  - If automatic fails, user adjusts cut plane manually
- **Inputs:** `BoneMesh` (E3-T2)
- **Outputs:** `SkullMesh` (vtkPolyData), `MandibleMesh` (vtkPolyData)
- **Dependencies:** E3-T2
- **Acceptance Criteria:** Two separate, complete 3D objects with no shared vertices.

---

### E4-T2 — Teeth Visual Identification (High HU Threshold)
- **Status:** [ ] TODO
- **Goal:** Identify teeth using high HU threshold and color them differently without separating them from arches.
- **Technical Details:**
  - Secondary segmentation at 1500+ HU to isolate enamel
  - Do NOT create separate mesh objects — apply per-cell scalar coloring instead
  - Colors: Skull = grey `RGB(180,180,180)`, Mandible = light blue `RGB(100,149,237)`, Teeth = ivory `RGB(255,250,240)`
- **Inputs:** `SkullMesh`, `MandibleMesh`, `vtkImageData` (E4-T1)
- **Outputs:** Color-coded meshes with teeth highlighted
- **Dependencies:** E4-T1
- **Acceptance Criteria:** Teeth clearly visible in different color on both arches; no separate mesh objects.

---

### E4-T3 — Export Individual Anatomical Segments as STL *(OPTIONAL)*
- **Status:** [ ] TODO
- **Goal:** Allow optional export of skull and mandible as separate STL files.
- **Technical Details:**
  - Right-click context menu on each segment: "Export as STL"
  - Use `vtkSTLWriter`
- **Inputs:** `SkullMesh`, `MandibleMesh` (E4-T1)
- **Outputs:** `.stl` files on disk
- **Dependencies:** E4-T1
- **Acceptance Criteria:** Exported STL opens correctly in MeshLab or PrusaSlicer.

---
---

# EPIC 5 — DENTAL CAST SUPERIMPOSITION AND BITE REGISTRATION

**Goal:** Import high-resolution STL dental casts, align them onto the CT model via ICP, and establish a reference occlusion.

> **Clinical rationale:** CT/CBCT produces poor dental detail due to metal artifacts. High-resolution optical scans replace CT teeth with accurate geometry, essential for planning occlusion and fabricating surgical splints.

> **Workflow position:** Epic 5 is the **second user-visible step** (after Import DICOM, before NHP and Osteotomies). Before E5 tasks become available, skull and mandible must already exist as two independent objects. Skull/mandible separation (E4-T1) therefore runs automatically or semi-automatically as a prerequisite when the user advances to the Dental Casts step — it is not a separate UI step.

---

### E5-T1 — Import STL Upper and Lower Dental Arches
- **Status:** [ ] TODO
- **Goal:** Load two STL files representing upper and lower dental arches from an optical scanner.
- **Technical Details:**
  - Two "Import" buttons: "Upper Arch" and "Lower Arch"
  - Use `vtkSTLReader`; display in scanner coordinate system (not yet aligned)
  - Colors: Upper = yellow `RGB(255,220,100)`, Lower = orange `RGB(255,165,0)`
  - Store as `UpperArchMesh`, `LowerArchMesh`
- **Inputs:** Two `.stl` files from dental scanner
- **Outputs:** `UpperArchMesh`, `LowerArchMesh` displayed in viewport
- **Dependencies:** E4-T1
- **Acceptance Criteria:** Both arches load and display correctly.

---

### E5-T2 — Automatic ICP Registration of Dental Casts onto CT
- **Status:** [ ] TODO
- **Goal:** Automatically align STL arches onto CT bone using Iterative Closest Point algorithm.
- **Technical Details:**
  - Use `vtkIterativeClosestPointTransform`
  - Pre-alignment: user clicks 3 landmark points on both meshes for coarse alignment first
  - ICP parameters: max 100 iterations, convergence threshold 0.01mm
  - Display registration error (RMS in mm) — clinical target: < 0.5mm
- **Inputs:** `UpperArchMesh`, `LowerArchMesh`, `SkullMesh`, `MandibleMesh`
- **Outputs:** Transformed arches in CT coordinate system + registration error value
- **Dependencies:** E5-T1, E4-T1
- **Acceptance Criteria:** Arches visually aligned; error displayed; error < 0.5mm on test case.

---

### E5-T2b — Post-Registration Alignment Quality Check
- **Status:** [ ] TODO
- **Goal:** After ICP registration of dental casts onto CT, automatically verify the quality of the tooth-to-bone alignment and flag any clinically significant misalignment before the surgeon proceeds.
- **Technical Details:**
  - Compute per-vertex distance between `UpperArchMesh` and the tooth region of `SkullMesh` (and same for lower arch vs mandible); report mean, max, and RMS distance in mm
  - Threshold: warn if mean distance > 0.5mm or max distance > 1.5mm — display a yellow warning banner in the properties panel: "Alignment error exceeds clinical threshold — manual correction recommended"
  - Add a cusp/collar verification overlay: highlight in red any arch vertex that is more than 1.0mm away from the nearest CT bone surface — visually shows the surgeon exactly where the misalignment is on the 3D model
  - Add a "Manual Initialization" button in the properties panel: clicking it activates a landmark-based manual alignment mode where the surgeon can click corresponding point pairs (one on arch, one on CT bone) to correct the coarse alignment before re-running ICP
  - After manual correction, re-run ICP automatically and recompute alignment error
  - If alignment error is within threshold after correction, show green confirmation: "Alignment verified — within clinical tolerance"
  - Store final alignment error value in session state for inclusion in the PDF report (E11-T2)
- **Inputs:** ICP-registered `UpperArchMesh`, `LowerArchMesh`, `SkullMesh`, `MandibleMesh` (E5-T2)
- **Outputs:** Alignment error metrics (mean/max/RMS in mm), visual misalignment overlay on 3D model, corrected mesh transforms if manual correction was applied
- **Dependencies:** E5-T2
- **Acceptance Criteria:** Warning displays when error exceeds threshold; red highlight correctly identifies misaligned vertices; manual initialization mode allows point-pair correction; ICP re-runs after correction; error value saved in session state.

---

### E5-T3 — Manual Fine-Tuning of Registration
- **Status:** [ ] TODO
- **Goal:** Allow user to manually adjust alignment after ICP.
- **Technical Details:**
  - Translation (X,Y,Z mm) and rotation (pitch,roll,yaw degrees) handles after ICP
  - "Reset to ICP result" button; error recalculates after each adjustment
- **Inputs:** ICP-aligned meshes (E5-T2)
- **Outputs:** Final-aligned `UpperArchMesh`, `LowerArchMesh`
- **Dependencies:** E5-T2
- **Acceptance Criteria:** User can nudge by <1mm increments; error recalculates in real time.

---

### E5-T3b — Skull/Mandible Separation Verification
- **Status:** [ ] TODO
- **Goal:** After skull/mandible separation (E4-T1), verify that the two meshes are correctly separated with no geometric artifacts that could falsify osteotomy cuts or segment movement downstream.
- **Technical Details:**
  - Run automated intersection test between `SkullMesh` and `MandibleMesh` using `vtkIntersectionPolyDataFilter`: if any triangle-triangle intersection is found, display a red error banner "Mesh intersection detected — separation has artifacts"
  - Check for open boundaries (non-manifold edges, holes) on both meshes using `vtkFeatureEdges` with `BoundaryEdgesOn()`: if holes found, display yellow warning "Open mesh boundary detected — may affect cut quality"
  - Display a separation summary panel in the right Properties panel showing:
    - `SkullMesh` triangle count and bounding box (mm)
    - `MandibleMesh` triangle count and bounding box (mm)
    - Minimum distance between the two meshes at the separation plane (should be ≥ 0mm — zero means touching, negative means interpenetrating)
    - Status: ✓ Clean separation / ⚠ Warning / ✗ Error
  - If intersection detected: offer a "Re-separate" button that re-runs E4-T1 semi-automatic separation with adjusted cut plane
  - If clean: show green "Separation verified — ready for osteotomy planning"
  - This check must pass (no intersection errors) before the Osteotomies step becomes available in the workflow panel
- **Inputs:** `SkullMesh`, `MandibleMesh` (E4-T1)
- **Outputs:** Separation quality report in properties panel; workflow gating (Osteotomies step locked until verification passes)
- **Dependencies:** E4-T1, E5-T3
- **Acceptance Criteria:** Intersection test runs automatically; red/yellow/green status shown; Osteotomies workflow step remains locked until separation is verified clean.

---

### E5-T4 — Replace CT Teeth with High-Resolution Arch Geometry
- **Status:** [ ] TODO
- **Goal:** Visually replace low-quality CT tooth regions with high-resolution STL arch geometry.
- **Technical Details:**
  - CT tooth regions hidden in viewport; registered arches displayed in their place
  - Arches are child objects of parent bone — they move together whenever parent bone moves
- **Inputs:** Registered arches (E5-T3), `SkullMesh`, `MandibleMesh`
- **Outputs:** Integrated dentoskeletal model
- **Dependencies:** E5-T3
- **Acceptance Criteria:** Model shows high-res teeth; arches move with parent bones in all subsequent steps.

---

### E5-T5 — Import Physical Bite Scan *(OPTIONAL)*
- **Status:** [ ] TODO
- **Goal:** Load optional STL of both arches already in final planned occlusion (physically scanned).
- **Technical Details:**
  - Align to CT via ICP; store as `FinalBiteReference` — locked, does not move
  - Toggle on/off via toolbar; semi-transparent overlay
- **Inputs:** One `.stl` of both arches in occlusion
- **Outputs:** `FinalBiteReference` object in scene
- **Dependencies:** E5-T3
- **Acceptance Criteria:** Bite reference visible as overlay; toggle works; object locked.

---

### E5-T6 — Virtual Auto-Occlusion (Alternative to Physical Bite)
- **Status:** [ ] TODO
- **Goal:** Automatically position arches in normalized Class I occlusion when no physical bite scan available.
- **Technical Details:**
  - Detect tooth landmarks via curvature analysis; compute rigid transform for:
    - Molar Class I: mesiobuccal cusp of upper 1st molar in buccal groove of lower 1st molar
    - Canine Class I: upper canine in embrasure of lower canine/1st premolar
    - Overjet: 1–2mm, Overbite: 1–2mm, Midlines coincident
  - User can manually adjust after auto-positioning
  - Store as `VirtualBiteReference`
- **Inputs:** Registered `UpperArchMesh`, `LowerArchMesh` (E5-T3)
- **Outputs:** `VirtualBiteReference` object in scene
- **Dependencies:** E5-T3
- **Acceptance Criteria:** Auto-occlusion produces visually plausible Class I; user can adjust manually.

---

### E5-T6b — Target Occlusion Definition and Validation
- **Status:** [ ] TODO
- **Goal:** Allow the surgeon to explicitly define and validate the target occlusion (the planned final bite relationship) before entering osteotomy planning. The target occlusion can come from a physical bite scan (E5-T5) or virtual auto-occlusion (E5-T6); this task formalizes it as the locked planning reference.
- **Technical Details:**
  - UI: "Define Target Occlusion" button appears in the Dental Casts workflow panel after E5-T5 or E5-T6 is complete
  - Display the current occlusion relationship in the triplanar viewer (axial view shows both arches from below so the surgeon can verify cusp-fossa relationships)
  - Show an occlusion checklist in the Properties panel that the surgeon must review:
    - [ ] Molar Class I relationship confirmed (left and right)
    - [ ] Canine relationship confirmed (left and right)
    - [ ] Overjet within 1–3mm
    - [ ] Overbite within 1–3mm
    - [ ] Midlines coincident (≤1mm discrepancy)
  - For each checklist item: auto-compute the metric from arch geometry and show the measured value next to the checkbox; color-code green (within range) or yellow (outside range)
  - Surgeon can manually check/uncheck each item to override automatic assessment
  - Arch interpenetration check: run `vtkIntersectionPolyDataFilter` on `UpperArchMesh` vs `LowerArchMesh` in the defined occlusion position — if any interpenetration detected, show red warning "Arch collision detected — arches interpenetrate in planned occlusion. Review occlusal set-up before proceeding." and block confirmation
  - "Confirm Target Occlusion" button locks `FinalBiteReference` or `VirtualBiteReference` as the definitive planning target — this object cannot be moved after confirmation
  - Locked occlusion shown as semi-transparent cyan overlay (toggle via "Bite Ref" toolbar button)
  - If the surgeon proceeds to Osteotomies without confirming target occlusion → show warning dialog "Target occlusion not defined. Splint generation will not be available. Continue anyway?"
- **Inputs:** `FinalBiteReference` (E5-T5) or `VirtualBiteReference` (E5-T6), `UpperArchMesh`, `LowerArchMesh`
- **Outputs:** Locked `TargetOcclusion` object in session state; occlusion metrics stored for PDF report
- **Dependencies:** E5-T5 or E5-T6
- **Acceptance Criteria:** Occlusion checklist auto-computes all 5 metrics; interpenetration check blocks confirmation if collision detected; "Confirm" locks the reference; warning shown if skipped.

---
---

# EPIC 6 — NATURAL HEAD POSITION (NHP)

**Goal:** Orient the 3D skull model in Natural Head Position — the orientation a patient maintains standing upright with eyes on the horizon.

> **Clinical rationale:** CT scans are acquired supine (arbitrary orientation). All surgical measurements must reference NHP, using the Frankfort Horizontal Plane (Porion–Orbitale line) as clinical horizontal.

---

### E6-T1 — Three-Axis NHP Viewer
- **Status:** [ ] TODO
- **Goal:** Display skull simultaneously in three orthogonal 2D views for precise orientation.
- **Technical Details:**
  - Three linked 2D views: Frontal (coronal), Lateral (sagittal), Top (axial)
  - Views update in real time during rotation; reference grid lines; draggable guide lines
- **Inputs:** `SkullMesh`, `SoftTissueMesh`
- **Outputs:** Three synchronized 2D views
- **Dependencies:** E4-T1, E3-T2
- **Acceptance Criteria:** All three views display and update in real time during rotation.

---

### E6-T2 — Rotation Controls (Pitch, Roll, Yaw)
- **Status:** [ ] TODO
- **Goal:** Allow user to rotate entire model on three axes to achieve NHP.
- **Technical Details:**
  - Three sliders: Pitch (X, ±45°), Roll (Z, ±45°), Yaw (Y, ±45°) + numeric input
  - Rotation applied to entire scene; center = centroid of `SkullMesh`
  - Store cumulative rotation as 4×4 `NHPMatrix`
- **Inputs:** Entire scene
- **Outputs:** `NHPMatrix` in session state
- **Dependencies:** E6-T1
- **Acceptance Criteria:** Sliders rotate model smoothly; numeric input works; rotation around model centroid.

---

### E6-T3 — Visual Reference Guides (Frankfort, Midline, Bipupillary)
- **Status:** [ ] TODO
- **Goal:** Display standard clinical reference lines as overlays to help judge NHP visually.
- **Technical Details:**
  - User clicks 6 landmarks total to define:
    - Frankfort Horizontal (Porion + Orbitale) — lateral view
    - Facial midline (Nasion + Philtrum) — frontal view
    - Bipupillary line (both pupils) — frontal view
  - Goal: Frankfort Horizontal appears horizontal in lateral view
- **Inputs:** `SkullMesh`, user landmark clicks
- **Outputs:** Reference line overlays; landmark positions saved in session
- **Dependencies:** E6-T1
- **Acceptance Criteria:** All three reference lines display and update during rotation.

---

### E6-T4 — Save NHP as Global Project Reference
- **Status:** [ ] TODO
- **Goal:** Lock current orientation as definitive NHP for the entire project.
- **Technical Details:**
  - "Set as NHP" button saves `NHPMatrix` to project
  - All subsequent measurements computed in NHP coordinate system
  - "Reset NHP" button available
- **Inputs:** `NHPMatrix` (E6-T2)
- **Outputs:** `NHPMatrix` saved in project session state
- **Dependencies:** E6-T2
- **Acceptance Criteria:** After setting NHP, all measurements reference NHP; saving/reloading preserves NHP.

---

### E6-T5 — Persistent NHP Button in Main Toolbar
- **Status:** [ ] TODO
- **Goal:** Allow user to reopen NHP panel at any point during planning without losing work.
- **Technical Details:**
  - Permanent "NHP" button in main toolbar — always visible
  - Opens NHP panel as floating/dockable side panel
  - If NHP changed after osteotomy planning → warn user that measurements may change
- **Inputs:** Current `NHPMatrix`
- **Outputs:** Updated `NHPMatrix` if changed
- **Dependencies:** E6-T4
- **Acceptance Criteria:** NHP button always visible; panel opens without interrupting 3D viewport; warning shown if changed post-planning.

---
---

# EPIC 7 — OSTEOTOMY PLANNING

**Goal:** Define virtual bone cuts on the 3D model. After cutting, each segment becomes an independent 3D object for movement in Epic 8.

---

### E7-T1 — LeFort I Osteotomy
- **Status:** [ ] TODO
- **Goal:** Apply standard LeFort I horizontal cut to separate maxilla from upper skull base.
- **Technical Details:**
  - Default cut: horizontal plane at nasal floor level (~5mm above nasal floor)
  - Cut plane shown as semi-transparent red rectangle; adjustable in sagittal view
  - On confirmation: `vtkClipPolyData` splits `SkullMesh` into:
    - `MaxillaMesh` (below cut — moves independently)
    - `UpperSkullMesh` (above cut — cranial base reference, fixed)
  - `MaxillaMesh` inherits `UpperArchMesh` as child object
- **Inputs:** `SkullMesh`, `NHPMatrix`
- **Outputs:** `MaxillaMesh`, `UpperSkullMesh` (vtkPolyData)
- **Dependencies:** E6-T4, E4-T1
- **Acceptance Criteria:** Two complete watertight mesh halves; `MaxillaMesh` moves independently.

---

### E7-T2 — BSSO (Bilateral Sagittal Split Osteotomy)
- **Status:** [ ] TODO
- **Goal:** Apply bilateral sagittal cuts to mandibular rami, separating tooth-bearing segment from condylar segments.
- **Technical Details:**
  - Produces three segments from `MandibleMesh`:
    - `MandibleBodyMesh` (tooth-bearing — moves)
    - `RightProximalMesh` (condyle+ramus — fixed)
    - `LeftProximalMesh` (condyle+ramus — fixed)
  - User adjusts cut planes in axial and coronal views
  - `MandibleBodyMesh` inherits `LowerArchMesh`
- **Inputs:** `MandibleMesh`, `NHPMatrix`
- **Outputs:** `MandibleBodyMesh`, `RightProximalMesh`, `LeftProximalMesh`
- **Dependencies:** E6-T4, E4-T1
- **Acceptance Criteria:** Three segments produced; only `MandibleBodyMesh` moves; proximal segments fixed.

---

### E7-T3 — Genioplasty Osteotomy
- **Status:** [ ] TODO
- **Goal:** Apply horizontal cut to chin segment for independent repositioning.
- **Technical Details:**
  - Default cut: horizontal plane 5mm below mental foramen; adjustable in sagittal view
  - Splits into `ChinMesh` (moves independently) and updated `MandibleBodyMesh`
  - `ChinMesh` does NOT inherit dental arch
- **Inputs:** `MandibleBodyMesh` (or `MandibleMesh`), `NHPMatrix`
- **Outputs:** `ChinMesh`, updated `MandibleBodyMesh`
- **Dependencies:** E7-T2 (or E4-T1 if no BSSO)
- **Acceptance Criteria:** Chin segment moves independently; mandible body intact.

---

### E7-T4 — Multipart Maxillary Osteotomy (Free-Draw)
- **Status:** [ ] TODO
- **Goal:** Allow surgeon to draw custom cut planes on maxilla for 2–5 independent segments.
- **Technical Details:**
  - Free-draw plane tool on `MaxillaMesh` in axial view
  - 1–4 cuts → 2–5 segments; each assigned unique color automatically
  - Each segment = independent `vtkPolyData`; inherits overlapping portion of `UpperArchMesh`
  - Each segment moves independently with full 6-DOF in Epic 8
- **Inputs:** `MaxillaMesh` (E7-T1), `UpperArchMesh`
- **Outputs:** Array of 2–5 `MaxillaSegment` objects (vtkPolyData)
- **Dependencies:** E7-T1
- **Acceptance Criteria:** User can draw 1–4 cuts; each segment different color and moves independently.

---

### E7-T5 — Osteotomy Plane Visualization, Interactive Control Points, and Cut Confirmation
- **Status:** [ ] TODO
- **Goal:** Show each osteotomy cut plane as an interactive poly-plane on the 3D model before confirmation, controllable via draggable control points (similar to IPS Case Designer), with safe zone visualization and a confirmation workflow that splits the mesh into independent segments.
- **Technical Details:**
  - Poly-plane representation: each cut plane is rendered as a semi-transparent colored rectangle (LeFort I = red, BSSO = blue, Genioplasty = green, Custom = orange) overlaid on the 3D bone mesh
  - Control point handles: display 4 sphere handles at the corners of each plane (radius 3mm, colored to match plane color). The surgeon can:
    - Drag any corner sphere → changes plane orientation (tilt/rotation) in real time
    - Drag the plane center → translates the plane along its normal without changing orientation
    - Numeric readout in Properties panel: plane position (X,Y,Z in mm relative to NHP) and orientation (pitch/roll/yaw in degrees)
  - Safe zone visualization:
    - For BSSO: display the mandibular canal as a colored tube overlay (yellow, 2mm diameter) computed from HU values — warn in red if the cut plane intersects the canal within 2mm safety margin
    - For LeFort I: display the nasal floor and orbital rim as reference lines — warn if plane is above orbital rim or below nasal spine
    - For Genioplasty: display the mental foramen positions as spheres — warn if plane is within 3mm of either foramen
  - Real-time intersection check: while the surgeon drags control points, continuously check `vtkIntersectionPolyDataFilter` between the proposed cut plane and the parent mesh — show a green outline on the mesh where the cut will be made
  - "Confirm Cut" button: applies `vtkClipPolyData` with the current plane, splits the parent mesh into two (or more for multipart) independent `vtkPolyData` objects, assigns each a distinct color, and adds them to the scene graph as moveable segments
  - "Cancel" button: removes the plane visualization entirely without modifying any mesh
  - Undo: after confirmation, a single "Undo Cut" action is available (restores original mesh, removes segments) — only available immediately after the cut, before any segment movement
  - Post-cut display: after confirmation, the cut plane is hidden; each resulting segment is shown in its assigned color with a label (e.g. "Maxilla", "Cranial Base", "Mandible Body", "Right Proximal", "Left Proximal", "Chin")
- **Inputs:** Active cut plane + parent mesh (`SkullMesh` for LeFort, `MandibleMesh` for BSSO/Genioplasty, `MaxillaMesh` for multipart)
- **Outputs:** Visual poly-plane with control point handles; safe zone overlays; confirmed mesh split into independent segments
- **Dependencies:** E7-T1, E7-T2, E7-T3, E7-T4
- **Acceptance Criteria:** Control point handles are draggable and update plane in real time; safe zone warnings display correctly; confirm splits mesh into correct number of independent segments; cancel removes plane without modifying mesh; undo restores original mesh.

---
---

# EPIC 8 — SEGMENT MOVEMENT + REAL-TIME SOFT TISSUE SIMULATION

**Goal:** Move each bone segment independently in 3D with a toggleable soft tissue mask that approximates facial skin changes in real time.

> **Note:** Uses geometric approximation (Dolphin-style tissue-to-bone ratios), NOT FEM. Clinically sufficient for v1.0. FEM deferred to v2.0.

---

### E8-T1 — Segment Translation and Rotation Controls
- **Status:** [ ] TODO
- **Goal:** Interactive controls to move each bone segment independently in 3D.
- **Technical Details:**
  - Each moveable segment has: 6-DOF gizmo for viewport dragging + properties panel with X,Y,Z (mm) and Pitch,Roll,Yaw (degrees) relative to NHP coordinate system
  - User drags OR types exact values
  - Child objects (arches) move rigidly with parent bone
  - Proximal condyles and `UpperSkullMesh` do NOT move
  - Undo/redo stack (50 steps)
- **Inputs:** All moveable segment meshes (from Epic 7)
- **Outputs:** Updated 4×4 transformation matrices per segment
- **Dependencies:** E7-T1, E7-T2, E7-T3
- **Acceptance Criteria:** Each segment moves independently; children follow parent; numeric values accurate; undo works.

---

### E8-T1b — Condyle Autorotation Verification and Segment Collision Detection
- **Status:** [ ] TODO
- **Goal:** During segment movement, continuously verify that condyle positions are anatomically consistent and that no two bone segments interpenetrate, providing real-time feedback and adjustment tools.
- **Technical Details:**
  - Condyle autorotation tracking: as `MandibleBodyMesh` is moved (translated/rotated), compute the resulting condyle head position relative to the glenoid fossa on `UpperSkullMesh`; display the condyle-fossa gap in mm in the Properties panel for left and right condyle independently
    - Green: condyle seated within fossa (gap 0–1mm)
    - Yellow: condyle slightly distracted (gap 1–3mm) — warning "Condyle distraction detected"
    - Red: condyle collision (gap < 0mm, i.e. interpenetration) — error "Condyle interpenetration — adjust autorotation"
  - Autorotation adjustment: if condyle interpenetration is detected, display an "Adjust Autorotation" slider in the Properties panel that rotates `MandibleBodyMesh` around the TMJ pivot axis (midpoint of left+right condyle centers) to resolve the collision; range ±10°
  - Segment collision detection: after every segment move (mouse-up event), run pairwise `vtkIntersectionPolyDataFilter` between all moveable segments and all fixed segments; highlight interpenetrating faces in red on both meshes; display warning "Segment collision: [SegmentA] intersects [SegmentB]"
  - Collision summary panel: small overlay in bottom-left of viewport listing all current collisions (e.g. "MaxillaMesh ↔ UpperSkullMesh: 2.3mm overlap") — clears automatically when collision is resolved
  - Performance: collision detection runs asynchronously after mouse-up, not during drag; timeout 500ms
- **Inputs:** All segment transformation matrices (E8-T1), condyle center positions (identified in E7-T2), `UpperSkullMesh` glenoid fossa region
- **Outputs:** Condyle gap measurements per side; collision highlights on mesh; autorotation adjustment slider
- **Dependencies:** E8-T1, E7-T2
- **Acceptance Criteria:** Condyle gap updates after each mandible movement; color coding correct; autorotation slider resolves condyle collision; segment collision detection identifies and highlights interpenetrating meshes within 500ms.

---

### E8-T2 — Real-Time Soft Tissue Mask (Geometric Approximation)
- **Status:** [ ] TODO
- **Goal:** Toggle semi-transparent skin surface that deforms in real time as bone segments move.
- **Technical Details:**
  - Tissue-to-bone displacement ratios per zone (from literature):
    - Upper lip: 0.6:1 (follows maxilla)
    - Lower lip: 0.8:1 (follows mandible)
    - Nose tip: 0.3:1 A-P (follows maxilla)
    - Paranasal: 0.8:1 (follows maxilla)
    - Chin soft tissue: 0.9:1 (follows chin bone)
    - Cheeks: 0.5:1 (follows maxilla)
  - Per vertex: weighted influence from nearby bone segments by proximity, scaled displacement
  - Update after mouse-up (not during drag for performance)
  - Render: alpha 0.4, skin tone `RGB(255,200,170)`
  - Toggle via persistent "Soft Tissue" toolbar button
- **Inputs:** `SoftTissueMesh` (E3-T2), transformation matrices (E8-T1)
- **Outputs:** Deformed `SoftTissueMesh` in viewport
- **Dependencies:** E8-T1, E3-T2
- **Acceptance Criteria:** Updates after bone movement; ratios anatomically plausible; toggle instant; update <500ms.

---

### E8-T3 — Before/After Comparison View
- **Status:** [ ] TODO
- **Goal:** Allow user to compare original and planned bone positions.
- **Technical Details:**
  - "Compare" toggle: split viewport (left = original, right = planned) OR ghost wireframe overlay
  - Both modes show soft tissue in planned position
  - Measurements panel: total movement per segment in mm and degrees
- **Inputs:** Original mesh positions, current transformation matrices
- **Outputs:** Split viewport or overlay comparison
- **Dependencies:** E8-T2
- **Acceptance Criteria:** Before/after view clearly shows difference; measurements displayed per segment.

---
---

# EPIC 9 — SURGICAL SPLINT GENERATION

**Goal:** Auto-generate intermediate and final surgical splints as printable STL files.

> **Clinical workflow:** Intermediate splint used after maxilla moved, before BSSO (mandible autorotates on TMJ). Final splint used after both jaws moved, establishing final occlusion.

---

### E9-T1 — Compute Intermediate Splint Geometry
- **Status:** [ ] TODO
- **Goal:** Calculate intermediate splint based on planned maxillary movement and mandibular autorotation.
- **Technical Details:**
  - Fills space between: `MaxillaMesh` occlusal surface (planned) and `MandibleBodyMesh` autorotated around TMJ condyle center
  - TMJ autorotation: pivot = midpoint of left+right condyle centers; rotate mandible until arches contact
  - Use `vtkBooleanOperationPolyDataFilter` to compute interocclusal volume
  - Add 2mm border walls; optional ligature holes (2mm diameter)
- **Inputs:** `MaxillaMesh` (planned), `MandibleBodyMesh` (autorotated), condyle positions
- **Outputs:** `IntermediateSplintMesh` (vtkPolyData)
- **Dependencies:** E8-T1, E7-T1, E7-T2
- **Acceptance Criteria:** Splint fits exactly between arches; no interpenetration; geometry watertight and printable.

---

### E9-T2 — Compute Final Splint Geometry
- **Status:** [ ] TODO
- **Goal:** Calculate final splint based on planned final occlusion.
- **Technical Details:**
  - Fills space between: `MaxillaMesh` (final planned) and `MandibleBodyMesh` (final planned) occlusal surfaces
  - Same Boolean geometry approach as E9-T1
  - References `FinalBiteReference` or `VirtualBiteReference` from Epic 5
- **Inputs:** Both meshes in final planned positions, BiteReference
- **Outputs:** `FinalSplintMesh` (vtkPolyData)
- **Dependencies:** E8-T1, E5-T5 or E5-T6
- **Acceptance Criteria:** Final splint fits planned occlusion; geometry watertight.

---

### E9-T3 — Splint Thickness Control (Mandibular Opening Angle)
- **Status:** [ ] TODO
- **Goal:** Set splint thickness by specifying mandibular opening angle.
- **Technical Details:**
  - Numeric input: opening angle in degrees (typical range 15–35°)
  - Show equivalent inter-incisal distance in mm
  - Recompute splint geometry when angle changes; real-time 3D preview
  - Min thickness: 2mm | Max thickness: 15mm
- **Inputs:** Opening angle value, arch geometry
- **Outputs:** Updated `IntermediateSplintMesh` and `FinalSplintMesh`
- **Dependencies:** E9-T1, E9-T2
- **Acceptance Criteria:** Changing angle updates thickness in real time; limits enforced; mm equivalent shown.

---

### E9-T4 — Splint 3D Preview and Export to STL
- **Status:** [ ] TODO
- **Goal:** Visually inspect both splints and export as STL for 3D printing.
- **Technical Details:**
  - Colors: Intermediate = cyan, Final = magenta; transparency slider per splint
  - Mesh validation before export: watertightness, non-manifold edges, self-intersections
  - Export buttons: "Export Intermediate STL" and "Export Final STL"
  - Use `vtkSTLWriter` in binary mode
  - Export dialog note: *"Print in biocompatible resin (Class IIa medical device)"*
- **Inputs:** `IntermediateSplintMesh`, `FinalSplintMesh`
- **Outputs:** Two `.stl` files ready for 3D printing
- **Dependencies:** E9-T1, E9-T2, E9-T3
- **Acceptance Criteria:** Both splints display correctly; validation passes; STL opens in PrusaSlicer/Formlabs PreForm without errors.

---
---

# EPIC 10 — USER INTERFACE

**Goal:** Polished, professional UI guiding the surgeon through the workflow step by step, with all critical tools always accessible and a dark theme matching medical software standards.

---

### E10-T1 — Finalize Main Layout and Dark Theme
- **Status:** [ ] TODO
- **Goal:** Polish main window with professional dark theme matching medical software standards.
- **Technical Details:**
  - Colors: background `#1E1E1E`, panels `#252526`, accent `#0078D4`
  - Font: Segoe UI 12pt; icons: Material Design white on dark; toolbar 40px height
  - All interactive elements have hover state
- **Inputs:** UI shell (E1-T5)
- **Outputs:** Polished main window
- **Dependencies:** E1-T5
- **Acceptance Criteria:** UI matches dark professional medical software; all text readable; no contrast issues.

---

### E10-T2 — Persistent Toolbar Buttons (NHP, Soft Tissue, Bite Reference)
- **Status:** [ ] TODO
- **Goal:** NHP, Soft Tissue, and Bite Reference buttons always visible in toolbar.
- **Technical Details:**
  - Always enabled and visible: "NHP" (opens NHP panel), "Soft Tissue" toggle, "Bite Ref" toggle
  - Active state = highlighted blue; tooltips on hover
- **Inputs:** All toolbar button implementations
- **Outputs:** Always-visible toolbar controls
- **Dependencies:** E6-T5, E8-T2, E5-T5
- **Acceptance Criteria:** All three buttons always visible; state toggles correctly; tooltips work.

---

### E10-T3 — Step-by-Step Workflow Panel
- **Status:** [ ] TODO
- **Goal:** Guide surgeon through workflow with numbered step list in left panel.
- **Technical Details:**
  - **7 user-visible steps:** 1.Import DICOM, 2.Dental Casts, 3.NHP, 4.Osteotomies, 5.Segment Movement, 6.Splint Generation, 7.Export
  - Segmentation (E3) and Segment Identification (E4) are **not shown** as steps — they execute automatically when the user enters Osteotomies
  - Each step: status icon, title, brief description
  - Completed steps = green checkmark; unsatisfied dependencies = greyed out
  - Note: `MainWindow.xaml.cs` `Steps` array must be updated to match (currently has 9 steps including the two hidden ones)
- **Inputs:** Session state
- **Outputs:** Navigable 7-step list
- **Dependencies:** All prior epics
- **Acceptance Criteria:** Steps accurately reflect session state; navigation works; completed steps marked; segmentation steps not visible to user.

---

### E10-T4 — Context-Sensitive Right Properties Panel
- **Status:** [ ] TODO
- **Goal:** Show relevant controls in right panel based on active tool or selected object.
- **Technical Details:**
  - Segmentation active → threshold sliders
  - Bone segment selected → translation/rotation inputs
  - NHP tool active → pitch/roll/yaw sliders
  - Splint tool active → thickness/opening angle controls
- **Inputs:** Active tool state
- **Outputs:** Context-appropriate controls in right panel
- **Dependencies:** All tool implementations
- **Acceptance Criteria:** Right panel always shows relevant controls; switching tools updates panel instantly.

---
---

# EPIC 11 — EXPORT AND REPORTING

**Goal:** Export planning results as STL files and a PDF report summarizing the surgical plan.

---

### E11-T1 — Export Final Planned Assembly as STL
- **Status:** [ ] TODO
- **Goal:** Export complete planned bone configuration as STL.
- **Technical Details:**
  - Apply all transformation matrices; merge via `vtkAppendPolyData`; export with `vtkSTLWriter`
  - Option to export individual segments separately
- **Inputs:** All segment meshes with final transformations
- **Outputs:** `PlannedAssembly.stl` (or individual STLs)
- **Dependencies:** E8-T1
- **Acceptance Criteria:** Exported STL correctly represents planned outcome; opens in any slicer software.

---

### E11-T2 — PDF Surgical Planning Report
- **Status:** [ ] TODO
- **Goal:** Generate PDF summarizing complete surgical plan for OR use and patient record.
- **Technical Details:**
  - Contents: patient info (from DICOM metadata), 3D screenshots (frontal/lateral/axial before+after), soft tissue frontal before/after, measurements table per segment (X/Y/Z mm, pitch/roll/yaw degrees), splint parameters
  - Use PdfSharp or iTextSharp
  - CMF Planner v1.0 logo + generation date in footer
- **Inputs:** Session state, transformation matrices, viewport screenshots
- **Outputs:** `SurgicalPlan_[PatientName]_[Date].pdf`
- **Dependencies:** E8-T1, E9-T4
- **Acceptance Criteria:** PDF opens correctly; measurements match in-app values; screenshots ≥300dpi equivalent.

---

### E11-T3 — Save and Reload Project File
- **Status:** [ ] TODO
- **Goal:** Save entire planning session as `.cmfplan` file for later continuation.
- **Technical Details:**
  - Format: `.cmfplan` (ZIP-based)
  - Contents: transformation matrices (JSON), session metadata, threshold values, `NHPMatrix`, landmark positions, DICOM folder path reference, STL mesh cache
  - Save: `Ctrl+S` | Open: `Ctrl+O`
  - On load: verify DICOM folder accessible; prompt to relocate if not
- **Inputs:** Complete session state
- **Outputs:** `.cmfplan` file
- **Dependencies:** All epics
- **Acceptance Criteria:** Save/reload restores exact state; works across sessions; file size <500MB.

---

## QUICK REFERENCE

| | |
|---|---|
| **Current task** | E3-T3 — Manual Segmentation Refinement (Brush/Eraser) |
| **Commit format** | `"E{epic}-T{task}: {title}"` |
| **Completed** | 13 of 47 tasks (E1-T1 through E1-T5, E2-T1 through E2-T5 incl. E2-T1b, E3-T1, E3-T2 ✓) |
| **Tech stack** | C# .NET 8, WPF, VTK.NET, fo-dicom, MEF, MathNet |
| **Skip optionals?** | Yes — tasks marked OPTIONAL can be deferred |
| **Soft tissue** | Geometric approximation only, NO FEM in v1.0 |
| **Splint export** | Binary STL, biocompatible resin recommended |
