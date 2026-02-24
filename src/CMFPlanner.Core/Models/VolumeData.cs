namespace CMFPlanner.Core.Models;

/// <summary>
/// 3D volume of Hounsfield Unit values built from a sorted DICOM series.
/// X = columns, Y = rows, Z = slices (ascending inferior → superior).
/// </summary>
public sealed class VolumeData
{
    public short[] HuBuffer  { get; init; } = [];
    public int     Columns   { get; init; }
    public int     Rows      { get; init; }
    public int     SliceCount{ get; init; }

    /// <summary>Voxel spacing along X (mm).</summary>
    public double SpacingX { get; init; }
    /// <summary>Voxel spacing along Y (mm).</summary>
    public double SpacingY { get; init; }
    /// <summary>Voxel spacing along Z / slice thickness (mm).</summary>
    public double SpacingZ { get; init; }

    /// <summary>World-space origin X (mm) from DICOM Image Position Patient.</summary>
    public double OriginX { get; init; }
    /// <summary>World-space origin Y (mm) from DICOM Image Position Patient.</summary>
    public double OriginY { get; init; }
    /// <summary>World-space origin Z (mm) from DICOM Image Position Patient.</summary>
    public double OriginZ { get; init; }

    public long TotalVoxels => (long)Columns * Rows * SliceCount;

    public short GetVoxel(int x, int y, int z)
        => HuBuffer[(long)z * Rows * Columns + (long)y * Columns + x];
}
