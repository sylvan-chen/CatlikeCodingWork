Shader "Custom RP/Lit"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {} // 必须以一个空代码块结束纹理属性的定义，该属性很久以前用于控制纹理设置，但如今仍需保留，以避免在某些情况下出现异常错误
        _BaseColor("Color", Color) = (0.5, 0.5, 0.5, 1.0)
        // Alpha 裁剪
        // 一种材质通常要么使用透明度混合，要么使用 Alpha 裁剪，不会同时使用两者
        // 典型的裁剪材质通常使用 AlphaTest 渲染队列 (在所有不透明物体之后渲染)，因为使用了裁剪就无法再假定三角形完全覆盖其后方内容，导致一些 GPU 优化无法实现
        // Toggle(...) 的参数会添加一个 shader keyword，勾选/不勾选会启用/禁用该 keyword
        [Toggle(_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        // 表面可以是完全漫反射的、完美镜面的，或是介于两者之间的任何状态。我们有多种方法来控制这一点。
        // 表面是金属性的还是非金属性的
        _Metallic ("Metallic", Range(0, 1)) = 0
        // 表面的光滑程度
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        // 漫反射是否预乘透明度
        [Toggle(_PREMULTIPLY_ALPHA)] _PremulAlpha ("Premultiply Alpha", Float) = 0
        // 混合模式用于支持透明
        // - ScrBlend 代表当前要绘制的内容要使用的混合模式
        // - DstBlend 代表之前绘制的内容要使用的混合模式
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        // 透明渲染通常不会写入深度缓冲区，因为它无法从中获益，甚至可能产生不希望的结果
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
    }

    SubShader
    {
        Pass
        {
            Tags
            {
                // 在 Unity 的 Shader 编写中，一个 Shader 可以包含多个 Pass（渲染通道）。比如一个复杂的物体可能需要一个 Pass 画基础颜色，一个 Pass 画阴影，另一个 Pass 画高光轮廓。
                // 为了让渲染管线能够精确控制“现在去画哪一个 Pass”，Unity 引入了 LightMode 标签。
                "LightMode" = "CustomLit"
            }

            Blend [_SrcBlend] [_DstBlend] // 支持混合模式 (透明效果)
            ZWrite [_ZWrite] // 支持写入深度

            HLSLPROGRAM
            #pragma target 3.5 // 只支持 3.5 以上版本
            // Unity 不会把一个 shader 编译成一个完整的程序。它会根据 keyword 的开/关组合，编译出多个不同版本的 shader，每个版本叫做一个 变体（variant）
            #pragma shader_feature _CLIPPING
            #pragma shader_feature _PREMULTIPLY_ALPHA
            #pragma multi_compile_instancing // 支持 GPU Instance 变体
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment
            #include "LitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                // 画阴影（调用 _context.DrawShadows）时，只会挑这个 LightMode 的 Pass 来画
                "LightMode" = "ShadowCaster"
            }

            // 只需写入深度，禁用颜色数据的写入
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5 // 只支持 3.5 以上版本
            #pragma shader_feature _CLIPPING
            #pragma multi_compile_instancing // 支持 GPU Instance 变体
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "CustomRP.Editor.CustomShaderGUI"
}