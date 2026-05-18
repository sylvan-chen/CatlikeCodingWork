using Unity.Mathematics;
using UnityEngine;

namespace ProceduralMeshes
{
    public interface IMeshStreams
    {
        /// <summary>
        /// 初始化网格数据
        /// </summary>
        /// <param name="data">网格数据</param>
        /// <param name="vertexCount">顶点数</param>
        /// <param name="indexCount">索引数量</param>
        public void Setup(Mesh.MeshData data, int vertexCount, int indexCount);

        /// <summary>
        /// 设置顶点数据
        /// </summary>
        /// <param name="index">顶点索引</param>
        /// <param name="data">要设置的数据</param>
        public void SetVertex(int index, Vertex data);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index">三角形索引</param>
        /// <param name="triangle">顶点索引三元组</param>
        public void SetTriangle(int index, int3 triangle);
    }
}