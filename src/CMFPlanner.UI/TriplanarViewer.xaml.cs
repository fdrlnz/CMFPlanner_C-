using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CMFPlanner.Core.Models;

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
    private int _ww = 400, _wl = 40;                  // soft-tissue default

    // Right-drag W/L state
    private bool  _wlDragging;
    private Point _wlDragOrigin;
    private int   _wlDragStartWw, _wlDragStartWl;

    // Expanded quadrant
    private enum Quadrant { None, Axial, Coronal, Sagittal }
    private Quadrant _expanded = Quadrant.None;

    // 2D grid overlay
    private bool _gridVisible = true;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Fired when cursor moves over a slice; argument is the HU value under cursor.</summary>
    public event Action<short>? CursorHuChanged;

    public TriplanarViewer()
    {
        InitializeComponent();
        AxialBorder.SizeChanged    += (_, _) => UpdateAllGridOverlays();
        CoronalBorder.SizeChanged  += (_, _) => UpdateAllGridOverlays();
        SagittalBorder.SizeChanged += (_, _) => UpdateAllGridOverlays();
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

        // Populate info panel
        InfoPatient.Text    = patientLabel;
        InfoDimensions.Text = $"{volume.Columns} × {volume.Rows} × {volume.SliceCount} vx";
        InfoSpacing.Text    = $"{volume.SpacingX:F2} × {volume.SpacingY:F2} × {volume.SpacingZ:F2} mm";

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

        // Viewbox effective scale: pixels per voxel (Stretch=Uniform)
        double scale    = Math.Min(borderW / imgW, borderH / imgH);
        double pxPerMm  = scale / spacingX;
        double px10mm   = pxPerMm * 10.0;

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

    private void UpdateWlDisplay()
        => InfoWL.Text = $"W {_ww}  ·  L {_wl}";

    // ── W/L presets ───────────────────────────────────────────────────────

    private void BtnPresetSoftTissue_Click(object sender, RoutedEventArgs e)
    {
        _ww = 400; _wl = 40;
        RefreshAll();
    }

    private void BtnPresetBone_Click(object sender, RoutedEventArgs e)
    {
        _ww = 2000; _wl = 400;
        RefreshAll();
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
            Quadrant.Coronal  => CoronalBorder,
            Quadrant.Sagittal => SagittalBorder,
            _                 => AxialBorder,
        };

        target.Visibility = Visibility.Visible;
        Grid.SetRow(target, 0);
        Grid.SetColumn(target, 0);
        Grid.SetRowSpan(target, 3);
        Grid.SetColumnSpan(target, 3);
        _expanded = q;
    }

    private void CollapseAll()
    {
        // Restore original grid positions
        RestoreQuadrant(AxialBorder,    0, 0);
        RestoreQuadrant(CoronalBorder,  0, 2);
        RestoreQuadrant(SagittalBorder, 2, 0);
        RestoreQuadrant(InfoBorder,     2, 2);

        HSeparator.Visibility = Visibility.Visible;
        VSeparator.Visibility = Visibility.Visible;
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
