using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 编辑器专用
/// </summary>
public partial class CameraRenderer
{
    private partial void DrawGizmos();
    private partial void DrawUnsupportedShaders();

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

#endif
}