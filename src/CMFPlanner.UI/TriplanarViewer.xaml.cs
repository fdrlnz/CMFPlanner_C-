using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using CMFPlanner.Core.Models;
using CMFPlanner.Visualization;
using HelixToolkit.Wpf;

namespace CMFPlanner.UI;

/// <summary>
/// Synchronized triplanar 2D viewer: axial, coronal, sagittal.
/// Views are rendered from the HU buffer in <see cref="VolumeData"/>.
/// Crosshairs link all three views; double-click any quadrant to expand it.
/// W/L is adjusted by right-click drag (horizontal=width, vertical=center).
/// </summary>
public partial class TriplanarViewer : UserControl
{
    // ── State ─────────────────────────────────────────────────────────────

    private VolumeData? _volume;

    // Crosshair position in voxel space
    private int _cx, _cy, _cz;

    // Window/Level (Hounsfield units)
    private int _ww = 2000, _wl = 400;                // bone window default

    // Right-drag W/L state
    private bool  _wlDragging;
    private Point _wlDragOrigin;
    private int   _wlDragStartWw, _wlDragStartWl;

    // Expanded quadrant
    private enum Quadrant { None, Axial, Coronal, Sagittal, Preview3D }
    private Quadrant _expanded = Quadrant.None;

    // Rotation zone within the 3D preview
    // Top 50 % is divided: left 25 % = Yaw (upper) / PitchLeft (lower),
    //                       right 25 % = Roll (upper) / PitchRight (lower),
    //                       center 50 % = Center (free).
    // Bottom 50 % is always Free (existing unconstrained orbit).
    private enum RotationZone { Yaw, Roll, PitchLeft, PitchRight, Center, Free }

    // 2D grid overlay
    private bool _gridVisible = true;

    // 3D bone preview
    private MeshData? _boneMesh;
    private Point3D   _orbitTarget = new(0, 0, 0);

    // Zoom and Pan
    private double _zoomFactor = 1.0;
    private Point  _panAxial, _panCoronal, _panSagittal;
    private bool   _isPanning;
    private Point  _panDragOrigin;
    private Point  _panDragStartOffset;
    private Quadrant _panQuadrant;

    // Zone rotation state
    private RotationZone _dragZone          = RotationZone.Free;
    private RotationZone _manipZone         = RotationZone.Free;
    private bool         _manipZoneReady;        // set on ManipulationStarted
    private bool         _zoneHintTriggered;     // true once hint has been shown this session
    private System.Windows.Threading.DispatcherTimer? _zoneHintTimer;
    private static readonly string _hintFlagPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CMFPlanner", "zone_hint_dismissed");

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Fired when cursor moves over a slice; argument is the HU value under cursor.</summary>
    public event Action<short>? CursorHuChanged;

    /// <summary>Fired whenever Window or Level changes (drag or preset); args are (ww, wl).</summary>
    public event Action<int, int>? WlChanged;

    /// <summary>Fired when a W/L preset button is clicked. Passes the preset name (e.g. "Bone", "Soft Tissue").</summary>
    public event Action<string>? PresetSelected;

    /// <summary>Fired when the live bone slider changes; argument is the new HU threshold.</summary>
    public event Action<int>? LiveThresholdChanged;

