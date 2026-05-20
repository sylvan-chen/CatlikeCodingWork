using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralMeshes
{
    public class SingleStream : IMeshStreams
    {
        public void Setup(Mesh.MeshData meshData, int vertexCount, int indexCount)
        {
            var attributes = new NativeArray<VertexAttributeDescriptor>(
                4,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );

            attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3);
            attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, dimension: 3);
            attributes[2] = new VertexAttributeDescriptor(VertexAttribute.Tangent, dimension: 4);
            attributes[3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2);

            meshData.SetVertexBufferParams(vertexCount, attributes);
        }

        public void SetVertex(int index, Vertex data) { }

        public void SetTriangle(int index, int3 triangle) { }
    }
}