using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CMFPlanner.Visualization;

/// <summary>
/// Reads binary or ASCII STL files into <see cref="MeshData"/> format.
/// All operations are CPU-only and safe to run on background threads.
/// </summary>
public static class StlReader
{
    /// <summary>Reads an STL file from disk.</summary>
    public static MeshData Read(string path) =>
        Read(System.IO.File.ReadAllBytes(path));

    /// <summary>Parses a raw STL byte array (auto-detects binary vs. ASCII).</summary>
    public static MeshData Read(byte[] raw)
    {
        // Binary STL: 80-byte header + uint32 triCount + triCount × 50 bytes
        if (raw.Length >= 84)
        {
            uint triCount = BitConverter.ToUInt32(raw, 80);
            if (84 + (long)triCount * 50 == raw.Length)
                return ParseBinary(raw, (int)triCount);
        }
        return ParseAscii(Encoding.ASCII.GetString(raw));
    }

    // ── Binary ────────────────────────────────────────────────────────────

    private static MeshData ParseBinary(byte[] data, int triCount)
    {
        // Flat (non-indexed) layout: 3 verts per triangle
        var positions = new float[triCount * 9];
        var normals   = new float[triCount * 9];
        var indices   = new int  [triCount * 3];

        int src  = 84;
        int vDst = 0;
        int iDst = 0;
        int vIdx = 0;

        for (int t = 0; t < triCount; t++)
        {
            float nx = BitConverter.ToSingle(data, src);
            float ny = BitConverter.ToSingle(data, src + 4);
            float nz = BitConverter.ToSingle(data, src + 8);
            src += 12;

            for (int v = 0; v < 3; v++)
            {
                positions[vDst]     = BitConverter.ToSingle(data, src);
                positions[vDst + 1] = BitConverter.ToSingle(data, src + 4);
                positions[vDst + 2] = BitConverter.ToSingle(data, src + 8);
                normals  [vDst]     = nx;
                normals  [vDst + 1] = ny;
                normals  [vDst + 2] = nz;
                indices  [iDst++]   = vIdx++;
                vDst += 3;
                src  += 12;
            }
            src += 2; // attribute byte count (ignored)
        }

        return new MeshData(positions, normals, indices);
    }

    // ── ASCII ─────────────────────────────────────────────────────────────

    private static MeshData ParseAscii(string text)
    {
        var positions = new List<float>();
        var normals   = new List<float>();
        var indices   = new List<int>();
        float nx = 0, ny = 0, nz = 0;
        int vIdx = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    nx = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    ny = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    nz = float.Parse(parts[4], CultureInfo.InvariantCulture);
                }
            }
            else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    positions.Add(float.Parse(parts[1], CultureInfo.InvariantCulture));
                    positions.Add(float.Parse(parts[2], CultureInfo.InvariantCulture));
                    positions.Add(float.Parse(parts[3], CultureInfo.InvariantCulture));
                    normals.Add(nx); normals.Add(ny); normals.Add(nz);
                    indices.Add(vIdx++);
                }
            }
        }

        return new MeshData([.. positions], [.. normals], [.. indices]);
    }
}