    public TriplanarViewer()
    {
        InitializeComponent();

        // Use custom sizing to force synchronized zooming across all 3 viewports
        AxialBorder.SizeChanged    += OnViewportSizeChanged;
        CoronalBorder.SizeChanged  += OnViewportSizeChanged;
        SagittalBorder.SizeChanged += OnViewportSizeChanged;

        // Redraw the 2D grid overlay on the 3D preview when it resizes
        InfoBorder.SizeChanged += (_, _) => Draw3DGridOverlay();

        // Escape collapses any expanded quadrant
        Loaded += (_, _) =>
        {
            var win = Window.GetWindow(this);
            if (win != null) win.PreviewKeyDown += OnWindowKeyDown;
        };
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _expanded != Quadrant.None)
        {
            CollapseAll();
            _expanded = Quadrant.None;
            e.Handled = true;
        }
    }

    /// <summary>Shows or hides the metric reference grid on all three slice panels.</summary>
    public void SetGridVisible(bool visible)
    {
        _gridVisible = visible;
        UpdateAllGridOverlays();
    }

    /// <summary>
    /// Loads a volume into the viewer, centres the crosshair, and renders all three planes.
    /// </summary>
    public void SetVolume(VolumeData volume, string patientLabel = "")
    {
        _volume = volume;
        _cx = volume.Columns  / 2;
        _cy = volume.Rows     / 2;
        _cz = volume.SliceCount / 2;

        // Size the inner grids to match bitmap pixel dimensions
        SetGridSize(AxialGrid,    AxialCanvas,    AxialGridCanvas,    volume.Columns, volume.Rows);
        SetGridSize(CoronalGrid,  CoronalCanvas,  CoronalGridCanvas,  volume.Columns, volume.SliceCount);
        SetGridSize(SagittalGrid, SagittalCanvas, SagittalGridCanvas, volume.Rows,    volume.SliceCount);

        SyncZoom();
        RefreshAll();
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAllGridOverlays();
        SyncZoom();
    }

    /// <summary>
    /// Computes a single unified px-per-mm scale factor across all three 2D panels
    /// and applies a combined LayoutTransform (spacing × zoom) to each grid.
    /// This guarantees that 1 physical mm occupies the same number of screen pixels
    /// in every viewport, so all three images appear at the same scale.
    /// </summary>
    private void SyncZoom()
    {
        if (_volume == null) return;

        // Physical dimensions of each view in mm
        double physAxialW    = _volume.Columns    * _volume.SpacingX;
        double physAxialH    = _volume.Rows       * _volume.SpacingY;
        double physCoronalW  = _volume.Columns    * _volume.SpacingX;
        double physCoronalH  = _volume.SliceCount * _volume.SpacingZ;
        double physSagittalW = _volume.Rows       * _volume.SpacingY;
        double physSagittalH = _volume.SliceCount * _volume.SpacingZ;

        // Available viewport pixel sizes
        double aw = AxialBorder.ActualWidth,    ah = AxialBorder.ActualHeight;
        double cw = CoronalBorder.ActualWidth,  ch = CoronalBorder.ActualHeight;
        double sw = SagittalBorder.ActualWidth,  sh = SagittalBorder.ActualHeight;

        if (aw <= 0 || cw <= 0 || sw <= 0) return;

        if (_expanded != Quadrant.None)
        {
            // When expanded, let the single visible panel fill its space
            double pxPerMm;
            if (_expanded == Quadrant.Axial)
                pxPerMm = Math.Min(aw / physAxialW, ah / physAxialH);
            else if (_expanded == Quadrant.Coronal)
                pxPerMm = Math.Min(cw / physCoronalW, ch / physCoronalH);
            else if (_expanded == Quadrant.Sagittal)
                pxPerMm = Math.Min(sw / physSagittalW, sh / physSagittalH);
            else
                return; // 3D panel expanded, nothing to do

            AxialGrid.LayoutTransform    = new ScaleTransform(pxPerMm * _volume.SpacingX, pxPerMm * _volume.SpacingY);
            CoronalGrid.LayoutTransform  = new ScaleTransform(pxPerMm * _volume.SpacingX, pxPerMm * _volume.SpacingZ);
            SagittalGrid.LayoutTransform = new ScaleTransform(pxPerMm * _volume.SpacingY, pxPerMm * _volume.SpacingZ);
            return;
        }

        // Compute per-panel max px/mm if each panel used its full area
        double scaleAxial    = Math.Min(aw / physAxialW,    ah / physAxialH);
        double scaleCoronal  = Math.Min(cw / physCoronalW,  ch / physCoronalH);
        double scaleSagittal = Math.Min(sw / physSagittalW, sh / physSagittalH);

        // Unified scale = smallest of the three, so 1 mm = same screen px everywhere
        double uni = Math.Min(scaleAxial, Math.Min(scaleCoronal, scaleSagittal));

        // Apply combined transform: voxel → mm (spacing) → screen (zoom)
        AxialGrid.LayoutTransform    = new ScaleTransform(uni * _volume.SpacingX, uni * _volume.SpacingY);
        CoronalGrid.LayoutTransform  = new ScaleTransform(uni * _volume.SpacingX, uni * _volume.SpacingZ);
        SagittalGrid.LayoutTransform = new ScaleTransform(uni * _volume.SpacingY, uni * _volume.SpacingZ);

        // Apply RenderTransform for dynamic Zoom and Pan
        AxialGrid.RenderTransform = new TransformGroup {
            Children = new TransformCollection {
                new ScaleTransform(_zoomFactor, _zoomFactor, aw / 2, ah / 2),
                new TranslateTransform(_panAxial.X, _panAxial.Y)
            }
        };
        CoronalGrid.RenderTransform = new TransformGroup {
            Children = new TransformCollection {
                new ScaleTransform(_zoomFactor, _zoomFactor, cw / 2, ch / 2),
                new TranslateTransform(_panCoronal.X, _panCoronal.Y)
            }
        };
        SagittalGrid.RenderTransform = new TransformGroup {
            Children = new TransformCollection {
                new ScaleTransform(_zoomFactor, _zoomFactor, sw / 2, sh / 2),
                new TranslateTransform(_panSagittal.X, _panSagittal.Y)
            }
        };
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        if (_volume == null) return;
        AxialImage.Source   = DicomSliceRenderer.RenderAxial(_volume, _cz, _ww, _wl);
        CoronalImage.Source = DicomSliceRenderer.RenderCoronal(_volume, _cy, _ww, _wl);
        SagittalImage.Source= DicomSliceRenderer.RenderSagittal(_volume, _cx, _ww, _wl);
        UpdateCrosshairs();
        UpdateWlDisplay();
    }

    private void UpdateCrosshairs()
    {
        if (_volume == null) return;
        int czFlip = _volume.SliceCount - 1 - _cz;  // superior slice at top

        // Axial (bitmap: Columns × Rows) — H=coronal ref (green), V=sagittal ref (orange)
        SetLine(AxialHLine,    0, _volume.Columns, _cy,     _cy);
        SetLine(AxialVLine,    _cx,    _cx,         0, _volume.Rows);

        // Coronal (bitmap: Columns × SliceCount) — H=axial ref (blue), V=sagittal ref (orange)
        SetLine(CoronalHLine,  0, _volume.Columns, czFlip,  czFlip);
        SetLine(CoronalVLine,  _cx,    _cx,         0, _volume.SliceCount);

        // Sagittal (bitmap: Rows × SliceCount) — H=axial ref (blue), V=coronal ref (green)
        SetLine(SagittalHLine, 0, _volume.Rows,    czFlip,  czFlip);
        SetLine(SagittalVLine, _cy,    _cy,         0, _volume.SliceCount);
    }

    private static void SetLine(Line l, double x1, double x2, double y1, double y2)
    {
        l.X1 = x1; l.X2 = x2; l.Y1 = y1; l.Y2 = y2;
    }

    private static void SetGridSize(FrameworkElement grid, FrameworkElement canvas, FrameworkElement gridCanvas, int w, int h)
    {
        grid.Width        = w; grid.Height        = h;
        canvas.Width      = w; canvas.Height      = h;
        gridCanvas.Width  = w; gridCanvas.Height  = h;
    }

    // ── 2D metric grid overlay ────────────────────────────────────────────

    private void UpdateAllGridOverlays()
    {
        if (_volume == null) return;
        DrawGridOverlay(AxialGridCanvas,
            _volume.Columns, _volume.SpacingX, _volume.Rows,       _volume.SpacingY,
            AxialBorder.ActualWidth,   AxialBorder.ActualHeight);
        DrawGridOverlay(CoronalGridCanvas,
            _volume.Columns, _volume.SpacingX, _volume.SliceCount, _volume.SpacingZ,
            CoronalBorder.ActualWidth,  CoronalBorder.ActualHeight);
        DrawGridOverlay(SagittalGridCanvas,
            _volume.Rows,    _volume.SpacingY, _volume.SliceCount, _volume.SpacingZ,
            SagittalBorder.ActualWidth, SagittalBorder.ActualHeight);
    }

    private void DrawGridOverlay(
        Canvas canvas,
        double imgW, double spacingX,
        double imgH, double spacingY,
        double borderW, double borderH)
    {
        canvas.Children.Clear();
        if (!_gridVisible || _volume == null || borderW <= 0 || borderH <= 0) return;

        // Physical dimensions in mm (Viewbox sees these after LayoutTransform)
        double physW   = imgW * spacingX;
        double physH   = imgH * spacingY;
        // px/mm: Viewbox Stretch=Uniform picks the limiting axis
        double pxPerMm = Math.Min(borderW / physW, borderH / physH);
        double px10mm  = pxPerMm * 10.0;

        // Choose mm spacing: 10 mm default, 5 mm zoomed, 1 mm maximum zoom
        double mm = px10mm < 30.0 ? 10.0
                  : px10mm < 80.0 ?  5.0
                  :                   1.0;

        double voxX       = mm / spacingX;          // voxel interval in X
        double voxY       = mm / spacingY;          // voxel interval in Y
        int    majorEvery = Math.Max(1, (int)Math.Round(10.0 / mm));  // major every 10 mm

        var minor = new SolidColorBrush(Color.FromArgb(55,  255, 255, 255));
        var major = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));

        // Vertical lines (constant X)
        for (double x = 0, i = 0; x <= imgW + voxX * 0.5; x += voxX, i++)
        {
            bool isMajor = (int)i % majorEvery == 0;
            canvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = imgH,
                Stroke          = isMajor ? major : minor,
                StrokeThickness = isMajor ? 0.6 : 0.4,
                IsHitTestVisible= false
            });
        }

        // Horizontal lines (constant Y)
        for (double y = 0, i = 0; y <= imgH + voxY * 0.5; y += voxY, i++)
        {
            bool isMajor = (int)i % majorEvery == 0;
            canvas.Children.Add(new Line
            {
                X1 = 0, X2 = imgW, Y1 = y, Y2 = y,
                Stroke          = isMajor ? major : minor,
                StrokeThickness = isMajor ? 0.6 : 0.4,
                IsHitTestVisible= false
            });
        }
    }

    private void UpdateWlDisplay() { /* W/L shown in status bar via WlChanged event */ }

    // ── W/L presets ───────────────────────────────────────────────────────

    private void BtnPresetSoftTissue_Click(object sender, RoutedEventArgs e)
    {
        _ww = 400; _wl = 40;
        RefreshAll();
        WlChanged?.Invoke(_ww, _wl);
        PresetSelected?.Invoke("Soft Tissue");
    }

    private void BtnPresetBone_Click(object sender, RoutedEventArgs e)
    {
        _ww = 2000; _wl = 400;
        RefreshAll();
        WlChanged?.Invoke(_ww, _wl);
        PresetSelected?.Invoke("Bone");
    }

    // ── Unified 2D Viewport Mouse Interactions ────────────────────────────

    private Quadrant GetQuadrant(object sender)
    {
        if (sender == AxialGrid) return Quadrant.Axial;
        if (sender == CoronalGrid) return Quadrant.Coronal;
        if (sender == SagittalGrid) return Quadrant.Sagittal;
        return Quadrant.None;
    }

    private void AnyGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var q = GetQuadrant(sender);
        if (e.ClickCount == 2) { ToggleExpand(q); return; }
        
        _wlDragging    = true;
        _wlDragOrigin  = e.GetPosition(this);
        _wlDragStartWw = _ww;
        _wlDragStartWl = _wl;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void AnyGrid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).CaptureMouse();
        UpdateCrosshairFromMouse(sender, e);
        e.Handled = true;
    }

    private void AnyGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed || e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _panQuadrant = GetQuadrant(sender);
            _panDragOrigin = e.GetPosition(this);
            
            if (_panQuadrant == Quadrant.Axial) _panDragStartOffset = _panAxial;
            else if (_panQuadrant == Quadrant.Coronal) _panDragStartOffset = _panCoronal;
            else if (_panQuadrant == Quadrant.Sagittal) _panDragStartOffset = _panSagittal;
            
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }
    }

    private void AnyGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _wlDragging = false;
        _isPanning = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void AnyGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_volume == null) return;
        
        if (_wlDragging)
        {
            var pos = e.GetPosition(this);
            double dx = pos.X - _wlDragOrigin.X;
            double dy = pos.Y - _wlDragOrigin.Y;
            _ww = Math.Max(1, _wlDragStartWw + (int)(dx * 4));
            _wl = _wlDragStartWl - (int)(dy * 4);
            RefreshAll();
            WlChanged?.Invoke(_ww, _wl);
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            UpdateCrosshairFromMouse(sender, e);
        }
        else if (_isPanning)
        {
            var pos = e.GetPosition(this);
            double dx = pos.X - _panDragOrigin.X;
            double dy = pos.Y - _panDragOrigin.Y;
            
            if (_panQuadrant == Quadrant.Axial)
                _panAxial = new Point(_panDragStartOffset.X + dx, _panDragStartOffset.Y + dy);
            else if (_panQuadrant == Quadrant.Coronal)
                _panCoronal = new Point(_panDragStartOffset.X + dx, _panDragStartOffset.Y + dy);
            else if (_panQuadrant == Quadrant.Sagittal)
                _panSagittal = new Point(_panDragStartOffset.X + dx, _panDragStartOffset.Y + dy);
                
            SyncZoom();
        }
        else
        {
            // Hover logic: update HU display 
            var q = GetQuadrant(sender);
            var p = e.GetPosition((UIElement)sender);
            int hx = _cx, hy = _cy, hz = _cz;
            if (q == Quadrant.Axial) { hx = Clamp((int)p.X, _volume.Columns); hy = Clamp((int)p.Y, _volume.Rows); }
            if (q == Quadrant.Coronal) { hx = Clamp((int)p.X, _volume.Columns); hz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount); }
            if (q == Quadrant.Sagittal) { hy = Clamp((int)p.X, _volume.Rows); hz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount); }
            FireHu(hx, hy, hz);
        }
    }

    private void UpdateCrosshairFromMouse(object sender, MouseEventArgs e)
    {
        if (_volume == null) return;
        var p = e.GetPosition((UIElement)sender);
        var q = GetQuadrant(sender);
        
        if (q == Quadrant.Axial)
        {
            _cx = Clamp((int)p.X, _volume.Columns);
            _cy = Clamp((int)p.Y, _volume.Rows);
        }
        else if (q == Quadrant.Coronal)
        {
            _cx = Clamp((int)p.X, _volume.Columns);
            _cz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount);
        }
        else if (q == Quadrant.Sagittal)
        {
            _cy = Clamp((int)p.X, _volume.Rows);
            _cz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount);
        }
        
        RefreshAll();
        // Also fire HU on drag
        FireHu(_cx, _cy, _cz);
    }

    private void AnyGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_volume == null) return;
        
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Zoom (Ctrl + Wheel)
            double zoomChange = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            _zoomFactor = Math.Max(0.1, Math.Min(20.0, _zoomFactor * zoomChange));
            SyncZoom();
        }
        else
        {
            // Scroll slices
            var q = GetQuadrant(sender);
            int delta = e.Delta > 0 ? 1 : -1;
            
            if (q == Quadrant.Axial)
                _cz = Math.Clamp(_cz + delta, 0, _volume.SliceCount - 1);
            else if (q == Quadrant.Coronal)
                _cy = Math.Clamp(_cy + delta, 0, _volume.Rows - 1);
            else if (q == Quadrant.Sagittal)
                _cx = Math.Clamp(_cx + delta, 0, _volume.Columns - 1);
                
            RefreshAll();
        }
    }

    // ── HU cursor helper ──────────────────────────────────────────────────


    private void FireHu(int x, int y, int z)
    {
        if (_volume == null) return;
        CursorHuChanged?.Invoke(_volume.GetVoxel(x, y, z));
    }

    // ── 3D bone preview ───────────────────────────────────────────────────

    /// <summary>Push a new bone mesh into the 3D preview panel (null clears it).</summary>
    public void SetBoneMesh(MeshData? mesh)
    {
        _boneMesh = mesh;
        UpdateBonePreview();
    }

    private void UpdateBonePreview()
    {
        // Clear previous content (keep lights)
        Preview3D.Children.Clear();
        Preview3D.Children.Add(new DefaultLights());

        if (_boneMesh == null || _boneMesh.VertexCount == 0)
        {
            NoMeshText.Visibility = Visibility.Visible;
            return;
        }

        NoMeshText.Visibility      = Visibility.Collapsed;
        ThresholdOverlay.Visibility = Visibility.Visible;

        var geo = BuildBoneGeometry(_boneMesh);

        // Ivory/white bone color
        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(245, 245, 240)));
        var model = new GeometryModel3D(geo, mat) { BackMaterial = mat };
        Preview3D.Children.Add(new ModelVisual3D { Content = model });

        // Compute centroid for orbiting
        var b = geo.Bounds;
        _orbitTarget = new Point3D(b.X + b.SizeX * 0.5, b.Y + b.SizeY * 0.5, b.Z + b.SizeZ * 0.5);

        // Fit camera from a 3/4 front-right-above angle then zoom to fit
        double d = b.SizeX > b.SizeZ ? b.SizeX : b.SizeZ;
        double r  = d * 2.2;
        Preview3D.Camera = new PerspectiveCamera
        {
            Position      = _orbitTarget + new Vector3D(r * 0.6, -r, r * 0.5),
            LookDirection = new Vector3D(-0.6, 1, -0.5),
            UpDirection   = new Vector3D(0, 0, 1),
            FieldOfView   = 40
        };
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            () => Preview3D.ZoomExtents(0));
    }

    private static MeshGeometry3D BuildBoneGeometry(MeshData data)
    {
        var geo       = new MeshGeometry3D();
        var positions = geo.Positions       = new Point3DCollection(data.VertexCount);
        var normals   = geo.Normals          = new Vector3DCollection(data.VertexCount);
        var indices   = geo.TriangleIndices  = new Int32Collection(data.Indices.Length);

        float[] p = data.Positions;
        float[] n = data.Normals;
        int     count = data.VertexCount;
        for (int i = 0; i < count; i++)
        {
            int o = i * 3;
            positions.Add(new Point3D(p[o], p[o + 1], p[o + 2]));
            normals.Add(new Vector3D(n[o], n[o + 1], n[o + 2]));
        }
        foreach (int idx in data.Indices)
            indices.Add(idx);

        return geo;
    }

    // ── 3D panel custom mouse handlers ───────────────────────────────────
    // We handle rotation/zoom/pan ourselves so it works on all touchpads.

    private bool  _3dDragging;
    private Point _3dDragOrigin;

    private void Preview3DContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Any click while the zone hint is visible → permanently dismiss it
        if (ZoneHintOverlay.Visibility == Visibility.Visible)
        {
            DismissZoneHint(permanent: true);
            e.Handled = true;
            return;
        }

        // Double-click → expand/collapse
        if (e.ClickCount == 2)
        {
            ToggleExpand(Quadrant.Preview3D);
            e.Handled = true;
            return;
        }

        // Single click → lock rotation zone and start drag
        var pos       = e.GetPosition(Preview3DContainer);
        _dragZone     = GetZone(pos);
        _3dDragging   = true;
        _3dDragOrigin = pos;
        Preview3DContainer.CaptureMouse();
        e.Handled = true;
    }

    private void Preview3DContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_3dDragging)
        {
            _3dDragging = false;
            Preview3DContainer.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void Preview3DContainer_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Preview3DContainer);

        if (!_3dDragging)
        {
            // Hover — update cursor icon, floating label, and maybe show zone hint
            var hz = GetZone(pos);
            UpdateZoneCursor(hz);
            UpdateZoneLabel(hz, pos);
            MaybeShowZoneHint(pos);
            return;
        }

        if (Preview3D.Camera is not PerspectiveCamera cam) return;

        double dx = pos.X - _3dDragOrigin.X;
        double dy = pos.Y - _3dDragOrigin.Y;
        _3dDragOrigin = pos;

        if (Math.Abs(dx) < 0.1 && Math.Abs(dy) < 0.1) return;

        ApplyZoneRotation(cam, _dragZone, dx, dy);
    }

    private void Preview3DContainer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Preview3D.Camera is not PerspectiveCamera cam) return;

        // Scroll wheel (and 2-finger touchpad scroll) → zoom in / out.
        // Use pow() so small precision-touchpad deltas feel proportional to large mouse notches.
        // Standard notch: Delta = ±120 → factor ≈ 0.887 / 1.127  (~11 % step)
        double factor    = Math.Pow(0.999, e.Delta);   // < 1 = closer, > 1 = farther
        var    toTarget  = _orbitTarget - cam.Position;
        double dist      = Math.Max(1.0, toTarget.Length * factor);
        var    look      = cam.LookDirection; look.Normalize();
        cam.Position     = _orbitTarget - look * dist;

        e.Handled = true;
    }

    private void Preview3DContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        ZoneLabelBorder.Visibility = Visibility.Collapsed;
        Preview3DContainer.Cursor  = null;   // revert to inherited default
    }

    // ── 3D panel touch / precision-touchpad manipulation ─────────────────
    // Fires on actual touch input (Windows Precision Touchpad with WM_POINTER
    // or a touchscreen).  Plain 2-finger scroll arrives via MouseWheel above.

    private void Preview3DContainer_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
    {
        e.ManipulationContainer = Preview3DContainer;
        e.Mode = ManipulationModes.TranslateX | ManipulationModes.TranslateY
               | ManipulationModes.Scale;
        _manipZoneReady = false; // will be set in ManipulationStarted
        e.Handled = true;
    }

    private void Preview3DContainer_ManipulationStarted(object sender, ManipulationStartedEventArgs e)
    {
        // ManipulationOrigin is the centroid of all touch contacts in Preview3DContainer space
        _manipZone      = GetZone(e.ManipulationOrigin);
        _manipZoneReady = true;
        e.Handled = true;
    }

    private void Preview3DContainer_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
    {
        if (Preview3D.Camera is not PerspectiveCamera cam) { e.Handled = true; return; }

        // Fallback: if ManipulationStarted never fired, derive zone from origin
        if (!_manipZoneReady)
        {
            _manipZone      = GetZone(e.ManipulationOrigin);
            _manipZoneReady = true;
        }

        // 2-finger translation → zone-aware rotation
        double dx = e.DeltaManipulation.Translation.X;
        double dy = e.DeltaManipulation.Translation.Y;
        if (Math.Abs(dx) > 0.3 || Math.Abs(dy) > 0.3)
            ApplyZoneRotation(cam, _manipZone, dx, dy);

        // 2-finger pinch / spread → zoom (zone-independent)
        double scale = e.DeltaManipulation.Scale.X;
        if (Math.Abs(scale - 1.0) > 0.001)
        {
            var toTarget = _orbitTarget - cam.Position;
            double dist  = Math.Max(1.0, toTarget.Length / scale);
            var look     = cam.LookDirection; look.Normalize();
            cam.Position = _orbitTarget - look * dist;
        }

        e.Handled = true;
    }

    private void Preview3DContainer_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
    {
        // Brief glide then stop; keeps the model from drifting indefinitely
        e.TranslationBehavior.DesiredDeceleration = 96.0 * 4 / (1000.0 * 1000.0);
        e.Handled = true;
    }

    // ── Zone-control system ───────────────────────────────────────────────
    // Zone map (relative to Preview3DContainer size):
    //   Y < 50 % and X <  25 % → top = Yaw,   bottom = PitchLeft
    //   Y < 50 % and X >  75 % → top = Roll,  bottom = PitchRight
    //   Y < 50 % and X 25–75 % → Center (unconstrained)
    //   Y ≥ 50 %                → Free  (unchanged existing orbit)

    private RotationZone GetZone(Point p)
    {
        double w = Preview3DContainer.ActualWidth;
        double h = Preview3DContainer.ActualHeight;
        if (w <= 0 || h <= 0) return RotationZone.Free;

        double rx = p.X / w;
        double ry = p.Y / h;

        if (ry >= 0.5) return RotationZone.Free;          // bottom half — free orbit

        if (rx < 0.375)      // left column now 37.5 % (was 25 %, +50 %)
            return ry < 0.25 ? RotationZone.Yaw : RotationZone.PitchLeft;
        if (rx > 0.75)       // right column unchanged at 25 %
            return ry < 0.25 ? RotationZone.Roll : RotationZone.PitchRight;
        return RotationZone.Center;
    }

    /// <summary>
    /// Applies a rotation to the camera that matches the zone's constrained axis.
    /// Named zones (Yaw/Roll/Pitch) use only the vertical drag component (dy) at 0.3 °/px.
    /// Center and Free use the original unconstrained orbit at 0.4 °/px.
    /// </summary>
    private void ApplyZoneRotation(PerspectiveCamera cam, RotationZone zone, double dx, double dy)
    {
        const double yawSens  = 1.0;   // deg/px — yaw needs stronger response on constrained axis
        const double zoneSens = 0.3;   // deg/px — ROLL / PITCH named zones
        const double freeSens = 0.4;   // deg/px — CENTER / FREE unconstrained orbit

        var look  = cam.LookDirection; look.Normalize();
        var up    = cam.UpDirection;   up.Normalize();
        var right = Vector3D.CrossProduct(look, up); right.Normalize();

        switch (zone)
        {
            case RotationZone.Yaw:
                // Vertical drag → yaw around world Z axis (the "up" axis in this viewer)
                // Z = superior-inferior in DICOM; rotating around it turns the nose left/right.
                RotateCameraOnAxis(cam, new Vector3D(0, 0, 1), dy * yawSens);
                break;

            case RotationZone.Roll:
                // Vertical drag → roll around camera look direction (head tilt)
                RotateCameraOnAxis(cam, look, dy * zoneSens);
                break;

            case RotationZone.PitchLeft:
            case RotationZone.PitchRight:
                // Vertical drag → pitch around camera right vector (head nod)
                RotateCameraOnAxis(cam, right, dy * zoneSens);
                break;

            default:   // Center + Free — original unconstrained orbit (dx + dy)
                var qYaw   = new System.Windows.Media.Media3D.Quaternion(up,    -dx * freeSens);
                var qPitch = new System.Windows.Media.Media3D.Quaternion(right,  -dy * freeSens);
                var rot    = new QuaternionRotation3D(qYaw * qPitch);
                var rotTx  = new RotateTransform3D(rot, _orbitTarget);
                cam.Position      = rotTx.Transform(cam.Position);
                cam.LookDirection = rotTx.Transform(cam.LookDirection);
                cam.UpDirection   = rotTx.Transform(cam.UpDirection);
                break;
        }
    }

    private void RotateCameraOnAxis(PerspectiveCamera cam, Vector3D axis, double angleDeg)
    {
        if (axis.Length < 1e-6) return;
        axis.Normalize();
        var q     = new System.Windows.Media.Media3D.Quaternion(axis, angleDeg);
        var rot   = new QuaternionRotation3D(q);
        var rotTx = new RotateTransform3D(rot, _orbitTarget);
        cam.Position      = rotTx.Transform(cam.Position);
        cam.LookDirection = rotTx.Transform(cam.LookDirection);
        cam.UpDirection   = rotTx.Transform(cam.UpDirection);
    }

    private void UpdateZoneCursor(RotationZone zone)
    {
        Preview3DContainer.Cursor = zone switch
        {
            RotationZone.Yaw        => Cursors.SizeWE,
            RotationZone.Roll       => Cursors.SizeNESW,
            RotationZone.PitchLeft  => Cursors.SizeNS,
            RotationZone.PitchRight => Cursors.SizeNS,
            _                       => Cursors.SizeAll,
        };
    }

    private void UpdateZoneLabel(RotationZone zone, Point pos)
    {
        string? label = zone switch
        {
            RotationZone.Yaw        => "⟳ YAW",
            RotationZone.Roll       => "⟳ ROLL",
            RotationZone.PitchLeft  => "↕ PITCH",
            RotationZone.PitchRight => "↕ PITCH",
            _                       => null,
        };

        if (label is null) { ZoneLabelBorder.Visibility = Visibility.Collapsed; return; }

        ZoneLabelText.Text         = label;
        ZoneLabelBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(ZoneLabelBorder, pos.X + 16);
        Canvas.SetTop(ZoneLabelBorder,  pos.Y - 8);
    }

    // ── Zone hint overlay ─────────────────────────────────────────────────

    private static bool ZoneHintDismissed =>
        System.IO.File.Exists(_hintFlagPath);

    /// <summary>
    /// Shows the zone-control overlay the first time the cursor enters the top 50 %
    /// of the 3D panel (skipped if already permanently dismissed).
    /// </summary>
    private void MaybeShowZoneHint(Point pos)
    {
        if (_zoneHintTriggered || ZoneHintDismissed) return;

        double ry = pos.Y / Math.Max(1.0, Preview3DContainer.ActualHeight);
        if (ry >= 0.5) return;  // only trigger from the zoned top half

        _zoneHintTriggered = true;
        ZoneHintOverlay.Visibility = Visibility.Visible;

        _zoneHintTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _zoneHintTimer.Tick += (_, _) => DismissZoneHint(permanent: false);
        _zoneHintTimer.Start();
    }

    /// <summary>
    /// Hides the zone-control overlay. When <paramref name="permanent"/> is true,
    /// writes a marker file so the hint is never shown again across sessions.
    /// </summary>
    private void DismissZoneHint(bool permanent)
    {
        _zoneHintTimer?.Stop();
        _zoneHintTimer = null;
        ZoneHintOverlay.Visibility = Visibility.Collapsed;

        if (permanent)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_hintFlagPath)!;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(_hintFlagPath, "dismissed");
            }
            catch { /* non-critical — hint will show again next session if write fails */ }
        }
    }

    // ── 2D metric grid overlay on the 3D preview ─────────────────────────

    private void Draw3DGridOverlay()
    {
        Preview3DGridCanvas.Children.Clear();

        double w = Preview3DGridCanvas.ActualWidth;
        double h = Preview3DGridCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Fixed 40-pixel spacing for minor lines, with majors every 5th line
        double spacing = 40.0;
        int majorEvery = 5;

        var minor = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        var major = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

        // Vertical lines
        for (double x = 0, i = 0; x <= w; x += spacing, i++)
        {
            bool isMajor = (int)i % majorEvery == 0;
            Preview3DGridCanvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = h,
                Stroke          = isMajor ? major : minor,
                StrokeThickness = isMajor ? 0.8 : 0.4,
                IsHitTestVisible = false
            });
        }

        // Horizontal lines
        for (double y = 0, i = 0; y <= h; y += spacing, i++)
        {
            bool isMajor = (int)i % majorEvery == 0;
            Preview3DGridCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = w, Y1 = y, Y2 = y,
                Stroke          = isMajor ? major : minor,
                StrokeThickness = isMajor ? 0.8 : 0.4,
                IsHitTestVisible = false
            });
        }
    }

    // Live bone threshold slider ─────────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _liveSliderTimer;

    private void LiveBoneSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Guard: fires during InitializeComponent before named elements exist
        if (LiveBoneLabel is null) return;

        int hu = (int)e.NewValue;
        LiveBoneLabel.Text = $"{hu} HU";

        // Debounce: wait 180 ms after last drag event before firing regeneration
        if (_liveSliderTimer == null)
        {
            _liveSliderTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _liveSliderTimer.Tick += (_, _) =>
            {
                _liveSliderTimer.Stop();
                LiveThresholdChanged?.Invoke((int)LiveBoneSlider.Value);
            };
        }
        _liveSliderTimer.Stop();
        _liveSliderTimer.Start();
    }

    // ── Expand / collapse ─────────────────────────────────────────────────

    private void ToggleExpand(Quadrant q)
    {
        if (_expanded == q)
        {
            CollapseAll();
            _expanded = Quadrant.None;
        }
        else
        {
            ExpandQuadrant(q);
            _expanded = q;
        }
    }

    private void ExpandQuadrant(Quadrant q)
    {
        HSeparator.Visibility    = Visibility.Collapsed;
        VSeparator.Visibility    = Visibility.Collapsed;
        AxialBorder.Visibility   = Visibility.Collapsed;
        CoronalBorder.Visibility = Visibility.Collapsed;
        SagittalBorder.Visibility= Visibility.Collapsed;
        InfoBorder.Visibility    = Visibility.Collapsed;

        var target = q switch
        {
            Quadrant.Coronal   => CoronalBorder,
            Quadrant.Sagittal  => SagittalBorder,
            Quadrant.Preview3D => InfoBorder,
            _                  => AxialBorder,
        };

        target.Visibility = Visibility.Visible;
        Grid.SetRow(target, 0);
        Grid.SetColumn(target, 0);
        Grid.SetRowSpan(target, 3);
        Grid.SetColumnSpan(target, 3);

        // Show axes widget when 3D panel is expanded
        Preview3D.ShowCoordinateSystem = (q == Quadrant.Preview3D);

        _expanded = q;
        SyncZoom();
    }

    private void CollapseAll()
    {
        RestoreQuadrant(AxialBorder,    0, 0);
        RestoreQuadrant(CoronalBorder,  0, 2);
        RestoreQuadrant(SagittalBorder, 2, 0);
        RestoreQuadrant(InfoBorder,     2, 2);

        HSeparator.Visibility          = Visibility.Visible;
        VSeparator.Visibility          = Visibility.Visible;
        Preview3D.ShowCoordinateSystem = false;

        SyncZoom();
    }

    private static void RestoreQuadrant(Border b, int row, int col)
    {
        b.Visibility = Visibility.Visible;
        Grid.SetRow(b, row);
        Grid.SetColumn(b, col);
        Grid.SetRowSpan(b, 1);
        Grid.SetColumnSpan(b, 1);
    }

    // ── Utility ───────────────────────────────────────────────────────────

    private static int Clamp(int v, int max) => Math.Clamp(v, 0, max - 1);
}
