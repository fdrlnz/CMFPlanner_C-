namespace CMFPlanner.Core.Models;

/// <summary>
/// Represents a loaded DICOM CT/CBCT series — metadata and sorted slice paths.
/// Pixel data is read on demand by the visualization layer.
/// </summary>
public sealed class DicomVolume
{
    /// <summary>Sorted slice file paths (ascending Z — inferior to superior).</summary>
    public IReadOnlyList<string> SliceFilePaths { get; init; } = [];

    public string PatientName       { get; init; } = string.Empty;
    public string StudyDate         { get; init; } = string.Empty;
    public string Modality          { get; init; } = string.Empty;
    public string SeriesDescription { get; init; } = string.Empty;
    public string SeriesInstanceUID { get; init; } = string.Empty;

    /// <summary>Pixel spacing along columns (mm).</summary>
    public double PixelSpacingX { get; init; }

    /// <summary>Pixel spacing along rows (mm).</summary>
    public double PixelSpacingY { get; init; }

    /// <summary>Nominal slice thickness (mm).</summary>
    public double SliceThickness { get; init; }

    public int Rows    { get; init; }
    public int Columns { get; init; }

    /// <summary>
    /// Computed inter-slice spacing along the scan direction (mm).
    /// Derived from adjacent IPP geometry (median), then SpacingBetweenSlices,
    /// then SliceThickness, then 1.0 as last-resort fallback.
    /// </summary>
    public double SpacingZ { get; init; }

    /// <summary>Image Position Patient X of the first (inferior-most) slice (mm). 0 if unavailable.</summary>
    public double OriginX { get; init; }
    /// <summary>Image Position Patient Y of the first (inferior-most) slice (mm). 0 if unavailable.</summary>
    public double OriginY { get; init; }
    /// <summary>Image Position Patient Z of the first (inferior-most) slice (mm). 0 if unavailable.</summary>
    public double OriginZ { get; init; }

    public int SliceCount => SliceFilePaths.Count;
}
