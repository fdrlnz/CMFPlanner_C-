using CMFPlanner.Core.Models;
using FellowOakDicom;

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

            for (int i = 0; i < sliceVoxels; i++)
            {
                int offset = i * bytesPerPx;
                int raw = bits switch
                {
                    16 => isSigned
                            ? BitConverter.ToInt16(frameBytes, offset)
                            : BitConverter.ToUInt16(frameBytes, offset),
                    8  => isSigned ? (sbyte)frameBytes[offset] : frameBytes[offset],
                    _  => throw new NotSupportedException($"Unsupported BitsAllocated={bits}; only 8/16-bit monochrome slices are supported."),
                };

                double hu = raw * slope + intercept;
                huBuffer[baseIdx + i] = (short)Math.Clamp(hu, short.MinValue, short.MaxValue);
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
            SpacingZ   = volume.SpacingZ > 0 ? volume.SpacingZ : volume.SliceThickness,
            OriginX    = volume.OriginX,
            OriginY    = volume.OriginY,
            OriginZ    = volume.OriginZ,
        };
    }

    private static double GetDouble(DicomDataset ds, DicomTag tag, double defaultVal)
    {
        try { return ds.GetSingleValueOrDefault<double>(tag, defaultVal); }
        catch { return defaultVal; }
    }
}
