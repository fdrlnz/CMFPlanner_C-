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

    /// <summary>Distance between slice centres when present (mm).</summary>
    public double SpacingBetweenSlices { get; init; }

    /// <summary>Resolved spacing along Z (mm) after metadata/geometry fallback.</summary>
    public double SpacingZ { get; init; }

    /// <summary>Patient-space origin (ImagePositionPatient) of the first sorted slice.</summary>
    public double OriginX { get; init; }
    public double OriginY { get; init; }
    public double OriginZ { get; init; }

    public int Rows    { get; init; }
    public int Columns { get; init; }

    public int SliceCount => SliceFilePaths.Count;
}
