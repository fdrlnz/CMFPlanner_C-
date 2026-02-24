using CMFPlanner.Core.Models;

namespace CMFPlanner.Visualization;

/// <summary>
/// Extracts an isosurface mesh from a <see cref="VolumeData"/> HU buffer.
/// </summary>
public interface IMeshExtractor
{
    /// <summary>
    /// Runs marching cubes on a background thread and returns the bone isosurface.
    /// </summary>
    /// <param name="volume">Source HU volume.</param>
    /// <param name="threshold">Isovalue in Hounsfield Units (default 400 = bone).</param>
    /// <param name="stride">Voxel step size — 1 = full resolution, 2 = half (faster).</param>
    Task<MeshData> ExtractAsync(
        VolumeData volume,
        short threshold = 400,
        int stride = 2,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default);
}
