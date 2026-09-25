using System.Numerics;
using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace ChickenDesktopPet3D;

// Cache geometry bounds per influencing bone once, then transform only eight
// corners per bone each tick. Authored culling spheres are too loose for framing.
internal sealed class PoseBounds
{
    private readonly Vector3[] minimum;
    private readonly Vector3[] maximum;
    private readonly Matrix4x4[] matrices;
    private readonly Vector3[] points;
    private int pointCount;
    public ReadOnlySpan<Vector3> FramingPoints => points.AsSpan(0, pointCount);
    private Vector3 staticMin = new(float.MaxValue);
    private Vector3 staticMax = new(float.MinValue);

    public PoseBounds(Model model, AnimationController controller, GameFileLoader loader)
    {
        matrices = new Matrix4x4[controller.Pose.Length];
        points = new Vector3[(matrices.Length + 1) * 8];
        minimum = Enumerable.Repeat(new Vector3(float.MaxValue), matrices.Length).ToArray();
        maximum = Enumerable.Repeat(new Vector3(float.MinValue), matrices.Length).ToArray();
        foreach (var item in model.GetEmbeddedMeshesForLod(0)) AddMesh(item.Mesh, model.GetRemapTable(item.MeshIndex));
        foreach (var item in model.GetReferenceMeshNamesForLod(0))
        {
            using var resource = loader.LoadFileCompiled(item.MeshName);
            if (resource?.DataBlock is Mesh mesh) AddMesh(mesh, model.GetRemapTable(item.MeshIndex));
        }
    }

    private void AddMesh(Mesh mesh, int[]? remap)
    {
        foreach (var buffer in mesh.VBIB.VertexBuffers)
        {
            var positionField = buffer.InputLayoutFields.FirstOrDefault(f => f.SemanticName == "POSITION");
            if (positionField.SemanticName != "POSITION") continue;
            var positions = VBIB.GetVector3AttributeArray(buffer, positionField);
            var jointField = buffer.InputLayoutFields.FirstOrDefault(f => f.SemanticName == "BLENDINDICES");
            var weightField = buffer.InputLayoutFields.FirstOrDefault(f => f.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS");
            var joints = remap is not null && jointField.SemanticName == "BLENDINDICES"
                ? VBIB.GetBlendIndicesArray(buffer, jointField, remap) : null;
            var weights = weightField.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS"
                ? VBIB.GetBlendWeightsArray(buffer, weightField) : null;
            var jointCount = positions.Length == 0 ? 0 : (joints?.Length ?? 0) / positions.Length;
            for (var i = 0; i < positions.Length; i++)
            {
                if (joints is null)
                {
                    staticMin = Vector3.Min(staticMin, positions[i]);
                    staticMax = Vector3.Max(staticMax, positions[i]);
                    continue;
                }
                for (var j = 0; j < jointCount; j++)
                {
                    if (weights is null ? j > 0 : weights[i * (jointCount / 4) + j / 4][j % 4] <= 0) continue;
                    var bone = joints[i * jointCount + j];
                    if (bone >= matrices.Length) throw new InvalidDataException("Invalid framing bone index");
                    minimum[bone] = Vector3.Min(minimum[bone], positions[i]);
                    maximum[bone] = Vector3.Max(maximum[bone], positions[i]);
                }
            }
        }
    }

    public AABB Get(AnimationController controller, AABB fallback)
    {
        if (controller.ActiveAnimation is null) Array.Fill(matrices, Matrix4x4.Identity);
        else controller.GetSkinningMatrices(matrices);
        var min = staticMin;
        var max = staticMax;
        pointCount = 0;
        if (staticMin.X <= staticMax.X)
            for (var i = 0; i < 8; i++)
                points[pointCount++] = new Vector3((i & 1) == 0 ? staticMin.X : staticMax.X,
                    (i & 2) == 0 ? staticMin.Y : staticMax.Y, (i & 4) == 0 ? staticMin.Z : staticMax.Z);
        for (var bone = 0; bone < matrices.Length; bone++)
        {
            if (minimum[bone].X > maximum[bone].X) continue;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? minimum[bone].X : maximum[bone].X,
                    (i & 2) == 0 ? minimum[bone].Y : maximum[bone].Y,
                    (i & 4) == 0 ? minimum[bone].Z : maximum[bone].Z);
                corner = Vector3.Transform(corner, matrices[bone]);
                points[pointCount++] = corner;
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }
        }
        return min.X <= max.X ? new AABB(min, max) : fallback;
    }
}
