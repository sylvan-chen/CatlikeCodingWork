using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 自定义渲染管线
/// </summary>
public class CustomRenderPipeline : RenderPipeline
{
    private readonly CameraRenderer _renderer = new CameraRenderer();

    protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

    /// <summary>
    /// Unity 会每帧调用 RP 实例的 Render 方法。
    /// </summary>
    /// <param name="context">一个上下文结构体，提供与原生引擎的连接</param>
    /// <param name="cameras">一个摄像机数组，因为场景中可能存在多个激活的摄像机，RP 需要按摄像机数组的传入顺序渲染所有摄像机</param>
    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        for (int i = 0; i < cameras.Count; i++)
        {
            _renderer.Render(context, cameras[i]);
        }
    }
}