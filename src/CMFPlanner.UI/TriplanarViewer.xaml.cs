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

    // 2D grid overlay
    private bool _gridVisible = true;

    // 3D bone preview
    private MeshData? _boneMesh;
    private Point3D   _orbitTarget = new(0, 0, 0);

    // Right-click orbit state
    private bool  _orbitActive;
    private Point _orbitLast;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Fired when cursor moves over a slice; argument is the HU value under cursor.</summary>
    public event Action<short>? CursorHuChanged;

    /// <summary>Fired whenever Window or Level changes (drag or preset); args are (ww, wl).</summary>
    public event Action<int, int>? WlChanged;

    public TriplanarViewer()
    {
        InitializeComponent();
        AxialBorder.SizeChanged    += (_, _) => UpdateAllGridOverlays();
        CoronalBorder.SizeChanged  += (_, _) => UpdateAllGridOverlays();
        SagittalBorder.SizeChanged += (_, _) => UpdateAllGridOverlays();

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

        // Size the Viewbox inner grids to match bitmap dimensions
        SetGridSize(AxialGrid,    AxialCanvas,    AxialGridCanvas,    volume.Columns, volume.Rows);
        SetGridSize(CoronalGrid,  CoronalCanvas,  CoronalGridCanvas,  volume.Columns, volume.SliceCount);
        SetGridSize(SagittalGrid, SagittalCanvas, SagittalGridCanvas, volume.Rows,    volume.SliceCount);

        // Isotropic aspect ratio: scale each inner grid by physical voxel spacing so the
        // Viewbox sees physical-mm dimensions and preserves correct anatomical proportions.
        AxialGrid.LayoutTransform    = new ScaleTransform(volume.SpacingX, volume.SpacingY);
        CoronalGrid.LayoutTransform  = new ScaleTransform(volume.SpacingX, volume.SpacingZ);
        SagittalGrid.LayoutTransform = new ScaleTransform(volume.SpacingY, volume.SpacingZ);

        RefreshAll();
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
    }

    private void BtnPresetBone_Click(object sender, RoutedEventArgs e)
    {
        _ww = 2000; _wl = 400;
        RefreshAll();
        WlChanged?.Invoke(_ww, _wl);
    }

    // ── Right-drag W/L ────────────────────────────────────────────────────

    private void AnyGrid_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _wlDragging    = true;
        _wlDragOrigin  = Mouse.GetPosition(this);
        _wlDragStartWw = _ww;
        _wlDragStartWl = _wl;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void AnyGrid_RightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _wlDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ApplyWlDrag()
    {
        if (!_wlDragging || _volume == null) return;
        var pos = Mouse.GetPosition(this);
        double dx =  pos.X - _wlDragOrigin.X;
        double dy =  pos.Y - _wlDragOrigin.Y;
        _ww = Math.Max(1, _wlDragStartWw + (int)(dx * 4));
        _wl = _wlDragStartWl - (int)(dy * 4);
        RefreshAll();
        WlChanged?.Invoke(_ww, _wl);
    }

    // ── Axial mouse events ────────────────────────────────────────────────

    private void AxialGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleExpand(Quadrant.Axial); return; }
        if (_volume == null) return;
        var p = e.GetPosition((UIElement)sender);
        _cx = Clamp((int)p.X, _volume.Columns);
        _cy = Clamp((int)p.Y, _volume.Rows);
        RefreshAll();
    }

    private void AxialGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_wlDragging) { ApplyWlDrag(); return; }
        if (e.LeftButton != MouseButtonState.Pressed || _volume == null) return;
        var p = e.GetPosition((UIElement)sender);
        _cx = Clamp((int)p.X, _volume.Columns);
        _cy = Clamp((int)p.Y, _volume.Rows);
        RefreshAll();
        FireHu(Clamp((int)p.X, _volume.Columns), Clamp((int)p.Y, _volume.Rows), _cz);
    }

    private void AxialGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_volume == null) return;
        _cz = Math.Clamp(_cz + (e.Delta > 0 ? 1 : -1), 0, _volume.SliceCount - 1);
        RefreshAll();
    }

    // ── Coronal mouse events ──────────────────────────────────────────────

    private void CoronalGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleExpand(Quadrant.Coronal); return; }
        if (_volume == null) return;
        var p = e.GetPosition((UIElement)sender);
        _cx = Clamp((int)p.X, _volume.Columns);
        _cz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount);
        RefreshAll();
    }

    private void CoronalGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_wlDragging) { ApplyWlDrag(); return; }
        if (e.LeftButton != MouseButtonState.Pressed || _volume == null) return;
        var p = e.GetPosition((UIElement)sender);
        _cx = Clamp((int)p.X, _volume.Columns);
        _cz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount);
        RefreshAll();
        FireHu(_cx, _cy, _cz);
    }

    private void CoronalGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_volume == null) return;
        _cy = Math.Clamp(_cy + (e.Delta > 0 ? 1 : -1), 0, _volume.Rows - 1);
        RefreshAll();
    }

    // ── Sagittal mouse events ─────────────────────────────────────────────

    private void SagittalGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleExpand(Quadrant.Sagittal); return; }
        if (_volume == null) return;
        var p = e.GetPosition((UIElement)sender);
        _cy = Clamp((int)p.X, _volume.Rows);
        _cz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount);
        RefreshAll();
    }

    private void SagittalGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_wlDragging) { ApplyWlDrag(); return; }
        if (e.LeftButton != MouseButtonState.Pressed || _volume == null) return;
        var p = e.GetPosition((UIElement)sender);
        _cy = Clamp((int)p.X, _volume.Rows);
        _cz = _volume.SliceCount - 1 - Clamp((int)p.Y, _volume.SliceCount);
        RefreshAll();
        FireHu(_cx, _cy, _cz);
    }

    private void SagittalGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_volume == null) return;
        _cx = Math.Clamp(_cx + (e.Delta > 0 ? 1 : -1), 0, _volume.Columns - 1);
        RefreshAll();
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

        NoMeshText.Visibility = Visibility.Collapsed;

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

    // ── 3D panel mouse handlers ───────────────────────────────────────────

    private void Preview3D_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleExpand(Quadrant.Preview3D);
            e.Handled = true; // stop HelixToolkit starting rotation on the second click
        }
    }

    private void Preview3D_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _orbitActive = true;
        _orbitLast   = e.GetPosition(Preview3D);
        Preview3D.CaptureMouse();
        e.Handled = true;   // suppress context menu and HelixToolkit zoom
    }

    private void Preview3D_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _orbitActive = false;
        Preview3D.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Preview3D_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_orbitActive || Preview3D.Camera is not PerspectiveCamera cam) return;
        var pos = e.GetPosition(Preview3D);
        double dx = pos.X - _orbitLast.X;
        double dy = pos.Y - _orbitLast.Y;
        _orbitLast = pos;
        OrbitCamera(cam, _orbitTarget, dx, dy);
    }

    private static void OrbitCamera(PerspectiveCamera cam, Point3D target, double dx, double dy)
    {
        const double sensitivity = 0.4 * Math.PI / 180.0;
        var offset = cam.Position - target;
        double r     = offset.Length;
        double theta = Math.Atan2(offset.Y, offset.X) - dx * sensitivity;
        double phi   = Math.Acos(Math.Clamp(offset.Z / r, -1.0, 1.0)) + dy * sensitivity;
        phi = Math.Clamp(phi, 0.04, Math.PI - 0.04);   // prevent gimbal pole

        var newOffset = new Vector3D(
            r * Math.Sin(phi) * Math.Cos(theta),
            r * Math.Sin(phi) * Math.Sin(theta),
            r * Math.Cos(phi));

        cam.Position      = target + newOffset;
        cam.LookDirection = -newOffset;
        cam.UpDirection   = new Vector3D(0, 0, 1);
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
    }

    private void CollapseAll()
    {
        RestoreQuadrant(AxialBorder,    0, 0);
        RestoreQuadrant(CoronalBorder,  0, 2);
        RestoreQuadrant(SagittalBorder, 2, 0);
        RestoreQuadrant(InfoBorder,     2, 2);

        HSeparator.Visibility         = Visibility.Visible;
        VSeparator.Visibility         = Visibility.Visible;
        Preview3D.ShowCoordinateSystem = false;
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
