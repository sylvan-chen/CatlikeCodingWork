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
        /// 在阴影图集中预留灯光的阴影空间，并存储渲染这些阴影贴图所需要的信息
        /// </summary>
        public void ReserveDirectionalShadows(Light light, int visibleLightIndex)
        {
            if (_shadowedDirectionalLightCount < MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT
                // 阴影模式不能为 None
                && light.shadows != LightShadows.None
                // 阴影强度要大于 0
                && light.shadowStrength > 0f
                // 剔除结果中阴影的包围盒是有效的
                && _cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds _))
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
            else
            {
                // 无需阴影时，获取一个 1x1 的虚拟纹理，避免额外的着色器变体
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

            // 按照光源数量拆分图块
            int split = _shadowedDirectionalLightCount <= 1 ? 1 : 2;
            int tileSize = atlasSize / split;

            for (int i = 0; i < _shadowedDirectionalLightCount; i++)
            {
                RenderDirectionalShadows(i, split, tileSize);
            }

            _buffer.EndSample(BUFFER_NAME);
            ExecuteBuffer();
        }

        private void RenderDirectionalShadows(int index, int split, int tileSize)
        {
            var light = _shadowedDirectionalLights[index];
            var shadowSettings = new ShadowDrawingSettings(_cullingResults, light.VisibleLightIndex);

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
            SetTileViewport(index, split, tileSize);
            _buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            ExecuteBuffer();
            _context.DrawShadows(ref shadowSettings);
        }

        private void SetTileViewport(int index, int split, float tileSize)
        {
            Vector2 offset = new Vector2(index % split, (float)index / split);
            _buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));
        }

        private void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}