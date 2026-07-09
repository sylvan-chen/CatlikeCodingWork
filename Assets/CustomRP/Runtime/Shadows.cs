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

    /// <summary>
    /// 阴影渲染处理
    /// </summary>
    public class Shadows
    {
        /// <summary> 应用阴影的方向光 </summary>
        public struct ShadowedDirectionalLight
        {
            public int VisibleLightIndex;
            public float SlopeScaleBias;
            public float NearPlaneOffset;
        }

        private const string BUFFER_NAME = "Shadows";

        /// <summary> 应用阴影的方向光的最大数量 </summary>
        private const int MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT = 4;
        /// <summary> 最大级联数量 </summary>
        private const int MAX_CASCADES_COUNT = 4;

        /// <summary> 方向阴影图集 </summary>
        private static readonly int DirectionalShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas");
        /// <summary> 方向阴影矩阵 </summary>
        private static readonly int DirectionalShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices");
        /// <summary> 阴影级联数量 </summary>
        private static readonly int CascadeCountId = Shader.PropertyToID("_CascadeCount");
        /// <summary> 级联剔除球 </summary>
        private static readonly int CascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres");
        /// <summary> 级联数据 </summary>
        private static readonly int CascadeDataId = Shader.PropertyToID("_CascadeData");
        /// <summary> 阴影贴图尺寸 </summary>
        private static readonly int ShadowAtlasSizeId = Shader.PropertyToID("_ShadowAtlasSize");
        /// <summary> 阴影距离衰减 </summary>
        private static readonly int ShadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade");

        /// <summary> 登记到阴影图集的所有方向光 </summary>
        private readonly ShadowedDirectionalLight[] ShadowedDirectionalLights =
            new ShadowedDirectionalLight[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT];
        private static readonly Matrix4x4[] DirectionalShadowMatrices =
            new Matrix4x4[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT * MAX_CASCADES_COUNT];
        private static readonly Vector4[] CascadeCullingSpheres = new Vector4[MAX_CASCADES_COUNT];
        private static readonly Vector4[] CascadeData = new Vector4[MAX_CASCADES_COUNT];

        private static string[] DirectionalFilterKeywords =
        {
            "_DIRECTIONAL_PCF3", "_DIRECTIONAL_PCF5", "_DIRECTIONAL_PCF7",
        };

        private static string[] CascadeBlendKeywords = { "_CASCADE_BLEND_SOFT", "_CASCADE_BLEND_DITHER" };

        /// <summary> 渲染上下文 </summary>
        private ScriptableRenderContext _context;
        /// <summary> 剔除结果 </summary>
        private CullingResults _cullingResults;

        /// <summary> 命令缓冲区 </summary>
        private readonly CommandBuffer _buffer = new() { name = BUFFER_NAME };

        /// <summary> 阴影设置 </summary>
        private ShadowSettings _settings;

        // 当前应用阴影的方向光数量
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
            _buffer.ReleaseTemporaryRT(DirectionalShadowAtlasId);
            ExecuteBuffer();
        }

        /// <summary>
        /// 登记光照：在阴影图集中登记要应用的灯光
        /// </summary>
        /// <returns>这盏灯应用阴影贴图需要的数据: Vector2(阴影强度, Shadow Map 偏移量, 阴影法线偏移)</returns>
        public Vector3 ReserveDirectionalShadows(Light light, int visibleLightIndex)
        {
            if (_shadowedDirectionalLightCount < MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT
                // 阴影模式不能为 None
                && light.shadows != LightShadows.None
                // 阴影强度要大于 0
                && light.shadowStrength > 0f
                // 剔除结果中阴影的包围盒是有效的
                && _cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds _))
            {
                // 记录可应用阴影的方向光数据
                ShadowedDirectionalLights[_shadowedDirectionalLightCount] = new ShadowedDirectionalLight()
                {
                    VisibleLightIndex = visibleLightIndex,
                    SlopeScaleBias = light.shadowBias,
                    NearPlaneOffset = light.shadowNearPlane,
                };

                // 返回应用贴图需要的数据
                return new Vector3(
                    light.shadowStrength,
                    _settings.Directional.CascadeCount * _shadowedDirectionalLightCount++,
                    light.shadowNormalBias
                );
            }

            return Vector3.zero;
        }

        /// <summary>
        /// 绘制阴影贴图 (Shadow map)
        /// </summary>
        public void Render()
        {
            if (_shadowedDirectionalLightCount > 0)
            {
                // 方向阴影的贴图
                RenderDirectionalShadows();
            }
            else
            {
                // 无需阴影时，获取一个 1x1 的虚拟纹理，保证 _DirectionalShadowAtlas 这个全局变量永远存在，shader 里不用写“有没有阴影贴图”两个变体，避免额外的着色器变体
                _buffer.GetTemporaryRT(
                    DirectionalShadowAtlasId,
                    1,
                    1,
                    32,
                    FilterMode.Bilinear,
                    RenderTextureFormat.Shadowmap
                );
            }
        }

        /// <summary>
        /// 绘制方向光阴影贴图
        /// </summary>
        private void RenderDirectionalShadows()
        {
            // 创建阴影图集
            int atlasSize = (int)_settings.Directional.AtlasSize;
            // 获取一个专门渲染阴影的 RenderTexture
            // 32 是深度位数（Shadow Map 本质是深度图）
            _buffer.GetTemporaryRT(
                DirectionalShadowAtlasId,
                atlasSize,
                atlasSize,
                32,
                FilterMode.Bilinear,
                RenderTextureFormat.Shadowmap
            );
            // 设置当前上下文的 RenderTarget 为刚才获取的 RenderTexture
            _buffer.SetRenderTarget(
                DirectionalShadowAtlasId,
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store
            );
            // 清理 RenderTarget，只清理深度缓冲
            _buffer.ClearRenderTarget(true, false, Color.clear);
            _buffer.BeginSample(BUFFER_NAME);
            ExecuteBuffer();

            // 按照光源数量和级联数量拆分图块，把多盏光的 Shadow Map 拼进一张纹理，省去切换纹理的开销
            // 总 tile 数量 = 级联数量 * 总登记灯光数量
            int tiles = _settings.Directional.CascadeCount * _shadowedDirectionalLightCount;
            // split 代表每条边要切分的数量，总块数为 split * split
            int split;
            if (tiles <= 1)
            {
                // 只有 1 盏光、1 层级联，不用切分
                split = 1;
            }
            else if (tiles <= 4)
            {
                // 4 个 tiles 以内，切成 2x2=4 块
                split = 2;
            }
            else
            {
                // 最多切成 4x4=16 块
                split = 4;
            }
            // 每个 tile 的尺寸 = 图集尺寸 / 分割数
            int tileSize = atlasSize / split;

            // 遍历绘制每一盏登记的灯光
            for (int i = 0; i < _shadowedDirectionalLightCount; i++)
            {
                RenderDirectionalShadow(i, split, tileSize);
            }

            float cascadeFade = 1f - _settings.Directional.CascadeFade;
            _buffer.SetGlobalInt(CascadeCountId, _settings.Directional.CascadeCount);
            _buffer.SetGlobalVectorArray(CascadeCullingSpheresId, CascadeCullingSpheres);
            _buffer.SetGlobalVectorArray(CascadeDataId, CascadeData);
            _buffer.SetGlobalMatrixArray(DirectionalShadowMatricesId, DirectionalShadowMatrices);
            _buffer.SetGlobalVector(
                ShadowDistanceFadeId,
                new Vector4(
                    1f / _settings.MaxDistance,
                    1f / _settings.DistanceFade,
                    1f / (1f - cascadeFade * cascadeFade)
                )
            );
            SetKeywords(DirectionalFilterKeywords, (int)_settings.Directional.Filter - 1);
            SetKeywords(CascadeBlendKeywords, (int)_settings.Directional.CascadeBlend - 1);
            _buffer.SetGlobalVector(ShadowAtlasSizeId, new Vector4(atlasSize, 1f / atlasSize));
            _buffer.EndSample(BUFFER_NAME);
            ExecuteBuffer();
        }

        /// <summary>
        /// 绘制单盏方向光的阴影贴图
        /// </summary>
        private void RenderDirectionalShadow(int index, int split, int tileSize)
        {
            var light = ShadowedDirectionalLights[index];
            // 要绘制阴影贴图，需要的配置参数
            var shadowSettings = new ShadowDrawingSettings(_cullingResults, light.VisibleLightIndex);

            int cascadeCount = _settings.Directional.CascadeCount;
            int tileOffset = index * cascadeCount; // 每盏光（每个索引）占用级联数量的 tile
            Vector3 cascadeRatios = _settings.Directional.CascadeRatios;

            float cullingFactor = Mathf.Max(0f, 0.8f - _settings.Directional.CascadeFade);
            // 为每层级联做正交投影，拿到 VP 矩阵
            for (int cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
            {
                // 函数内部具体做法：构造一个正交视锥体（一个盒子），让这个盒子刚好包住「相机能看到的，且在阴影距离内的场景」，盒子的朝向就是光照方向。
                _cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    light.VisibleLightIndex,
                    cascadeIndex,  // 级联索引
                    cascadeCount,  // 级联数量
                    cascadeRatios, // 级联比例
                    tileSize,
                    light.NearPlaneOffset,
                    out Matrix4x4 viewMatrix,       // 输出 View 矩阵
                    out Matrix4x4 projectionMatrix, // 输出 Projection 矩阵
                    out ShadowSplitData splitData
                );

                splitData.shadowCascadeBlendCullingFactor = cullingFactor;
                shadowSettings.splitData = splitData;

                // 只需要记录第一个光源的级联数据，所有光源的级联都是等效的
                if (index == 0)
                {
                    SetCascadeData(cascadeIndex, splitData.cullingSphere, tileSize);
                }

                int tileIndex = tileOffset + cascadeIndex; // 该级联所在的 tile
                // 划定 Tile 视口：把渲染区域限定在图集里属于这盏光的那一块，这样 _context.DrawShadows 画出来的深度只写进这个 tile，不会覆盖别的光的 tile。
                DirectionalShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                    projectionMatrix * viewMatrix,
                    SetTileViewport(tileIndex, split, tileSize),
                    split
                );
                _buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                _buffer.SetGlobalDepthBias(0, light.SlopeScaleBias);
                ExecuteBuffer();
                _context.DrawShadows(ref shadowSettings);
                _buffer.SetGlobalDepthBias(0f, 0f);
            }
        }

        /// <summary>
        /// 设置级联数据
        /// </summary>
        private void SetCascadeData(int index, Vector4 cullingSphere, float tileSize)
        {
            // 级联剔除球数据
            float texelSize = 2f * cullingSphere.w / tileSize;
            float filterSize = texelSize * ((float)_settings.Directional.Filter + 1f);
            cullingSphere.w -= filterSize;
            cullingSphere.w *= cullingSphere.w; // 直接存储平方半径，方便在着色器中计算一个片段是否在球体内部（比较球体中心到片段的平方距离与球体的平方半径）
            CascadeCullingSpheres[index] = cullingSphere;

            // 级联数据
            CascadeData[index] = new Vector4(1f / cullingSphere.w, filterSize * 1.4142136f);
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
            matrix.m20 = 0.5f * (matrix.m20 + matrix.m30);
            matrix.m21 = 0.5f * (matrix.m21 + matrix.m31);
            matrix.m22 = 0.5f * (matrix.m22 + matrix.m32);
            matrix.m23 = 0.5f * (matrix.m23 + matrix.m33);
            return matrix;
        }

        private Vector2 SetTileViewport(int tileIndex, int split, float tileSize)
        {
            Vector2 offset = new Vector2(tileIndex % split, tileIndex / split);
            _buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));
            return offset;
        }

        private void SetKeywords(string[] keywords, int enabledIndex)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (i == enabledIndex)
                {
                    _buffer.EnableShaderKeyword(keywords[i]);
                }
                else
                {
                    _buffer.DisableShaderKeyword(keywords[i]);
                }
            }
        }

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}
