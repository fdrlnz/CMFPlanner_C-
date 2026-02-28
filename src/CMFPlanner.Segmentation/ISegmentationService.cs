using CMFPlanner.Core.Models;
using CMFPlanner.Visualization;

namespace CMFPlanner.Segmentation;

/// <summary>
/// Provides isosurface extraction for bone segmentation.
/// </summary>
public interface ISegmentationService
{
    /// <summary>
    /// Extracts the bone isosurface at <paramref name="thresholdHu"/> on a background thread.
    /// </summary>
    Task<MeshData> ExtractBoneSurfaceAsync(
        VolumeData volume,
        short thresholdHu,
        int stride,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Full-resolution pipeline: stride-1 marching cubes → Taubin smoothing (20 iterations)
    /// → vertex-clustering decimation (bone ≤ 300 k triangles).
    /// Reports human-readable step descriptions via <paramref name="progress"/>.
    /// </summary>
    Task<MeshData> ApplyFinalSegmentationAsync(
        VolumeData volume,
        short boneThresholdHu,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
