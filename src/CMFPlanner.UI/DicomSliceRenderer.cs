using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CMFPlanner.Core.Models;

namespace CMFPlanner.UI;

/// <summary>
/// Renders reformatted 2D slice bitmaps from a <see cref="VolumeData"/> HU buffer.
/// All three standard anatomical planes are supported.
/// Superior slices are placed at the top of coronal and sagittal views.
/// </summary>
internal static class DicomSliceRenderer
{
    // ── Transfer function ─────────────────────────────────────────────────

    private static byte HuToGray(short hu, int ww, int wc)
    {
        double lo = wc - ww * 0.5;
        double hi = wc + ww * 0.5;
        if (hu <= lo) return 0;
        if (hu >= hi) return 255;
        return (byte)((hu - lo) / ww * 255.0);
    }

    // ── Plane renderers ───────────────────────────────────────────────────

    /// <summary>Axial slice at index <paramref name="z"/> (0 = inferior).</summary>
    public static WriteableBitmap RenderAxial(VolumeData v, int z, int ww, int wc)
    {
        z = Math.Clamp(z, 0, v.SliceCount - 1);
        int w = v.Columns, h = v.Rows;
        var px = new byte[h * w];
        int baseZ = z * h * w;
        for (int y = 0; y < h; y++)
        {
            int rowOff = y * w;
            for (int x = 0; x < w; x++)
                px[rowOff + x] = HuToGray(v.HuBuffer[baseZ + rowOff + x], ww, wc);
        }
        return Write(px, w, h);
    }

    /// <summary>Coronal slice at row index <paramref name="y"/> (superior = top of image).</summary>
    public static WriteableBitmap RenderCoronal(VolumeData v, int y, int ww, int wc)
    {
        y = Math.Clamp(y, 0, v.Rows - 1);
        int w = v.Columns, h = v.SliceCount;
        var px = new byte[h * w];
        for (int z = 0; z < v.SliceCount; z++)
        {
            int dstY = (v.SliceCount - 1 - z) * w;
            long srcBase = (long)z * v.Rows * v.Columns + y * v.Columns;
            for (int x = 0; x < w; x++)
                px[dstY + x] = HuToGray(v.HuBuffer[srcBase + x], ww, wc);
        }
        return Write(px, w, h);
    }

    /// <summary>Sagittal slice at column index <paramref name="x"/> (superior = top of image).</summary>
    public static WriteableBitmap RenderSagittal(VolumeData v, int x, int ww, int wc)
    {
        x = Math.Clamp(x, 0, v.Columns - 1);
        int w = v.Rows, h = v.SliceCount;
        var px = new byte[h * w];
        for (int z = 0; z < v.SliceCount; z++)
        {
            int dstY = (v.SliceCount - 1 - z) * w;
            long srcZBase = (long)z * v.Rows * v.Columns + x;
            for (int row = 0; row < v.Rows; row++)
                px[dstY + row] = HuToGray(v.HuBuffer[srcZBase + (long)row * v.Columns], ww, wc);
        }
        return Write(px, w, h);
    }

    private static WriteableBitmap Write(byte[] px, int w, int h)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray8, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w, 0);
        return bmp;
    }
}
