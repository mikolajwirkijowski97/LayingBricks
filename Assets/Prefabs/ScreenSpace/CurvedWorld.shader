Shader "Custom/URP Curved World HLSL"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Curvature ("Curvature", Float) = 0.001
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Culling", Float) = 2 // Default: Backface culling
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100 // URP doesn't use LOD the same way as Built-in

        Pass
        {
            Name "ForwardLit" // Or just "Forward" for Unlit often
            Tags { "LightMode"="UniversalForward" } // Important URP tag

            Cull [_Cull] // Use property for culling mode
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP Core Library
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // If Lit, include Lighting.hlsl and Input.hlsl

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
            };

            // Shader Properties
            TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
            float4 _MainTex_ST; // For UV tiling/offset
            float _Curvature;

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0; // Initialize output

                // 1. Vertex to World Space
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // 2. Get Camera Position (World Space)
                // _WorldSpaceCameraPos usually works, or use GetCameraPositionWS()
                float3 cameraPosWS = GetCameraPositionWS(); 

                // 3. Calculate Z distance relative to camera
                float distZ = positionWS.z - cameraPosWS.z;

                // 4. Calculate World Space Y Offset
                float offsetY = (distZ * distZ) * -_Curvature;

                // 5. Apply Offset to World Position
                positionWS.y += offsetY;

                // 6. Transform modified World Position to Clip Space
                output.positionCS = TransformWorldToHClip(positionWS);

                // 7. Pass UVs, applying tiling/offset
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // Sample the texture
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Just return texture color for Unlit
                return texColor;
            }
            ENDHLSL
        }
    }
    // No Fallback needed in URP, it handles missing passes differently
}