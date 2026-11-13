// Mixes two finished pictures (the outgoing and incoming world) for WorldSequencer.
// Instead of a flat fade, the new picture seeps in through drifting cloud shapes and its brightest parts
// (the lights) arrive first.
Shader "Visualizer/WorldBlend"
{
    Properties
    {
        _MainTex ("From", 2D) = "black" {}
        _ToTex ("To", 2D) = "black" {}
        _Blend ("Blend", Range(0, 1)) = 0
        _Cloudiness ("Cloudiness", Range(0, 1)) = 0.6
        _CloudSize ("Cloud Size", Float) = 2.5
        _LightBleed ("Light Bleed", Range(0, 1)) = 0.5
        _Softness ("Softness", Range(0.02, 1)) = 0.35
        _Warp ("Warp", Range(0, 0.1)) = 0.015
        _EdgeGlow ("Edge Glow", Range(0, 2)) = 0.5
        _NoiseOffset ("Noise Offset", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Opaque" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex, _ToTex;
            float _Blend, _Cloudiness, _CloudSize, _LightBleed, _Softness, _Warp, _EdgeGlow;
            float4 _NoiseOffset;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { s += a * vnoise(p); p = p * 2.02 + 17.3; a *= 0.5; }
                return s / 0.96875;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 p = float2(i.uv.x * aspect, i.uv.y) * _CloudSize + _NoiseOffset.xy;
                float n = fbm(p);
                float2 nv = float2(fbm(p + 31.7), fbm(p + 74.1)) - 0.5;

                // Strongest in the middle of the fade, zero at both ends, so nothing pops.
                float bell = sin(_Blend * 3.14159265);
                float2 uv = i.uv + nv * _Warp * bell;

                float3 from = tex2D(_MainTex, uv).rgb;
                float3 to = tex2D(_ToTex, uv).rgb;

                // Bright parts of the incoming picture get a head start; dark parts are only held back slightly,
                // so a dark world still arrives steadily instead of all at the end.
                float lum = saturate(dot(to, float3(0.2126, 0.7152, 0.0722)) * 3.0);
                float offset = _Cloudiness * (n - 0.5) + _LightBleed * (0.15 - lum);
                float lo = -_Cloudiness * 0.5 - _LightBleed * 0.85;
                float hi = _Cloudiness * 0.5 + _LightBleed * 0.15;
                // Each pixel gets its own head start or delay, but all are fully "from" at 0 and fully "to" at 1.
                float local = _Blend * (1.0 + hi - lo) + lo - offset;
                float h = max(_Softness, 0.001) * 0.5;
                float t = smoothstep(0.5 - h, 0.5 + h, local);

                float3 col = lerp(from, to, t);
                col += to * (t * (1.0 - t) * 4.0) * bell * _EdgeGlow;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
