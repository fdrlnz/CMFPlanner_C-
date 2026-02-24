using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CMFPlanner.UI;

/// <summary>
/// Adaptive metric reference grid rendered on the XZ plane (Y = 0).
/// Cell spacing switches automatically between 10 mm, 5 mm, and 1 mm
/// based on the estimated visible world range derived from camera state.
/// Every 10th line is rendered brighter as a major reference gridline.
/// </summary>
internal sealed class AdaptiveGridVisual3D : ModelVisual3D
{
    private readonly LinesVisual3D _minor = new() { Color = Color.FromRgb( 80,  80,  80), Thickness = 0.3 };
    private readonly LinesVisual3D _major = new() { Color = Color.FromRgb(120, 120, 120), Thickness = 0.5 };

    // Cached state — avoids rebuilding on every tiny camera delta
    private double _prevSpacing = -1;
    private double _prevCx, _prevCz, _prevExtent;

    internal AdaptiveGridVisual3D()
    {
        Children.Add(_minor);
        Children.Add(_major);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Recomputes and redraws the grid for the given camera state.</summary>
    internal void Update(PerspectiveCamera cam)
    {
        var pos = cam.Position;

        // Camera distance from origin — proxy for scene scale
        double D       = Math.Max(Math.Sqrt(pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z), 1.0);
        double halfFov = cam.FieldOfView * Math.PI / 360.0;   // half-angle in radians
        double visible = 2.0 * D * Math.Tan(halfFov);

        double spacing = visible switch
        {
            <= 10 => 1.0,
            <= 50 => 5.0,
            _     => 10.0,
        };

        // Project camera look-ray onto Y = 0 to find grid centre
        double cx = RayOnFloor(pos, cam.LookDirection, axis: 0);
        double cz = RayOnFloor(pos, cam.LookDirection, axis: 2);

        // Snap centre to nearest 10-cell boundary — reduces shimmer
        double snap = spacing * 10;
        cx = Math.Round(cx / snap) * snap;
        cz = Math.Round(cz / snap) * snap;

        // Grid half-extent: wide enough to fill the viewport plus margin
        double extent = Math.Ceiling(visible * 2.0 / snap) * snap;
        extent = Math.Max(extent, snap * 2);

        // Skip rebuild when nothing meaningful has changed
        if (spacing == _prevSpacing
            && Math.Abs(cx     - _prevCx)     < snap
            && Math.Abs(cz     - _prevCz)     < snap
            && Math.Abs(extent - _prevExtent) < snap)
            return;

        _prevSpacing = spacing;
        _prevCx      = cx;
        _prevCz      = cz;
        _prevExtent  = extent;

        Rebuild(spacing, cx, cz, extent);
    }

    // ── Internals ──────────────────────────────────────────────────────────

    private void Rebuild(double spacing, double cx, double cz, double extent)
    {
        int steps = Math.Min((int)Math.Ceiling(extent / spacing), 200);  // performance cap

        double xMin = cx - steps * spacing;
        double xMax = cx + steps * spacing;
        double zMin = cz - steps * spacing;
        double zMax = cz + steps * spacing;
        const double Y = 0.0;

        var minorPts = new Point3DCollection(steps * 8);
        var majorPts = new Point3DCollection((steps / 5 + 4) * 8);

        for (int i = -steps; i <= steps; i++)
        {
            bool isMajor = (i % 10 == 0);
            var  col     = isMajor ? majorPts : minorPts;

            double x = cx + i * spacing;
            double z = cz + i * spacing;

            // Line parallel to Z axis at column x
            col.Add(new Point3D(x, Y, zMin));
            col.Add(new Point3D(x, Y, zMax));

            // Line parallel to X axis at row z
            col.Add(new Point3D(xMin, Y, z));
            col.Add(new Point3D(xMax, Y, z));
        }

        _minor.Points = minorPts;
        _major.Points = majorPts;
    }

    /// <summary>
    /// Returns the X (axis=0) or Z (axis=2) coordinate where the camera ray
    /// intersects the Y = 0 plane.  Falls back to the camera position component
    /// when the ray is nearly parallel to the floor.
    /// </summary>
    private static double RayOnFloor(Point3D pos, Vector3D dir, int axis)
    {
        if (Math.Abs(dir.Y) > 1e-6)
        {
            double t = -pos.Y / dir.Y;
            if (t > 0)
                return axis == 0 ? pos.X + t * dir.X : pos.Z + t * dir.Z;
        }
        return axis == 0 ? pos.X : pos.Z;
    }
}
