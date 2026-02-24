using CMFPlanner.Core.Models;
using FellowOakDicom;

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

                slices.Add(new SliceInfo
                {
                    FilePath              = allFiles[i],
                    SeriesInstanceUID     = GetString(ds, DicomTag.SeriesInstanceUID),
                    InstanceNumber        = GetInt(ds, DicomTag.InstanceNumber),
                    SliceLocation         = GetDoubleOrMin(ds, DicomTag.SliceLocation),
                    Ipp                   = GetVector3(ds, DicomTag.ImagePositionPatient),
                    Iop                   = GetVector6(ds, DicomTag.ImageOrientationPatient),
                    PatientName           = GetString(ds, DicomTag.PatientName),
                    StudyDate             = GetString(ds, DicomTag.StudyDate),
                    Modality              = GetString(ds, DicomTag.Modality),
                    SeriesDescription     = GetString(ds, DicomTag.SeriesDescription),
                    PixelSpacingRow       = GetPixelSpacing(ds, 0),
                    PixelSpacingCol       = GetPixelSpacing(ds, 1),
                    SliceThickness        = GetDouble(ds, DicomTag.SliceThickness),
                    SpacingBetweenSlices  = GetDouble(ds, DicomTag.SpacingBetweenSlices),
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
        if (series.Count == 0)
            throw new InvalidOperationException("No DICOM series could be selected.");

        ValidateSeriesDimensions(series);

        // 4. Sort slices by geometry first, then metadata fallbacks
        var normal = TryGetSliceNormal(series[0]);
        if (normal is not null)
        {
            foreach (var s in series)
            {
                if (s.Ipp is not null)
                {
                    s.GeometrySortKey = s.Ipp[0] * normal[0] + s.Ipp[1] * normal[1] + s.Ipp[2] * normal[2];
                }
            }
        }

        series.Sort((a, b) =>
        {
            if (a.GeometrySortKey.HasValue && b.GeometrySortKey.HasValue)
                return a.GeometrySortKey.Value.CompareTo(b.GeometrySortKey.Value);
            if (a.SliceLocation != double.MinValue && b.SliceLocation != double.MinValue)
                return a.SliceLocation.CompareTo(b.SliceLocation);
            return a.InstanceNumber.CompareTo(b.InstanceNumber);
        });

        // 5. Build result from first slice metadata
        var first = series[0];
        var spacingZ = ComputeSpacingZ(series, normal);
        var origin = first.Ipp ?? [0.0, 0.0, 0.0];

        return new DicomVolume
        {
            SliceFilePaths         = series.Select(s => s.FilePath).ToList().AsReadOnly(),
            PatientName            = first.PatientName,
            StudyDate              = first.StudyDate,
            Modality               = first.Modality,
            SeriesDescription      = first.SeriesDescription,
            SeriesInstanceUID      = dominantUID,
            PixelSpacingX          = first.PixelSpacingCol > 0 ? first.PixelSpacingCol : 1.0,
            PixelSpacingY          = first.PixelSpacingRow > 0 ? first.PixelSpacingRow : 1.0,
            SliceThickness         = first.SliceThickness,
            SpacingBetweenSlices   = first.SpacingBetweenSlices,
            SpacingZ               = spacingZ,
            OriginX                = origin[0],
            OriginY                = origin[1],
            OriginZ                = origin[2],
            Rows                   = first.Rows,
            Columns                = first.Columns,
        };
    }

    private static void ValidateSeriesDimensions(List<SliceInfo> series)
    {
        var rows = series[0].Rows;
        var cols = series[0].Columns;
        if (series.Any(s => s.Rows != rows || s.Columns != cols))
            throw new InvalidOperationException("Inconsistent DICOM slice dimensions in selected series.");
    }

    private static double[]? TryGetSliceNormal(SliceInfo first)
    {
        if (first.Iop is null || first.Iop.Length < 6)
            return null;

        var row = new[] { first.Iop[0], first.Iop[1], first.Iop[2] };
        var col = new[] { first.Iop[3], first.Iop[4], first.Iop[5] };

        var normal = new[]
        {
            row[1] * col[2] - row[2] * col[1],
            row[2] * col[0] - row[0] * col[2],
            row[0] * col[1] - row[1] * col[0],
        };

        var mag = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
        if (mag < 1e-8)
            return null;

        return [normal[0] / mag, normal[1] / mag, normal[2] / mag];
    }

    private static double ComputeSpacingZ(List<SliceInfo> series, double[]? normal)
    {
        var deltas = new List<double>(Math.Max(0, series.Count - 1));

        if (series.Count > 1 && normal is not null && series.All(s => s.Ipp is not null && s.GeometrySortKey.HasValue))
        {
            for (int i = 1; i < series.Count; i++)
            {
                double dz = Math.Abs(series[i].GeometrySortKey!.Value - series[i - 1].GeometrySortKey!.Value);
                if (dz > 0) deltas.Add(dz);
            }
        }

        if (deltas.Count > 0)
        {
            deltas.Sort();
            return deltas[deltas.Count / 2];
        }

        var first = series[0];
        if (first.SpacingBetweenSlices > 0) return first.SpacingBetweenSlices;
        if (first.SliceThickness > 0) return first.SliceThickness;
        return 1.0;
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

    private static double GetDouble(DicomDataset ds, DicomTag tag)
    {
        try { return ds.GetSingleValueOrDefault<double>(tag, 0.0); }
        catch { return 0.0; }
    }

    private static double GetDoubleOrMin(DicomDataset ds, DicomTag tag)
    {
        try { return ds.GetSingleValueOrDefault<double>(tag, double.MinValue); }
        catch { return double.MinValue; }
    }

    private static double[]? GetVector3(DicomDataset ds, DicomTag tag)
    {
        try
        {
            var vals = ds.GetValues<double>(tag);
            return vals.Length >= 3 ? [vals[0], vals[1], vals[2]] : null;
        }
        catch { return null; }
    }

    private static double[]? GetVector6(DicomDataset ds, DicomTag tag)
    {
        try
        {
            var vals = ds.GetValues<double>(tag);
            return vals.Length >= 6 ? [vals[0], vals[1], vals[2], vals[3], vals[4], vals[5]] : null;
        }
        catch { return null; }
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

    private sealed class SliceInfo
    {
        public string FilePath             { get; set; } = string.Empty;
        public string SeriesInstanceUID    { get; set; } = string.Empty;
        public int InstanceNumber          { get; set; }
        public double SliceLocation        { get; set; }
        public double[]? Ipp               { get; set; }
        public double[]? Iop               { get; set; }
        public double? GeometrySortKey     { get; set; }
        public string PatientName          { get; set; } = string.Empty;
        public string StudyDate            { get; set; } = string.Empty;
        public string Modality             { get; set; } = string.Empty;
        public string SeriesDescription    { get; set; } = string.Empty;
        public double PixelSpacingRow      { get; set; }
        public double PixelSpacingCol      { get; set; }
        public double SliceThickness       { get; set; }
        public double SpacingBetweenSlices { get; set; }
        public int Rows                    { get; set; }
        public int Columns                 { get; set; }
    }
}
