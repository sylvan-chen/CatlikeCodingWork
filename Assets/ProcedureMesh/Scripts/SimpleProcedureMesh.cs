using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimpleProceduralMesh : MonoBehaviour
{
    private void OnEnable()
    {
        var mesh = new Mesh { name = "Procedural Mesh" };

        // 网格是由三角面组成的，而一个三角面由三个顶点索引组成
        // 一个网格常见的可设置数据
        // 1. 顶点 (vertices): 顶点就是 3D 空间中的一个位置，由一个 Vector3 值来描述。
        // 2. 三角面 (triangles): 三角面数组中的元素代表的顶点数组的索引，每三个元素一组构成三角面，按**顺时针**顺序构成的面是正面。
        // 3. 法线 (normals): 法线用于给着色器计算光照。法线是一个单位向量，代表正面的局部向上方向 (表面外侧)。
        //                   法线由一个 Vector3 值来描述，法线数组按顶点数组顺序逐顶点对应，三角面上的实际法线通过多个顶点法线的插值得到。
        // 4. 纹理映射 (uv): 可以给网格添加纹理(贴图)来添加表面细节，我们通过设置每个顶点的 uv 属性来指定该顶点对应着纹理的哪个位置。
        //                  uv 由一个 Vector2 值来描述，它是一个归一化坐标 (0 ~ 1 闭区间)，纹理的左下角为原点 (0, 0)。
        // 5. 法线贴图和切线 (tangents): 可以给网格添加法线贴图来调整法线，影响光照效果 (在平坦的表面营造凹凸不平的假象)。为了将纹理空间中的向量转换到世界空间，
        //                            需要定义一个相对于表面的切线空间，由右轴、上轴和前轴组成。
        //                            - 上轴: 就是法线；
        //                            - 右轴: 通过与表面曲率相切的「切线」来定义；
        //                            - 前轴: 着色器计算法线和切线正交的向量来自行构建；
        //                            tangents 由一个 Vector4 值来描述，与顶点数组逐顶点对应，第 4 个分量应设置为 1 或 -1，用于定义前轴通过右手定则还是左手定则来计算

        mesh.vertices = new Vector3[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1f, 1f), };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back, };
        mesh.tangents = new Vector4[]
        {
            new Vector4(1f, 0f, 0f, -1f),
            new Vector4(1f, 0f, 0f, -1f),
            new Vector4(1f, 0f, 0f, -1f),
            new Vector4(1f, 0f, 0f, -1f),
        };
        mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };

        GetComponent<MeshFilter>().mesh = mesh;
    }
}