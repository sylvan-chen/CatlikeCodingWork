using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 我们专门定义个新类来渲染单个摄像机
/// </summary>
public partial class CameraRenderer
{
    private const string BUFFER_NAME = "Render Camera";
    private static ShaderTagId UnlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");

    private ScriptableRenderContext _context;
    private Camera _camera;

    /// <summary>
    /// 一些指令可以通过专有命令直接执行（比如绘制天空盒），但其他命令必须通过 CommandBuffer 间接发出。
    /// 我们可以在适当的位置调用 `BeginSample` 和 `EndSample` 来注入 Profiler
    /// </summary>
    private CommandBuffer _commandBuffer = new CommandBuffer { name = BUFFER_NAME };

    /// <summary>
    /// 剔除结果
    /// </summary>
    private CullingResults _cullingResults;

    public void Render(ScriptableRenderContext context, Camera camera)
    {
        _context = context;
        _camera = camera;

        // 知识补充：渲染目标（Render Target）
        // Render Target（渲染目标）是 GPU 显存中的一块特定内存区域。渲染管线执行完所有的顶点、光照和片段（像素）计算后，最终会将生成的颜色值和深度值写入这块内存。
        // 在底层图形 API 中，它对应 DirectX 的 RTV (Render Target View) 或 OpenGL 的 FBO (Frame Buffer Object)。
        //
        // 一个完整的 Render Target 通常由两个核心缓冲区（Buffer）组成：
        // 1. Color Buffer（颜色缓冲区）： 存储每个像素的 RGBA 颜色信息。
        // 2. Depth/Stencil Buffer（深度/模板缓冲区）： 存储每个像素的深度值（Z值），用于判断物体的前后遮挡关系，确保远处的物体不会画在近处物体的前面。
        //
        // Render Target 主要有两种：
        // 1. 默认的屏幕后备缓冲区 (帧缓冲区，Backbuffer / Frame buffer)
        // 当你的 Camera 面板上的 `Target Texture` 为空时，相机的 Render Target 默认指向设备的屏幕，也就是把 Render Target 绑定为显示器上用来显示的内存，此时，渲染完毕后，画面直接“上屏”。
        // 2. 离屏渲染目标 (渲染纹理，RenderTexture)
        // 当你新建一个 `RenderTexture` 资产并赋给相机的 `Target Texture` 时，GPU 不再把结果直接画到屏幕上，而是画到这张存在于显存里的“图片”上。
        //
        // Render Target 控制的高级用法:
        // - 后处理(Post-Processing)： 你不能直接在屏幕上做高斯模糊或色彩校正。你需要先将整个场景渲染到一个 RenderTexture（作为 Render Target），
        //   然后用一个全屏的 Shader 读取这张纹理，处理完毕后，再将结果输出到屏幕（或者下一个 Render Target）。
        // - 多重渲染目标 (MRT - Multiple Render Targets)： 在延迟渲染 (Deferred Rendering) 中，GPU 会在同一遍（Pass）渲染中， 同时向多个 Render Target 输出数据
        //   (比如一张图存颜色，一张存法线，一张存金属度，这也就是所谓的 G-Buffer)。
        // - VR 渲染机制： 在 VR 开发中，Render Target 的管理尤为特殊。因为 VR 头显需要左右眼的画面，在 Single Pass Stereo 渲染模式下，Render Target 实际上是一个 Texture Array（纹理数组）。
        //   GPU 在一次 Draw Call 中同时将左眼结果写入 Array 0，右眼写入 Array 1，随后再经过畸变着色器（Distortion Shader）处理后才真正上屏。

        // 先进行剔除
        if (!Cull()) return;
        // 设置属性
        Setup();
        // 绘制所有可见的几何图形
        DrawVisibleGeometry();
        // 绘制不支持的 Shader
        DrawUnsupportedShaders();
        // 绘制 Gizmos
        DrawGizmos();
        // 我们必须在上下文上调用 Submit 来提交排队的工作以执行
        Submit();
    }

