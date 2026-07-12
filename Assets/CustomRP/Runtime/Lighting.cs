using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    /// <summary>
    /// 光照渲染处理
    /// </summary>
    public class Lighting
    {
        private const string BUFFER_NAME = "Lighting";

        // ============================== 方向光相关数据 ==============================

        /// <summary> 最大方向光数量 </summary>
        private const int MAX_DIRECTIONAL_LIGHT_COUNT = 4;

        /// <summary> 方向光数量 </summary>
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        /// <summary> 方向光颜色 </summary>
        private static readonly int DirectionalLightColorsId = Shader.PropertyToID("_DirectionalLightColors");
        /// <summary> 方向光入射方向 </summary>
        private static readonly int DirectionalLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections");
        /// <summary> 方向光的阴影数据 </summary>
        private static readonly int DirectionalLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData");

        /// <summary> 方向光颜色 </summary>
        private static Vector4[] DirectionalLightColors = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
        /// <summary> 方向光入射方向 </summary>
        private static Vector4[] DirectionalLightDirections = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];
        /// <summary> 方向光的阴影数据 </summary>
        private static Vector4[] DirectionalLightShadowData = new Vector4[MAX_DIRECTIONAL_LIGHT_COUNT];

        // ==========================================================================

        /// <summary> 渲染上下文 </summary>
        private ScriptableRenderContext _context;
        /// <summary> 剔除结果 </summary>
        private CullingResults _cullingResults;

        /// <summary> 命令缓冲区 </summary>
        private readonly CommandBuffer _buffer = new() { name = BUFFER_NAME };
        /// <summary> 阴影 </summary>
        private readonly Shadows _shadows = new();

        public void Setup(ScriptableRenderContext context, CullingResults cullingResults, ShadowSettings shadowSettings)
        {
            _context = context;
            // 简单的方式是通过 RenderSetting.sun 获取场景主光源，但我们要支持多光源，因此通过剔除后的结果拿到光源信息
            _cullingResults = cullingResults;

            _buffer.BeginSample(BUFFER_NAME);
            // 配置阴影
            _shadows.Setup(context, cullingResults, shadowSettings);
            // 配置光照
            SetupLights();
            // 绘制阴影
            _shadows.Render();
            _buffer.EndSample(BUFFER_NAME);

            ExecuteBuffer();
        }

        public void Cleanup()
        {
            _shadows.Cleanup();
        }

        /// <summary>
        /// 配置所有可见光
        /// </summary>
        private void SetupLights()
        {
            // 拿剔除后的所有可见光（注意：不是"所有光"，是"相机能看见的光"）
            NativeArray<VisibleLight> visibleLights = _cullingResults.visibleLights;

            int directionalLightCount = 0;

            // 逐盏灯处理
            for (int i = 0; i < visibleLights.Length; i++)
            {
                VisibleLight visibleLight = visibleLights[i];

                // --- 方向光 ---
                if (visibleLight.lightType is LightType.Directional)
                {
                    SetupDirectionalLight(directionalLightCount++, ref visibleLight);
                    if (directionalLightCount >= MAX_DIRECTIONAL_LIGHT_COUNT) break;
                }
            }

            // 把所有数据一次性传给 GPU（不是逐盏光传）
            _buffer.SetGlobalInt(DirectionalLightCountId, directionalLightCount);
            _buffer.SetGlobalVectorArray(DirectionalLightColorsId, DirectionalLightColors);
            _buffer.SetGlobalVectorArray(DirectionalLightDirectionsId, DirectionalLightDirections);
            _buffer.SetGlobalVectorArray(DirectionalLightShadowDataId, DirectionalLightShadowData);
        }

        /// <summary>
        /// 配置一盏光的数据
        /// </summary>
        private void SetupDirectionalLight(int index, ref VisibleLight visibleLight)
        {
            // finalColor 是 Unity 帮你算好的"最终颜色"，已经把 light.color × intensity 算完了
            DirectionalLightColors[index] = visibleLight.finalColor;
            // localToWorldMatrix 的第三列 = Z 轴 = 物体的"前向"
            // 对方向光来说，前向 = 光线前进的方向
            // 我们要的是"指向光源的方向"（从表面射向光源），所以取负
            DirectionalLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
            // 登记这盏光到阴影贴图，存储这盏灯的阴影数据 Vector3(阴影强度, tile 偏移, 阴影法线偏移)
            DirectionalLightShadowData[index] = _shadows.ReserveDirectionalShadows(visibleLight.light, index);
        }

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}
