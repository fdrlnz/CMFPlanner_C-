using System.Runtime.CompilerServices;
using CMFPlanner.Core.Models;
using FellowOakDicom;

[assembly: InternalsVisibleTo("CMFPlanner.Dicom.Tests")]

namespace CMFPlanner.Dicom;

public sealed class DicomLoader : IDicomLoader
{
    public async Task<DicomVolume> LoadSeriesAsync(
        string folderPath,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Enumerate candidate files (.dcm or extensionless)
        var allFiles = Directory.GetFiles(folderPath, "*.dcm", SearchOption.AllDirectories);
        if (allFiles.Length == 0)
        {
            allFiles = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(f => string.IsNullOrEmpty(Path.GetExtension(f)))
                .ToArray();
        }

        if (allFiles.Length == 0)
            throw new InvalidOperationException("No DICOM files found in the selected folder.");

        // 2. Read metadata from each file (skip pixel data)
        var slices = new List<SliceInfo>(allFiles.Length);
        int total = allFiles.Length;

        for (int i = 0; i < allFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var file = await DicomFile.OpenAsync(allFiles[i], FileReadOption.SkipLargeTags);
                var ds = file.Dataset;

                double[] ipp = GetDoubleArray(ds, DicomTag.ImagePositionPatient, 3);
                double[] iop = GetDoubleArray(ds, DicomTag.ImageOrientationPatient, 6);
                double geoKey = ComputeGeometrySortKey(ipp, iop);

                slices.Add(new SliceInfo
                {
                    FilePath              = allFiles[i],
                    SeriesInstanceUID     = GetString(ds, DicomTag.SeriesInstanceUID),
                    InstanceNumber        = GetInt(ds, DicomTag.InstanceNumber),
                    SliceLocation         = GetDoubleOrSentinel(ds, DicomTag.SliceLocation),
                    GeometrySortKey       = geoKey,
                    IppX                  = ipp.Length >= 1 ? ipp[0] : double.MinValue,
                    IppY                  = ipp.Length >= 2 ? ipp[1] : double.MinValue,
                    IppZ                  = ipp.Length >= 3 ? ipp[2] : double.MinValue,
                    PatientName           = GetString(ds, DicomTag.PatientName),
                    StudyDate             = GetString(ds, DicomTag.StudyDate),
                    Modality              = GetString(ds, DicomTag.Modality),
                    SeriesDescription     = GetString(ds, DicomTag.SeriesDescription),
                    PixelSpacingRow       = GetPixelSpacing(ds, 0),
                    PixelSpacingCol       = GetPixelSpacing(ds, 1),
                    SliceThickness        = GetPositiveDouble(ds, DicomTag.SliceThickness),
                    SpacingBetweenSlices  = GetPositiveDouble(ds, DicomTag.SpacingBetweenSlices),
                    Rows                  = GetInt(ds, DicomTag.Rows),
                    Columns               = GetInt(ds, DicomTag.Columns),
                });
            }
            catch
            {
                // Skip files that are not valid DICOM
            }

            progress?.Report((i + 1, total));
        }

        if (slices.Count == 0)
            throw new InvalidOperationException("No valid DICOM slices could be read from the selected folder.");

        // 3. Select dominant series (most-common SeriesInstanceUID)
        var dominantUID = slices
            .GroupBy(s => s.SeriesInstanceUID)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var series = slices.Where(s => s.SeriesInstanceUID == dominantUID).ToList();

        // 4. Sort: GeometrySortKey → SliceLocation → InstanceNumber → FilePath (tie-breaker)
        series.Sort(CompareSlices);

        // 5. Compute inter-slice spacing
        double spacingZ = ComputeSpacingZ(series);

        // 6. Extract origin from first slice (0 if IPP unavailable)
        var first = series[0];
        double originX = first.IppX != double.MinValue ? first.IppX : 0.0;
        double originY = first.IppY != double.MinValue ? first.IppY : 0.0;
        double originZ = first.IppZ != double.MinValue ? first.IppZ : 0.0;

