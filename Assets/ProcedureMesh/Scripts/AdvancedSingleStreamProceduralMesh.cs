using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class AdvancedSingleStreamProceduralMesh : MonoBehaviour
{
    /// <summary>
    /// 顶点数据结构体
    /// 强制设置连续内存分布
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public float3 position, normal;
        public half4 tangent;
        public half2 texCoord0;
    }

    private void OnEnable()
    {
        // 一个网格最多包含 4 块独立的顶点数据内存区域
        int vertexAttributeCount = 4;
        // 顶点数量
        int vertexCount = 4;
        // 三角形索引数量
        int triangleIndexCount = 6;

        // 单流：
        // 由于一个网格最多包含 4 块独立的顶点数据内存区域，如果把每个属性单独放一个流，我们最多只能设置 4 种属性
        // 单流方式就是把不同的属性全部放一个流里面，内存分布就变成了 [P N T UV][P N T UV][P N T UV][P N T UV]
        Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
        Mesh.MeshData meshData = meshDataArray[0];

        // 必须先了解网格的内存布局：
        // 网格的内存被划分为多个区域，我们需要重点关注的时顶点区域和索引区域，顶点区域包含一个或多个数据流，这些数据流是格式相同的顶点数据的连续块。
        // 我们网格的每个顶点都有四个属性：位置、法向量、切线和一组纹理坐标，我们可以将它们分别存储在不同的流中。我们将此称为多流方法 (Multi-Stream)
        // 单流 (GPU 友好): [P N T UV][P N T UV][P N T UV][P N T UV]
        // 多流 (CPU 友好): [P P P P][N N N N][T T T T][UV UV UV UV]
        // 因此，更新网格数据的时候，多流更好 (在 CPU 执行)；真正渲染的时候，单流更好 (在 GPU 渲染)。
        // 最终上传到 GPU 时，Unity 可能会内部重排。

        // 分配一个临时原生数组，装 VertexAttributeDescriptor 元素，作为 meshData 的内存缓冲
        // 使用 NativeArrayOptions.UninitializedMemory 能够跳过内存初始化步骤，进一步优化效率
        var vertexAttributes = new NativeArray<VertexAttributeDescriptor>(
            vertexAttributeCount,
            Allocator.Temp,
            NativeArrayOptions.UninitializedMemory
        );
        // 4 块顶点数据流
        // dimension 代表数据维度 (float3、float4), stream 代表属于第几个数据流的数据
        vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3, stream: 0);
        vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, dimension: 3, stream: 0);
        vertexAttributes[2] = new VertexAttributeDescriptor(
            VertexAttribute.Tangent,
            dimension: 4,
            stream: 0,
            format: VertexAttributeFormat.Float16
        );
        vertexAttributes[3] = new VertexAttributeDescriptor(
            VertexAttribute.TexCoord0,
            dimension: 2,
            stream: 0,
            format: VertexAttributeFormat.Float16
        );

        // 分配网格的顶点数据流
        // 内存分布 (4类属性 x 4个顶点): [P][N][T][UV][P][N][T][UV][P][N][T][UV][P][N][T][UV]
        meshData.SetVertexBufferParams(vertexCount, vertexAttributes);
        // 分配给网格后记得 Dispose 原生数组
        vertexAttributes.Dispose();

        // 读取单个 Vertex 流的 NativeArray
        NativeArray<Vertex> vertices = meshData.GetVertexData<Vertex>();

        half h0 = math.half(0f), h1 = math.half(1f);

        var vertex = new Vertex { normal = math.back(), tangent = math.half4(h1, h0, h0, math.half(-1f)) };

        vertex.position = 0f;
        vertex.texCoord0 = h0;
        vertices[0] = vertex;

        vertex.position = math.right();
        vertex.texCoord0 = math.half2(h1, h0);
        vertices[1] = vertex;

        vertex.position = math.up();
        vertex.texCoord0 = math.half2(h0, h1);
        vertices[2] = vertex;

        vertex.position = math.float3(1f, 1f, 0f);
        vertex.texCoord0 = h1;
        vertices[3] = vertex;

        // 分配网格的索引数据流
        // UInt32 对应 uint，UInt16 对应 ushort
        meshData.SetIndexBufferParams(triangleIndexCount, IndexFormat.UInt16);
        // 设置索引
        NativeArray<ushort> triangleIndices = meshData.GetIndexData<ushort>();
        triangleIndices[0] = 0;
        triangleIndices[1] = 2;
        triangleIndices[2] = 1;
        triangleIndices[3] = 1;
        triangleIndices[4] = 2;
        triangleIndices[5] = 3;

        // 提前创建网格/子网格边界，避免自动计算
        var bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(1f, 1f, 0f));
        // 设置子网格数量
        meshData.subMeshCount = 1;
        // 设定每个子网格: 起始索引数组下标 + 包含的索引数量
        var subMesh = new SubMeshDescriptor(indexStart: 0, indexCount: triangleIndexCount)
        {
            bounds = bounds,
            // 顶点数量不是由 index 决定的吗？我手动填 vertexCount 干嘛？
            // 这里只是一个“这个子网格可能访问的顶点范围提示（hint）”，辅助 Unity 做范围裁剪
            // 写小了：可能出 bug（未定义行为）；写大了：安全，但不够优化
            vertexCount = vertexCount
        };
        meshData.SetSubMesh(0, subMesh);

        // 也可以直接把 bounds 设置给 mesh，那么所有子网都使用相同的边界
        var mesh = new Mesh { name = "Procedural Mesh", bounds = bounds, };

        Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);
        GetComponent<MeshFilter>().mesh = mesh;
    }
}