using System.Diagnostics;
using System.Runtime.CompilerServices;
using CMFPlanner.Core.Models;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;
using FellowOakDicom.Imaging.NativeCodec;

[assembly: InternalsVisibleTo("CMFPlanner.Visualization.Tests")]

namespace CMFPlanner.Visualization;

/// <summary>
/// Builds a <see cref="VolumeData"/> (HU buffer) from a sorted DICOM series.
/// Runs entirely on a thread-pool thread so the UI stays responsive.
/// VTK rendering integration is handled in E2-T3.
/// </summary>
public sealed class VtkVolumeBuilder : IVolumeBuilder
{
    /// <summary>
    /// Registers native DICOM codecs (JPEG2000, JPEG-LS, RLE) once per AppDomain
    /// so that compressed (encapsulated) transfer syntaxes can be transcoded to
    /// Explicit VR Little Endian before pixel reading.
    /// </summary>
    static VtkVolumeBuilder()
    {
        try
        {
            new DicomSetupBuilder()
                .RegisterServices(s => s.AddFellowOakDicom()
                    .AddTranscoderManager<NativeTranscoderManager>())
                .Build();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VtkVolumeBuilder] fo-dicom codec initialisation failed: {ex.Message}. " +
                "Compressed DICOM transcoding will not be available.");
        }
    }

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

            // ── Transfer-syntax check: decompress encapsulated (compressed) data ──────
            var ts = ds.InternalTransferSyntax;
            if (ts.IsEncapsulated)
            {
                Debug.WriteLine(
                    $"[VtkVolumeBuilder] Slice {z}: encapsulated transfer syntax " +
                    $"{ts.UID.Name} ({ts.UID.UID}). Transcoding to ExplicitVRLittleEndian.");
                try
                {
                    var transcoder = new DicomTranscoder(ts, DicomTransferSyntax.ExplicitVRLittleEndian);
                    file = transcoder.Transcode(file);
                    ds   = file.Dataset;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[VtkVolumeBuilder] Slice {z}: transcoding failed ({ex.Message}). " +
                        "Filling slice with air (-1000 HU).");
                    Array.Fill(huBuffer, (short)-1000, z * sliceVoxels, sliceVoxels);
                    progress?.Report((z + 1, nz));
                    continue;
                }
            }

            double slope     = GetDouble(ds, DicomTag.RescaleSlope,     1.0);
            double intercept = GetDouble(ds, DicomTag.RescaleIntercept, 0.0);

            var    pixelData  = DicomPixelData.Create(ds);
            int    bits       = pixelData.BitsAllocated;
            bool   isSigned   = pixelData.PixelRepresentation != 0;
            byte[] frameBytes = pixelData.GetFrame(0).Data;
            int    bytesPerPx = Math.Max(1, bits / 8);
            int    baseIdx    = z * sliceVoxels;

            if (bits != 8 && bits != 16)
                throw new InvalidOperationException(
                    $"Slice {z} ({volume.SliceFilePaths[z]}): unsupported BitsAllocated={bits}. " +
                    "Only 8-bit and 16-bit pixel data are supported.");

            int warnCount = 0;
            for (int i = 0; i < sliceVoxels; i++)
            {
                int offset = i * bytesPerPx;

                // ── Bounds guard: fill with air if the frame buffer is shorter than expected ──
                if (offset + bytesPerPx > frameBytes.Length)
                {
                    if (warnCount == 0)
                        Debug.WriteLine(
                            $"[VtkVolumeBuilder] Slice {z}, pixel {i}: " +
                            $"offset {offset}+{bytesPerPx} exceeds frameBytes.Length {frameBytes.Length}. " +
                            "Filling remainder with -1000 HU.");
                    warnCount++;
                    huBuffer[baseIdx + i] = -1000;
                    continue;
                }

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

            if (warnCount > 0)
                Debug.WriteLine(
                    $"[VtkVolumeBuilder] Slice {z}: {warnCount} out-of-bounds pixels filled with -1000 HU.");

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
