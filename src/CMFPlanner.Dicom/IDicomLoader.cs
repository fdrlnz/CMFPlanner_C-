using CMFPlanner.Core.Models;

namespace CMFPlanner.Dicom;

public interface IDicomLoader
{
    /// <summary>
    /// Scans <paramref name="folderPath"/> for DICOM files, selects the dominant
    /// series, sorts slices, and returns a <see cref="DicomVolume"/> with metadata.
    /// </summary>
    Task<DicomVolume> LoadSeriesAsync(
        string folderPath,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default);
}