    /// <summary>
    /// 剔除操作<br/>
    /// 我们不会一股脑渲染所有物体，而是只渲染相机可见的哪些。具体做法是先找出场景中所有带有渲染器组件的物体，然后剔除掉那些位于相机视锥体外的物体。
    /// </summary>
    private bool Cull()
    {
        if (_camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            _cullingResults = _context.Cull(ref p);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 设置属性
    /// </summary>
    private void Setup()
    {
        // context.SetupCameraProperties(camera) 扮演着“搭建桥梁”的角色。
        // 它的核心作用是将相机的 C# 属性转换为 GPU（Shader）可以理解的全局变量，并配置基础渲染目标。
        //
        // 底层具体操作：
        //
        // 1. 设置 Shader 矩阵变量 (Matrices)
        // 计算并向全局 Shader 注入渲染所需的核心 MVP 变换矩阵。当你在 Shader 中写 `TransformObjectToHClip` (或旧版本的 `mul(UNITY_MATRIX_MVP), v.vertex`) 时，依赖的就是这些矩阵:
        // - `UNITY_MATRIX_V`, `UNITY_MATRIX_P`, `UNITY_MATRIX_VP` 以及它们的逆矩阵 `UNITY_MATRIX_I_V`, `UNITY_MATRIX_I_P`, `UNITY_MATRIX_I_VP`
        //
        // 2. 设置相机与屏幕参数:
        // 它会填充大量 Unity 内置的全局 Shader 变量，这些变量在着色计算中非常常用：
        // - `_WorldSpaceCameraPos`：相机的世界坐标位置（高光计算、视线方向计算必备）。
        // - `_ScreenParams`：屏幕分辨率参数（·x = width·, ·y = height·, ·z = 1 + 1/width·, ·w = 1 + 1/height·），用于屏幕特效和 UV 映射。
        // - `_ZBufferParams`：用于将深度图（Depth Texture）中的非线性深度值转换为线性深度的数学参数。
        // - `_ProjectionParams`：包含相机的 Near Clip Plane 和 Far Clip Plane，以及是否翻转了投影矩阵（如在某些平台的 RenderTexture 渲染时）。
        //
        // 3.  设置当前的 Render Target (渲染目标)
        // 调用此方法时，Unity 会隐式地将图形 API 的 Render Target 绑定到该相机的目标上。
        // - 如果相机的 `Target Texture` 为空，它会将渲染目标指向屏幕后备缓冲区 (Backbuffer)。
        // - 如果相机挂载了 RenderTexture，它会自动将渲染目标切换到该 RenderTexture 的 Color Buffer 和 Depth Buffer。
        //
        // 关键细节:
        // 必须在执行任何 `CommandBuffer.ClearRenderTarget` 或 `context.DrawRenderers` 之前调用 `SetupCameraProperties`。
        // 如果你先 Clear 了屏幕，然后再 SetupCameraProperties，在某些平台（尤其是使用了基于图块延迟渲染 Tile-Based Deferred Rendering 的移动端设备）上，可能会导致 Clear 指令被覆盖或渲染错乱。
        _context.SetupCameraProperties(_camera);

        // 必须清空旧渲染目标以防干扰当前要渲染的图像
        // 如果在 SetupCameraProperties 之前执行，通常会通过内置的 Hidden/InternalClear shader 来绘制一个全屏四边形向 Render Target 写入内容，从而达到清除的效果，这样效率较低，且某些平台可能还无法正确执行。
        // 只有在 SetupCameraProperties 之后执行，才是正常的快速清除 Clear (color+Z+stencil)
        _commandBuffer.ClearRenderTarget(true, true, Color.clear);

        _commandBuffer.BeginSample(BUFFER_NAME);
        ExecuteBuffer();
    }

    /// <summary>
    /// 绘制几何图形
    /// </summary>
    private void DrawVisibleGeometry()
    {
        // 绘制渲染器
        // 需要提供: 剔除结果、绘制设置、过滤设置

        // 通常，我们会将不透明物体和透明物体分开绘制，不透明 -> 天空盒 -> 透明
        // 1. 绘制不透明物体（从前往后）：如果某个物体被绘制在另一个物体后方，那么它被遮挡的片段就可以跳过
        // 2. 绘制天空盒：天空盒作为整个不透明大背景，作为最后一个不透明物体绘制
        // 2. 绘制透明物体（从后往前）：透明物体被绘制在另一个物体前方时，会叠加混合每个片段的颜色

        // --- 绘制不透明物体 ---
        // 排序设置需要传入相机，用于确定应用正交排序还是基于距离的排序
        var sortingSettings = new SortingSettings(_camera)
        {
            // criterial 用于强制指定绘制顺序，比如使用 CommonOpaque (通用不透明排序) 就会大致按照从前到后的顺序绘制
            criteria = SortingCriteria.CommonOpaque
        };
        // 绘制设置用于指定支持的 Shader，以及排序设置
        var drawingSettings = new DrawingSettings(UnlitShaderTagId, sortingSettings);
        // 过滤设置用于指定允许的渲染队列
        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);

        _context.DrawRenderers(_cullingResults, ref drawingSettings, ref filteringSettings);

        // --- 绘制天空盒 ---
        _context.DrawSkybox(_camera);

        // --- 绘制透明物体 ---
        sortingSettings.criteria = SortingCriteria.CommonTransparent;
        drawingSettings.sortingSettings = sortingSettings;
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;

        _context.DrawRenderers(_cullingResults, ref drawingSettings, ref filteringSettings);
    }

    /// <summary>
    /// 提交
    /// </summary>
    private void Submit()
    {
        _commandBuffer.EndSample(BUFFER_NAME);
        ExecuteBuffer();

        _context.Submit();
    }

    /// <summary>
    /// 命令缓冲区的执行 + 清除
    /// </summary>
    private void ExecuteBuffer()
    {
        // 执行缓冲区命令
        _context.ExecuteCommandBuffer(_commandBuffer);
        // 必须手动调用 Clear 才会清除命令
        _commandBuffer.Clear();
    }
}