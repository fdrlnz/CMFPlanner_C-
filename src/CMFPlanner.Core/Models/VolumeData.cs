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
    /// <summary>Voxel spacing along Z (mm).</summary>
    public double SpacingZ { get; init; }

    /// <summary>Patient-space origin of the first sorted slice.</summary>
    public double OriginX { get; init; }
    public double OriginY { get; init; }
    public double OriginZ { get; init; }

    public long TotalVoxels => (long)Columns * Rows * SliceCount;

    public short GetVoxel(int x, int y, int z)
        => HuBuffer[(long)z * Rows * Columns + (long)y * Columns + x];
}
