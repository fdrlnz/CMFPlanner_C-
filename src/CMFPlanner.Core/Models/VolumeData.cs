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

    /// <summary>
    /// Returns a new <see cref="VolumeData"/> with all HU values clamped to
    /// <paramref name="maxHu"/>.  The original buffer is not mutated.
    /// Metal implants produce HU spikes above 3000; clamping to 2500 removes them
    /// without affecting real cortical bone (~400–1800 HU).
    /// </summary>
    public static VolumeData WithClampedHu(VolumeData source, short maxHu)
    {
        var buf = (short[])source.HuBuffer.Clone();
        for (int i = 0; i < buf.Length; i++)
            if (buf[i] > maxHu) buf[i] = maxHu;

        return new VolumeData
        {
            HuBuffer   = buf,
            Columns    = source.Columns,
            Rows       = source.Rows,
            SliceCount = source.SliceCount,
            SpacingX   = source.SpacingX,
            SpacingY   = source.SpacingY,
            SpacingZ   = source.SpacingZ,
            OriginX    = source.OriginX,
            OriginY    = source.OriginY,
            OriginZ    = source.OriginZ,
        };
    }
}
