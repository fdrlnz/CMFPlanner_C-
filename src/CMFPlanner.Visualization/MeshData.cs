namespace CMFPlanner.Visualization;

/// <summary>
/// Framework-agnostic triangle mesh.
/// Positions and Normals are flat float arrays: [x0,y0,z0, x1,y1,z1, …]
/// Indices are triangle indices into the Positions array.
/// </summary>
public record MeshData(float[] Positions, float[] Normals, int[] Indices)
{
    public int VertexCount   => Positions.Length / 3;
    public int TriangleCount => Indices.Length   / 3;
}
