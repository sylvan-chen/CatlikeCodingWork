using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRP
{
    public class Shadows
    {
        public struct ShadowedDirectionalLight
        {
            public int VisibleLightIndex;
        }

        private const string BUFFER_NAME = "Shadows";

        private static int DIRECTIONAL_SHADOW_ATLAS_ID = Shader.PropertyToID("_DirectionalShadowAtlas");

        /// <summary> 最大启用阴影的方向光数量 </summary>
        private const int MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT = 1;

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

        /// <summary>
        /// 在阴影图集中预留灯光的阴影空间，并存储渲染这些阴影贴图所需要的信息
        /// </summary>
        public void ReserveDirectionalShadows(Light light, int visibleLightIndex)
        {
            if (_shadowedDirectionalLightCount < MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT
                && light.shadows != LightShadows.None
                && light.shadowStrength > 0f
                && _cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds shadowBounds))
            {
                _shadowedDirectionalLights[_shadowedDirectionalLightCount++] = new ShadowedDirectionalLight()
                {
                    VisibleLightIndex = visibleLightIndex,
                };
            }
        }

        public void Render()
        {
            if (_shadowedDirectionalLightCount > 0)
            {
                RenderDirectionalShadows();
            }
        }

        private void RenderDirectionalShadows()
        {
            int atlasSize = (int)_settings.Directional.AtlasSize;
            _buffer.GetTemporaryRT(DIRECTIONAL_SHADOW_ATLAS_ID, atlasSize, atlasSize);
        }

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}