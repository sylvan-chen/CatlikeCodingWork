Shader "Custom RP/Unlit"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {} // 必须以一个空代码块结束纹理属性的定义，该属性很久以前用于控制纹理设置，但如今仍需保留，以避免在某些情况下出现异常错误
        _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
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
            Blend [_SrcBlend] [_DstBlend] // 支持混合模式 (透明效果)
            ZWrite [_ZWrite] // 支持写入深度

            HLSLPROGRAM
            #pragma multi_compile_instancing // 支持 GPU Instance 变体
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment
            #include "UnlitPass.hlsl"
            ENDHLSL
        }
    }
}