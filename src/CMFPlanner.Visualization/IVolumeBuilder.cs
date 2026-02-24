using CMFPlanner.Core.Models;

namespace CMFPlanner.Visualization;

public interface IVolumeBuilder
{
    /// <summary>
    /// Reads pixel data from every slice in <paramref name="volume"/>,
    /// applies RescaleSlope/Intercept → HU, and returns a <see cref="VolumeData"/>.
    /// </summary>
    Task<VolumeData> BuildAsync(
        DicomVolume volume,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default);
}
