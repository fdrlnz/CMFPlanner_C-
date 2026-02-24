using System.Runtime.CompilerServices;
using CMFPlanner.Core.Models;
using FellowOakDicom;
using FellowOakDicom.Imaging;

[assembly: InternalsVisibleTo("CMFPlanner.Visualization.Tests")]

namespace CMFPlanner.Visualization;

/// <summary>
/// Builds a <see cref="VolumeData"/> (HU buffer) from a sorted DICOM series.
/// Runs entirely on a thread-pool thread so the UI stays responsive.
/// VTK rendering integration is handled in E2-T3.
/// </summary>
public sealed class VtkVolumeBuilder : IVolumeBuilder
{
    public Task<VolumeData> BuildAsync(
        DicomVolume volume,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => Build(volume, progress, ct), ct);
    }

    private static VolumeData Build(
        DicomVolume volume,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken ct)
    {
        int nx = volume.Columns;
        int ny = volume.Rows;
        int nz = volume.SliceCount;
        int sliceVoxels  = nx * ny;
        long totalVoxels = (long)sliceVoxels * nz;

        var huBuffer = new short[totalVoxels];

        for (int z = 0; z < nz; z++)
        {
            ct.ThrowIfCancellationRequested();

            // Synchronous read — we're already on a background thread
            var file = DicomFile.Open(volume.SliceFilePaths[z]);
            var ds   = file.Dataset;

            double slope     = GetDouble(ds, DicomTag.RescaleSlope,     1.0);
            double intercept = GetDouble(ds, DicomTag.RescaleIntercept, 0.0);

            var  pixelData    = DicomPixelData.Create(ds);
            int  bits         = pixelData.BitsAllocated;
            bool isSigned     = pixelData.PixelRepresentation != 0;
            byte[] frameBytes = pixelData.GetFrame(0).Data;
            int   bytesPerPx  = Math.Max(1, bits / 8);
            int   baseIdx     = z * sliceVoxels;

            if (bits != 8 && bits != 16)
                throw new InvalidOperationException(
                    $"Slice {z} ({volume.SliceFilePaths[z]}): unsupported BitsAllocated={bits}. " +
                    "Only 8-bit and 16-bit pixel data are supported.");

            for (int i = 0; i < sliceVoxels; i++)
            {
                int offset = i * bytesPerPx;
                int raw = bits switch
                {
                    16 => isSigned
                            ? BitConverter.ToInt16(frameBytes, offset)
                            : (int)BitConverter.ToUInt16(frameBytes, offset),
                    8  => isSigned ? (int)(sbyte)frameBytes[offset] : (int)frameBytes[offset],
                    _  => 0, // unreachable — guarded above
                };

                huBuffer[baseIdx + i] = ConvertToHu(raw, slope, intercept);
            }

            progress?.Report((z + 1, nz));
        }

        return new VolumeData
        {
            HuBuffer   = huBuffer,
            Columns    = nx,
            Rows       = ny,
            SliceCount = nz,
            SpacingX   = volume.PixelSpacingX,
            SpacingY   = volume.PixelSpacingY,
            SpacingZ   = volume.SpacingZ,
            OriginX    = volume.OriginX,
            OriginY    = volume.OriginY,
            OriginZ    = volume.OriginZ,
        };
    }

    /// <summary>Converts a raw pixel value to a clamped Hounsfield Unit short.</summary>
    internal static short ConvertToHu(int raw, double slope, double intercept)
        => (short)Math.Clamp(raw * slope + intercept, short.MinValue, short.MaxValue);

    private static double GetDouble(DicomDataset ds, DicomTag tag, double defaultVal)
    {
        try { return ds.GetSingleValueOrDefault<double>(tag, defaultVal); }
        catch { return defaultVal; }
    }
}
