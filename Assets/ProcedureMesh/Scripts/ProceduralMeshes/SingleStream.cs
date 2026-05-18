using Unity.Mathematics;
using UnityEngine;

namespace ProceduralMeshes
{
    public class SingleStream : IMeshStreams
    {
        public void Setup(Mesh.MeshData data, int vertexCount, int indexCount) { }

        public void SetVertex(int index, Vertex data) { }

        public void SetTriangle(int index, int3 triangle) { }
    }
}