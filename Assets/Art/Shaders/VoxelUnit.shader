Shader "DotsCrowd/Voxel Unit"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _ModelHeight("Model Height", Float) = 1.5
        _BobAmount("Walk Bob", Range(0, 0.5)) = 0.08
        _SwayAmount("Walk Sway (rad)", Range(0, 0.6)) = 0.14
        _LeanAmount("Run Lean (rad)", Range(0, 0.6)) = 0.12
        _LungeAmount("Attack Lunge", Range(0, 1)) = 0.3
        _AmbientFloor("Ambient Floor", Range(0, 1)) = 0.32
        [HideInInspector] _AnimParams("Anim Params", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "VoxelUnitCore.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #define VOXEL_SHADOW_CASTER_PASS
            #include "VoxelUnitCore.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #define VOXEL_DEPTH_PASS
            #include "VoxelUnitCore.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
