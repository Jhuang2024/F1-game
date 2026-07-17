// Premium visual pass: the game's single full-screen post chain for the
// built-in render pipeline, driven by CameraPostFx (OnRenderImage).
//   Pass 0 - bright-pass downsample (bloom threshold, quarter res)
//   Pass 1 - separable 9-tap gaussian blur (direction in _BlurDir)
//   Pass 2 - final composite: bloom add, exposure, filmic (ACES-approx)
//            tonemap, saturation/contrast grade, vignette
// The HDR camera was already rendering values above 1.0 - without a
// tonemapper they simply clipped, which is a large part of why the old
// image read as flat and dated. This is deliberately dependency-free (no
// PostProcessing package) so the project can never fail to compile or
// resolve because of it.
Shader "Hidden/RacePostUber"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _BloomTex;
        float2 _BlurDir;
        float _BloomThreshold;
        float _BloomIntensity;
        float _Exposure;
        float _Saturation;
        float _Contrast;
        float _VignetteStrength;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float2 uv : TEXCOORD0;
            float4 vertex : SV_POSITION;
        };

        v2f vert (appdata v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }

        // Narkowicz ACES approximation - cheap, stable, no LUT needed.
        float3 AcesTonemap(float3 x)
        {
            return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
        }
        ENDCG

        // Pass 0: bright-pass downsample.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float brightness = max(c.r, max(c.g, c.b));
                float knee = _BloomThreshold * 0.5;
                float soft = saturate((brightness - _BloomThreshold + knee) / (2.0 * knee));
                float contribution = max(soft * soft, step(_BloomThreshold, brightness));
                return fixed4(c * contribution, 1.0);
            }
            ENDCG
        }

        // Pass 1: separable gaussian blur. 13-tap kernel via 7 linear-filtered
        // samples (was an effective 9-tap via 5) - the wider, denser kernel
        // removes the stepped halo rings the smaller kernel left around
        // strong bloom sources like floodlights and the setting sun.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float2 stepUv = _BlurDir * _MainTex_TexelSize.xy;
                float3 c = tex2D(_MainTex, i.uv).rgb * 0.196482;
                c += tex2D(_MainTex, i.uv + stepUv * 1.411764).rgb * 0.296906;
                c += tex2D(_MainTex, i.uv - stepUv * 1.411764).rgb * 0.296906;
                c += tex2D(_MainTex, i.uv + stepUv * 3.294117).rgb * 0.094470;
                c += tex2D(_MainTex, i.uv - stepUv * 3.294117).rgb * 0.094470;
                c += tex2D(_MainTex, i.uv + stepUv * 5.176470).rgb * 0.010381;
                c += tex2D(_MainTex, i.uv - stepUv * 5.176470).rgb * 0.010381;
                return fixed4(c, 1.0);
            }
            ENDCG
        }

        // Pass 2: composite + grade.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float3 bloom = tex2D(_BloomTex, i.uv).rgb;
                c += bloom * _BloomIntensity;
                c *= _Exposure;
                c = AcesTonemap(c);

                // Grade in display space: saturation around luma, then a
                // gentle contrast S-curve pivoted at mid grey.
                float luma = dot(c, float3(0.2126, 0.7152, 0.0722));
                c = lerp(float3(luma, luma, luma), c, _Saturation);
                c = saturate((c - 0.5) * _Contrast + 0.5);

                // Vignette: subtle corner falloff to pull the eye centre-frame.
                float2 fromCentre = i.uv - 0.5;
                float vignette = 1.0 - _VignetteStrength * smoothstep(0.25, 0.75, dot(fromCentre, fromCentre) * 2.0);
                c *= vignette;

                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
