using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    // 阴影的物理本质：一个点是否在阴影里，取决于：从光源看过去，这个点前面有没有别的东西挡着它。如果有更近的物体挡住了光路，这个点就接收不到光，就是阴影。
    //
    // 那么，怎么用 GPU 判断“前面有没有东西挡着”呢？
    //
    // 直接对每个点做光线相交测试太贵，阴影投射的核心技巧是把问题转化为深度比较：
    // - 第一步（生产者 / Caster Pass）：把相机搬到光源的位置，朝光照方向看，渲染一遍场景。但这次我们不要颜色，只要深度——把“从光源看，每个位置最近的几何体有多远”存进一张图，
    //   这张图叫 Shadow Map (阴影贴图)。
    // - 第二步（消费者 / Receiver）：正常从相机渲染场景。对每个像素，我们已经知道它的世界坐标。把这个世界坐标用光源的 VP 矩阵变换到光源的裁剪空间，得到“它离光源有多远”。
    //   然后去 shadow map 上对应的像素采样，取出“从光源看，这个方向上最近的物体有多远”。
    //   - 如果 采样到的最近深度 < 该像素自己到光源的深度 → 说明前面有更近的物体挡着 → 在阴影里，光照衰减为 0。
    //   - 否则 → 没被挡 → 正常受光。
    //
    // 这就是全部核心。一句话：阴影 = “从光源看的最近深度”和“该点离光源的实际深度”做比较。
    //
    // 不同光源阴影的投影矩阵：
    // - 方向光阴影：使用正交投影矩阵。方向光没有位置，光线是平行的（就像太阳）。所以从方向光“看”场景，等价于一个正交相机（没有近大远小透视）。
    // - 聚光灯阴影：聚光灯用透视投影。
    // - 点光源阴影：点光源用六面立方体贴图（cubemap）。

    public class Shadows
    {
        public struct ShadowedDirectionalLight
        {
            public int VisibleLightIndex;
        }

        private const string BUFFER_NAME = "Shadows";

        private static readonly int DIRECTIONAL_SHADOW_ATLAS_ID = Shader.PropertyToID("_DirectionalShadowAtlas");
        private static readonly int DIRECTIONAL_SHADOW_MATRICES_ID = Shader.PropertyToID("_DirectionalShadowMatrices");

        private static readonly Matrix4x4[] DirectionalShadowMatrices =
            new Matrix4x4[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT];

        /// <summary> 最大启用阴影的方向光数量 </summary>
        private const int MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT = 4;

        /// <summary> 渲染上下文 </summary>
        private ScriptableRenderContext _context;
        /// <summary> 剔除结果 </summary>
        private CullingResults _cullingResults;

        /// <summary> 命令缓冲区 </summary>
        private readonly CommandBuffer _buffer = new() { name = BUFFER_NAME };
        /// <summary> 启用阴影的方向光列表 </summary>
        private readonly ShadowedDirectionalLight[] _shadowedDirectionalLights =
            new ShadowedDirectionalLight[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT];

        /// <summary> 阴影设置 </summary>
        private ShadowSettings _settings;

        private int _shadowedDirectionalLightCount;

        public void Setup(ScriptableRenderContext context, CullingResults cullingResults, ShadowSettings settings)
        {
            _context = context;
            _cullingResults = cullingResults;
            _settings = settings;

            _shadowedDirectionalLightCount = 0;
        }

        public void Cleanup()
        {
            _buffer.ReleaseTemporaryRT(DIRECTIONAL_SHADOW_ATLAS_ID);
            ExecuteBuffer();
        }

        /// <summary>
        /// 先登记：在阴影图集中登记灯光的阴影，并存储渲染这些阴影贴图所需要的信息。
        /// 返回 (阴影强度, Shadow Map 偏移量)
        /// </summary>
        public Vector2 ReserveDirectionalShadows(Light light, int visibleLightIndex)
        {
            if (_shadowedDirectionalLightCount < MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT
                // 阴影模式不能为 None
                && light.shadows != LightShadows.None
                // 阴影强度要大于 0
                && light.shadowStrength > 0f
                // 剔除结果中阴影的包围盒是有效的
                && _cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds _))
            {
                _shadowedDirectionalLights[_shadowedDirectionalLightCount] = new ShadowedDirectionalLight()
                {
                    VisibleLightIndex = visibleLightIndex,
                };

                return new Vector2(light.shadowStrength, _shadowedDirectionalLightCount++);
            }

            return Vector2.zero;
        }

        /// <summary>
        /// 再创建 Shadow Map + 画阴影贴图
        /// </summary>
        public void Render()
        {
            if (_shadowedDirectionalLightCount > 0)
            {
                RenderDirectionalShadows();
            }
            else
            {
                // 无需阴影时，获取一个 1x1 的虚拟纹理，保证 _DirectionalShadowAtlas 这个全局变量永远存在，shader 里不用写“有没有阴影贴图”两个变体，避免额外的着色器变体
                _buffer.GetTemporaryRT(
                    DIRECTIONAL_SHADOW_ATLAS_ID,
                    1,
                    1,
                    32,
                    FilterMode.Bilinear,
                    RenderTextureFormat.Shadowmap
                );
            }
        }

        private void RenderDirectionalShadows()
        {
            // 创建阴影图集
            int atlasSize = (int)_settings.Directional.AtlasSize;
            // 获取一个专门渲染阴影的 RenderTexture
            // 32 是深度位数（Shadow Map 本质是深度图）
            _buffer.GetTemporaryRT(
                DIRECTIONAL_SHADOW_ATLAS_ID,
                atlasSize,
                atlasSize,
                32,
                FilterMode.Bilinear,
                RenderTextureFormat.Shadowmap
            );
            // 设置当前上下文的 RenderTarget 为刚才获取的 RenderTexture
            _buffer.SetRenderTarget(
                DIRECTIONAL_SHADOW_ATLAS_ID,
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store
            );
            // 清理 RenderTarget，只清理深度缓冲
            _buffer.ClearRenderTarget(true, false, Color.clear);
            _buffer.BeginSample(BUFFER_NAME);
            ExecuteBuffer();

            // 按照光源数量拆分图块，吧多盏光的 Shadow Map 拼进一张纹理，省去切换纹理的开销
            int split = _shadowedDirectionalLightCount <= 1 ? 1 : 2; // 大于 1 的时候切成 2x2 共 4 个 tile，每盏光一个
            int tileSize = atlasSize / split;

            for (int i = 0; i < _shadowedDirectionalLightCount; i++)
            {
                RenderDirectionalShadows(i, split, tileSize);
            }

            _buffer.SetGlobalMatrixArray(DIRECTIONAL_SHADOW_MATRICES_ID, DirectionalShadowMatrices);
            _buffer.EndSample(BUFFER_NAME);
            ExecuteBuffer();
        }

        private void RenderDirectionalShadows(int index, int split, int tileSize)
        {
            var light = _shadowedDirectionalLights[index];
            var shadowSettings = new ShadowDrawingSettings(_cullingResults, light.VisibleLightIndex);

            // 基于剔除数据，算出光源的 VP 矩阵
            // 函数内部具体做法：构造一个正交视锥体（一个盒子），让这个盒子刚好包住「相机能看到的，且在阴影距离内的场景」，盒子的朝向就是光照方向。
            _cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                light.VisibleLightIndex,
                0,
                1,
                Vector3.zero,
                tileSize,
                0f,
                out Matrix4x4 viewMatrix,
                out Matrix4x4 projectionMatrix,
                out ShadowSplitData splitData
            );

            shadowSettings.splitData = splitData;
            // 划定 Tile 视口：把渲染区域限定在图集里属于这盏光的那一块，这样 _context.DrawShadows 画出来的深度只写进这个 tile，不会覆盖别的光的 tile。
            var offset = SetTileViewport(index, split, tileSize);
            DirectionalShadowMatrices[index] = ConvertToAtlasMatrix(projectionMatrix * viewMatrix, offset, split);
            _buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            ExecuteBuffer();
            _context.DrawShadows(ref shadowSettings);
        }

        /// <summary>
        /// 接收光照矩阵、tile 偏移和分割数，把光照矩阵从世界空间转换到阴影贴图 tile 空间
        /// </summary>
        private Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 matrix, Vector2 offset, int split)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                matrix.m20 = -matrix.m20;
                matrix.m21 = -matrix.m21;
                matrix.m22 = -matrix.m22;
                matrix.m23 = -matrix.m23;
            }

            // 从 [-1, 1] 缩放到 [0, 1]，如果有多个 split，还要进一步缩放到对应 tile 空间
            float scale = 1f / split;
            matrix.m00 = (0.5f * (matrix.m00 + matrix.m30) + offset.x * matrix.m30) * scale;
            matrix.m01 = (0.5f * (matrix.m01 + matrix.m31) + offset.x * matrix.m31) * scale;
            matrix.m02 = (0.5f * (matrix.m02 + matrix.m32) + offset.x * matrix.m32) * scale;
            matrix.m03 = (0.5f * (matrix.m03 + matrix.m33) + offset.x * matrix.m33) * scale;
            matrix.m10 = (0.5f * (matrix.m10 + matrix.m30) + offset.y * matrix.m30) * scale;
            matrix.m11 = (0.5f * (matrix.m11 + matrix.m31) + offset.y * matrix.m31) * scale;
            matrix.m12 = (0.5f * (matrix.m12 + matrix.m32) + offset.y * matrix.m32) * scale;
            matrix.m13 = (0.5f * (matrix.m13 + matrix.m33) + offset.y * matrix.m33) * scale;
            return matrix;
        }

        private Vector2 SetTileViewport(int index, int split, float tileSize)
        {
            Vector2 offset = new Vector2(index % split, index / split);
            _buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));
            return offset;
        }

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}
