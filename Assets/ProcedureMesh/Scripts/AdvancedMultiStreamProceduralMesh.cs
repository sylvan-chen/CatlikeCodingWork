using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 进阶：通过 MeshData 设置网格<br/>
/// 使用之前的简单 API 向网格体分配数据时，Unity 必须在某个时刻将所有数据复制并转换为网格体的原生内存格式；<br/>
/// 而使用 MeshData 能够直接以网格体的原生内存格式进行操作，避免转换 (直接写入 GPU 顶点/索引缓冲)。<br/>
/// <br/>
/// 必须先了解网格的内存布局：<br/>
/// Mesh 本质就是两块数据:「顶点缓冲」和「索引缓冲」。<br/>
/// - 顶点缓冲: 存放每个顶点的数据，每块顶点缓冲称为一个「流」(stream)，一个网格最多包含 4 个流。结构: [s0][s1][s2][s3]<br/>
/// - 索引缓冲: 存放三角面索引。<br/>
/// <br/>
/// 顶点数据包括: 位置、法线、UV 和切线。顶点数据可以用不同的分布方式存放在 MeshData 的 stream 中。<br/>
/// - 单流 (GPU 友好): [P N T UV][P N T UV][P N T UV][P N T UV]<br/>
/// - 多流 (CPU 友好): [P P P P][N N N N][T T T T][UV UV UV UV]<br/>
/// 更新网格数据的时候，多流更好 (在 CPU 执行)；真正渲染的时候，单流更好 (在 GPU 渲染)。<br/>
/// 最终上传到 GPU 时，Unity 可能会内部重排。<br/>
/// <br/>
/// 流程:<br/>
/// 1. 设置顶点缓冲:<br/>
///    - 定义顶点属性描述: new VertexAttributeDescriptor(VertexAttribute, dimension, stream)<br/>
///      - 通过 VertexAttribute 枚举表明描述的属性是什么；<br/>
///      - 通过 stream 指定放在那一块缓冲(流)中。<br/>
///    - 根据顶点描述数组分配顶点缓冲内存结构: meshData.SetVertexBufferParams(vertexCount, VertexAttributeDescriptor[]);<br/>
/// 2. 写顶点缓冲:<br/>
///    - 先读内存 (按指定类型读): T attribute = meshData.GetVertexData&lt;T&gt;();<br/>
///    - 写入内存: attribute = xxx;<br/>
/// 3. 设置索引缓冲: meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);<br/>
/// 4. 设置子网格:<br/>
///    - 设置子网格数量: meshData.subMeshCount = n;<br/>
///    - 定义子网格描述 (起始索引 + 索引数量): new SubMeshDescriptor(indexStart, indexCount);<br/>
///    - 根据子网格描述设置子网格: meshData.SetSubMesh(i, SubMeshDescriptor);<br/>
/// 5. 应用: Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);<br/>
/// 6. 计算边界 (也可以手动设置 bounds 给网格或子网格): mesh.RecalculateBounds();<br/>
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class AdvancedMultiStreamProceduralMesh : MonoBehaviour
{
    private void OnEnable()
    {
        // 网格数量
        int meshCount = 1;
        // 顶点属性数量
        int vertexAttributeCount = 4;
        // 顶点数量
        int vertexCount = 4;
        // 三角形索引数量
        int triangleIndexCount = 6;

        // --- 定义 MeshData 数组 ---
        Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(meshCount);

        // --- 开始设置第一个网格数据 ---
        Mesh.MeshData meshData = meshDataArray[0];

        // --- 分配一个临时原生数组来装 VertexAttributeDescriptor ---
        // 使用 NativeArrayOptions.UninitializedMemory 能够跳过内存初始化步骤，进一步优化效率
        var vertexAttributes = new NativeArray<VertexAttributeDescriptor>(
            vertexAttributeCount,
            Allocator.Temp,
            NativeArrayOptions.UninitializedMemory
        );

        // --- 定义每一个 VertexAttributeDescriptor ---
        // dimension 代表数据维度 (float3、float4), stream 代表属于第几块顶点缓冲 (0 ~ 3，一个网格最多四块顶点缓冲)
        // 注意到: 这里我们每个流只放一种属性，这就是多流
        vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3, stream: 0);
        vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, dimension: 3, stream: 1);
        vertexAttributes[2] = new VertexAttributeDescriptor(
            VertexAttribute.Tangent,
            dimension: 4,
            stream: 2,
            format: VertexAttributeFormat.Float16
        );
        vertexAttributes[3] = new VertexAttributeDescriptor(
            VertexAttribute.TexCoord0,
            dimension: 2,
            stream: 3,
            format: VertexAttributeFormat.Float16
        );

        // --- 设置顶点缓冲 ---
        meshData.SetVertexBufferParams(vertexCount, vertexAttributes);
        // 记得 Dispose 原生数组
        vertexAttributes.Dispose();

        // --- 读写顶点缓冲数据 ---
        // 位置
        NativeArray<float3> positions = meshData.GetVertexData<float3>();
        positions[0] = 0f;
        positions[1] = math.right();
        positions[2] = math.up();
        positions[3] = math.float3(1f, 1f, 0f);
        // 法线
        NativeArray<float3> normals = meshData.GetVertexData<float3>(1);
        normals[0] = normals[1] = normals[2] = normals[3] = math.back();
        // 切线
        NativeArray<half4> tangents = meshData.GetVertexData<half4>(2);
        tangents[0] = tangents[1] = tangents[2] = tangents[3] = math.half4(
            math.half(1f),
            math.half(0f),
            math.half(0f),
            math.half(-1f)
        );
        // UV
        NativeArray<half2> texCoords = meshData.GetVertexData<half2>(3);
        texCoords[0] = math.half(0f);
        texCoords[1] = math.half2(math.half(1f), math.half(0f));
        texCoords[2] = math.half2(math.half(0f), math.half(1f));
        texCoords[3] = math.half(1f);

        // --- 设置索引缓冲 ---
        // UInt32 对应 uint，UInt16 对应 ushort
        meshData.SetIndexBufferParams(triangleIndexCount, IndexFormat.UInt16);

        // --- 读写索引缓冲数据 ---
        NativeArray<ushort> triangleIndices = meshData.GetIndexData<ushort>();
        triangleIndices[0] = 0;
        triangleIndices[1] = 2;
        triangleIndices[2] = 1;
        triangleIndices[3] = 1;
        triangleIndices[4] = 2;
        triangleIndices[5] = 3;

        // 提前创建网格/子网格边界，也可以最后 mesh.RecalculateBounds()
        var bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(1f, 1f, 0f));

        // --- 设置子网格 ---
        // 子网格数量
        meshData.subMeshCount = 1;
        // 定义每个子网格: 起始索引数组下标 + 包含的索引数量
        var subMesh = new SubMeshDescriptor(indexStart: 0, indexCount: triangleIndexCount)
        {
            bounds = bounds,
            // 顶点数量不是由 index 决定的吗？我手动填 vertexCount 干嘛？
            // 这里只是一个“这个子网格可能访问的顶点范围提示（hint）”，辅助 Unity 做范围裁剪
            // 写小了：可能出 bug（未定义行为）；写大了：安全，但不够优化
            vertexCount = vertexCount
        };
        meshData.SetSubMesh(0, subMesh);

        // --- 新建 Mesh ---
        // 也可以直接把 bounds 设置给 mesh，那么所有子网都使用相同的边界
        var mesh = new Mesh { name = "Procedural Mesh", bounds = bounds, };

        // --- 应用 MeshData 到 Mesh ---
        Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);
        GetComponent<MeshFilter>().mesh = mesh;
    }
}