        return new DicomVolume
        {
            SliceFilePaths    = series.Select(s => s.FilePath).ToList().AsReadOnly(),
            PatientName       = first.PatientName,
            StudyDate         = first.StudyDate,
            Modality          = first.Modality,
            SeriesDescription = first.SeriesDescription,
            SeriesInstanceUID = dominantUID,
            PixelSpacingX     = first.PixelSpacingCol,
            PixelSpacingY     = first.PixelSpacingRow,
            SliceThickness    = first.SliceThickness,
            Rows              = first.Rows,
            Columns           = first.Columns,
            SpacingZ          = spacingZ,
            OriginX           = originX,
            OriginY           = originY,
            OriginZ           = originZ,
        };
    }

    // ── Geometry sort key ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the signed projection of IPP onto the slice-normal derived from IOP.
    /// Normal = rowCosines × colCosines (cross product).
    /// Returns <see cref="double.MinValue"/> when IOP or IPP are absent/degenerate.
    /// </summary>
    private static double ComputeGeometrySortKey(double[] ipp, double[] iop)
    {
        if (ipp.Length < 3 || iop.Length < 6)
            return double.MinValue;

        // Row direction cosines F, column direction cosines G
        double fx = iop[0], fy = iop[1], fz = iop[2];
        double gx = iop[3], gy = iop[4], gz = iop[5];

        // Normal N = F × G
        double nx = fy * gz - fz * gy;
        double ny = fz * gx - fx * gz;
        double nz = fx * gy - fy * gx;

        double magnitude = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (magnitude < 1e-10)
            return double.MinValue;

        // N · IPP (no need to normalise — consistent ordering is all we need,
        // but we normalise so that adjacent gaps equal true mm spacing)
        return (nx * ipp[0] + ny * ipp[1] + nz * ipp[2]) / magnitude;
    }

    // ── Sort comparator ────────────────────────────────────────────────────

    /// <summary>
    /// Stable, deterministic slice ordering:
    /// 1. Geometry sort key (when both slices have valid IOP+IPP)
    /// 2. SliceLocation (when both slices have it)
    /// 3. InstanceNumber
    /// 4. FilePath (tie-breaker — ensures identical ordering on repeated loads)
    /// </summary>
    internal static int CompareSlices(SliceInfo a, SliceInfo b)
    {
        bool aHasGeo = a.GeometrySortKey != double.MinValue;
        bool bHasGeo = b.GeometrySortKey != double.MinValue;
        if (aHasGeo && bHasGeo)
        {
            int c = a.GeometrySortKey.CompareTo(b.GeometrySortKey);
            if (c != 0) return c;
        }
        else if (!aHasGeo && !bHasGeo)
        {
            // Both lack geometry — fall through to SliceLocation
        }
        else
        {
            // Mixed: one has geometry, one does not.
            // Fall through to SliceLocation/InstanceNumber to keep ordering
            // within what DICOM provides for the weaker slice.
        }

        bool aHasSL = a.SliceLocation != double.MinValue;
        bool bHasSL = b.SliceLocation != double.MinValue;
        if (aHasSL && bHasSL)
        {
            int c = a.SliceLocation.CompareTo(b.SliceLocation);
            if (c != 0) return c;
        }

        int instCmp = a.InstanceNumber.CompareTo(b.InstanceNumber);
        if (instCmp != 0) return instCmp;

        return string.Compare(a.FilePath, b.FilePath, StringComparison.Ordinal);
    }

    // ── Spacing computation ────────────────────────────────────────────────

    /// <summary>
    /// Computes the inter-slice spacing (mm) using the best available source:
    /// 1. Median of adjacent geometry-key gaps (most accurate for oblique/CBCT).
    /// 2. First positive SpacingBetweenSlices tag in the series.
    /// 3. First positive SliceThickness tag in the series.
    /// 4. 1.0 mm as last-resort fallback.
    /// </summary>
    internal static double ComputeSpacingZ(IReadOnlyList<SliceInfo> sorted)
    {
        // 1. Geometry-based median spacing
        var geoKeys = sorted
            .Where(s => s.GeometrySortKey != double.MinValue)
            .Select(s => s.GeometrySortKey)
            .ToList();

        if (geoKeys.Count >= 2)
        {
            var gaps = new List<double>(geoKeys.Count - 1);
            for (int i = 1; i < geoKeys.Count; i++)
            {
                double gap = geoKeys[i] - geoKeys[i - 1];
                if (gap > 0)
                    gaps.Add(gap);
            }
            if (gaps.Count > 0)
                return Median(gaps);
        }

        // 2. SpacingBetweenSlices
        foreach (var s in sorted)
        {
            if (s.SpacingBetweenSlices > 0)
                return s.SpacingBetweenSlices;
        }

        // 3. SliceThickness
        foreach (var s in sorted)
        {
            if (s.SliceThickness > 0)
                return s.SliceThickness;
        }

        // 4. Last-resort fallback
        return 1.0;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[mid - 1] + values[mid]) / 2.0
            : values[mid];
    }

    // ── Tag helpers ────────────────────────────────────────────────────────

    private static string GetString(DicomDataset ds, DicomTag tag)
    {
        try { return ds.GetSingleValueOrDefault<string>(tag, string.Empty); }
        catch { return string.Empty; }
    }

    private static int GetInt(DicomDataset ds, DicomTag tag)
    {
        try { return ds.GetSingleValueOrDefault<int>(tag, 0); }
        catch { return 0; }
    }

    private static double GetDoubleOrSentinel(DicomDataset ds, DicomTag tag)
    {
        try { return ds.GetSingleValueOrDefault<double>(tag, double.MinValue); }
        catch { return double.MinValue; }
    }

    /// <summary>Returns the tag value only when it is strictly positive; otherwise 0.</summary>
    private static double GetPositiveDouble(DicomDataset ds, DicomTag tag)
    {
        try
        {
            double v = ds.GetSingleValueOrDefault<double>(tag, 0.0);
            return v > 0 ? v : 0.0;
        }
        catch { return 0.0; }
    }

    private static double[] GetDoubleArray(DicomDataset ds, DicomTag tag, int minLength)
    {
        try
        {
            var vals = ds.GetValues<double>(tag);
            return vals.Length >= minLength ? vals : [];
        }
        catch { return []; }
    }

    private static double GetPixelSpacing(DicomDataset ds, int index)
    {
        try
        {
            var vals = ds.GetValues<double>(DicomTag.PixelSpacing);
            return vals.Length > index ? vals[index] : 0.0;
        }
        catch { return 0.0; }
    }

    // ── Internal DTO ───────────────────────────────────────────────────────

    internal sealed class SliceInfo
    {
        public string FilePath             { get; set; } = string.Empty;
        public string SeriesInstanceUID    { get; set; } = string.Empty;
        public int    InstanceNumber       { get; set; }
        public double SliceLocation        { get; set; }
        /// <summary>N·IPP where N is the slice normal; double.MinValue if IOP/IPP absent.</summary>
        public double GeometrySortKey      { get; set; }
        public double IppX                 { get; set; }
        public double IppY                 { get; set; }
        public double IppZ                 { get; set; }
        public string PatientName          { get; set; } = string.Empty;
        public string StudyDate            { get; set; } = string.Empty;
        public string Modality             { get; set; } = string.Empty;
        public string SeriesDescription    { get; set; } = string.Empty;
        public double PixelSpacingRow      { get; set; }
        public double PixelSpacingCol      { get; set; }
        public double SliceThickness       { get; set; }
        public double SpacingBetweenSlices { get; set; }
        public int    Rows                 { get; set; }
        public int    Columns              { get; set; }
    }
}
