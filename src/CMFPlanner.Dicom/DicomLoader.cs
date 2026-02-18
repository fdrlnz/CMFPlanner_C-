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
                    FilePath           = allFiles[i],
                    SeriesInstanceUID  = GetString(ds, DicomTag.SeriesInstanceUID),
                    InstanceNumber     = GetInt(ds,    DicomTag.InstanceNumber),
                    SliceLocation      = GetDoubleOrMin(ds, DicomTag.SliceLocation),
                    IppZ               = GetIppZ(ds),
                    PatientName        = GetString(ds, DicomTag.PatientName),
                    StudyDate          = GetString(ds, DicomTag.StudyDate),
                    Modality           = GetString(ds, DicomTag.Modality),
                    SeriesDescription  = GetString(ds, DicomTag.SeriesDescription),
                    PixelSpacingRow    = GetPixelSpacing(ds, 0),   // row spacing = Y
                    PixelSpacingCol    = GetPixelSpacing(ds, 1),   // col spacing = X
                    SliceThickness     = GetDouble(ds,  DicomTag.SliceThickness),
                    Rows               = GetInt(ds,    DicomTag.Rows),
                    Columns            = GetInt(ds,    DicomTag.Columns),
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

        // 4. Sort: IPP-Z → SliceLocation → InstanceNumber (fallback chain)
        series.Sort((a, b) =>
        {
            if (a.IppZ != double.MinValue && b.IppZ != double.MinValue)
                return a.IppZ.CompareTo(b.IppZ);
            if (a.SliceLocation != double.MinValue && b.SliceLocation != double.MinValue)
                return a.SliceLocation.CompareTo(b.SliceLocation);
            return a.InstanceNumber.CompareTo(b.InstanceNumber);
        });

        // 5. Build result from first slice metadata
        var first = series[0];

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
        };
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

    private static double GetIppZ(DicomDataset ds)
    {
        try
        {
            var vals = ds.GetValues<double>(DicomTag.ImagePositionPatient);
            return vals.Length >= 3 ? vals[2] : double.MinValue;
        }
        catch { return double.MinValue; }
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
        public string FilePath          { get; set; } = string.Empty;
        public string SeriesInstanceUID { get; set; } = string.Empty;
        public int    InstanceNumber    { get; set; }
        public double SliceLocation     { get; set; }
        public double IppZ              { get; set; }
        public string PatientName       { get; set; } = string.Empty;
        public string StudyDate         { get; set; } = string.Empty;
        public string Modality          { get; set; } = string.Empty;
        public string SeriesDescription { get; set; } = string.Empty;
        public double PixelSpacingRow   { get; set; }
        public double PixelSpacingCol   { get; set; }
        public double SliceThickness    { get; set; }
        public int    Rows              { get; set; }
        public int    Columns           { get; set; }
    }
}
