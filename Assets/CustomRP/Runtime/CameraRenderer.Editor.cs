using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace CustomRP
{
    /// <summary>
    /// 编辑器专用
    /// </summary>
    public partial class CameraRenderer
    {
        private partial void PrepareBuffer();
        private partial void PrepareForSceneWindow();
        private partial void DrawUnsupportedShaders();
        private partial void DrawGizmos();

#if UNITY_EDITOR
        private static ShaderTagId[] LegacyShaderTagIds =
        {
            new ShaderTagId("Always"),
            new ShaderTagId("ForwardBase"),
            new ShaderTagId("PrepassBase"),
            new ShaderTagId("Vertex"),
            new ShaderTagId("VertexLMRGBM"),
            new ShaderTagId("VertexLM")
        };
        private static Material ErrorMaterial;

        /// <summary>
        /// CommandBuffer 的渲染准备
        /// </summary>
        private partial void PrepareBuffer()
        {
            Profiler.BeginSample("Editor Only");
            // 不同的相机设置不同的 CommandBuffer 名字，以便在 FrameDebugger 区分不同相机的采样
            _commandBuffer.name = SampleName = _camera.name;
            Profiler.EndSample();
        }

        /// <summary>
        /// Scene 窗口的渲染准备
        /// </summary>
        private partial void PrepareForSceneWindow()
        {
            if (_camera.cameraType is CameraType.SceneView)
            {
                // 将 UI 显示添加到 Scene 窗口中
                ScriptableRenderContext.EmitWorldGeometryForSceneView(_camera);
            }
        }

        /// <summary>
        /// 绘制不支持的 Shader
        /// </summary>
        private partial void DrawUnsupportedShaders()
        {
            if (ErrorMaterial == null) ErrorMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));

            var drawingSettings = new DrawingSettings(LegacyShaderTagIds[0], new SortingSettings(_camera))
            {
                overrideMaterial = ErrorMaterial,
            };
            for (int i = 1; i < LegacyShaderTagIds.Length; i++)
            {
                drawingSettings.SetShaderPassName(i, LegacyShaderTagIds[i]);
            }
            var filteringSettings = FilteringSettings.defaultValue;
            _context.DrawRenderers(_cullingResults, ref drawingSettings, ref filteringSettings);
        }

        /// <summary>
        /// 绘制 Gizmos
        /// </summary>
        private partial void DrawGizmos()
        {
            if (Handles.ShouldRenderGizmos())
            {
                _context.DrawGizmos(_camera, GizmoSubset.PreImageEffects);
                _context.DrawGizmos(_camera, GizmoSubset.PostImageEffects);
            }
        }
#else
    private partial void PrepareBuffer() { }
    private partial void PrepareForSceneWindow() { }
    private partial void DrawUnsupportedShaders() { }
    private partial void DrawGizmos() { }
#endif
    }
}