#ifndef DOTS_CROWD_VOXEL_UNIT_CORE_INCLUDED
#define DOTS_CROWD_VOXEL_UNIT_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    float4 _AnimParams;
    float _ModelHeight;
    float _BobAmount;
    float _SwayAmount;
    float _LeanAmount;
    float _LungeAmount;
    half _AmbientFloor;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _AnimParams)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#define _AnimParams UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AnimParams)
#endif

float3 _LightDirection;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4  color      : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    half3  color      : COLOR;
    float3 normalWS   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float3 RotateX(float3 p, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float3(p.x, p.y * c - p.z * s, p.y * s + p.z * c);
}

float3 RotateZ(float3 p, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float3(p.x * c - p.y * s, p.x * s + p.y * c, p.z);
}

// Procedural animation around the feet (object-space origin), driven by one per-instance float4:
// x gait phase [0,1), y move blend, z attack swing (1 -> 0),
// w emerge (-1 -> 0, rising out of the ground) or death progress (0 -> 1).
void AnimateObjectSpace(inout float3 position, inout float3 normal)
{
    float4 anim = _AnimParams;
    float height = max(_ModelHeight, 1e-3);
    float wave = sin(anim.x * 6.2831853);

    // Idle breathing runs on time (offset by the agent's phase), so agents at rest need no CPU updates.
    // It fades out as the agent starts to move.
    float breathe = sin((_Time.y * 0.7 + anim.x) * 6.2831853);
    position.y *= 1.0 + (1.0 - anim.y) * breathe * 0.03;

    // Attack: a quick forward lunge, peaking in the middle of the swing.
    float swing = anim.z * (1.0 - anim.z) * 4.0;
    float pitch = _LeanAmount * anim.y + swing * 0.5;
    float roll = wave * _SwayAmount * anim.y;

    // Death: topple sideways, then sink into the ground.
    roll += smoothstep(0.0, 0.6, anim.w) * 1.45;

    position = RotateZ(RotateX(position, pitch), roll);
    normal = RotateZ(RotateX(normal, pitch), roll);

    position.y += abs(wave) * _BobAmount * height * anim.y;
    position.z += swing * _LungeAmount * height;
    position.y -= smoothstep(0.55, 1.0, anim.w) * height * 0.9;
    position.y -= saturate(-anim.w) * height * 1.05;
}

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionOS = input.positionOS.xyz;
    float3 normalOS = input.normalOS;
    AnimateObjectSpace(positionOS, normalOS);

    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);

#if defined(VOXEL_SHADOW_CASTER_PASS)
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
    #if UNITY_REVERSED_Z
        positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #else
        positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #endif
    output.positionCS = positionCS;
#else
    output.positionCS = TransformWorldToHClip(positionWS);
#endif

    half3 color = input.color.rgb;
#if !defined(UNITY_COLORSPACE_GAMMA)
    color = SRGBToLinear(color);
#endif
    output.color = color * _BaseColor.rgb;
    output.normalWS = normalWS;
    return output;
}

half4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

#if defined(VOXEL_SHADOW_CASTER_PASS) || defined(VOXEL_DEPTH_PASS)
    return 0;
#else
    half3 normalWS = normalize(input.normalWS);
    Light mainLight = GetMainLight();
    half diffuse = saturate(dot(normalWS, mainLight.direction));
    half3 ambient = max(SampleSH(normalWS), _AmbientFloor.xxx);
    return half4(input.color * (mainLight.color * diffuse + ambient), 1.0);
#endif
}

#endif
