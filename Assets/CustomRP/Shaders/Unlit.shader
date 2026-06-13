Shader "Custom RP/Unlit"
{
    Properties
    {
        _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
    }

    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma multi_compile_instancing // 支持 GPU Instance 变体
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment
            #include "UnlitPass.hlsl"
            ENDHLSL
        }
    }
}