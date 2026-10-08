// 항구 하늘(스카이박스): 위→수평선 그라데이션, 수평선의 먼 도시·산 실루엣과 작은 불빛, 별, 해(새벽)와 그 둘레의 붉은 빛.
// _Dawn(0~1)으로 밤→새벽을 섞는다(C# SkyCycle이 올린다)
Shader "Game/HarborSky"
{
    Properties
    {
        _NightTop ("Night Top", Color) = (0.012, 0.016, 0.035, 1)
        _NightHorizon ("Night Horizon", Color) = (0.07, 0.075, 0.11, 1)
        _DawnTop ("Dawn Top", Color) = (0.16, 0.22, 0.38, 1)
        _DawnHorizon ("Dawn Horizon", Color) = (0.95, 0.55, 0.32, 1)
        _Ground ("Below Horizon", Color) = (0.02, 0.025, 0.035, 1)
        _Silhouette ("Skyline", Color) = (0.015, 0.018, 0.028, 1)
        _CityLights ("City Lights", Color) = (1.0, 0.7, 0.4, 1)
        _SunDir ("Sun Direction", Vector) = (0.15, 0.03, 1, 0)
        _SunColor ("Sun Color", Color) = (1.0, 0.62, 0.32, 1)
        _Dawn ("Dawn", Range(0, 1)) = 0
        _Stars ("Stars", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _NightTop, _NightHorizon, _DawnTop, _DawnHorizon, _Ground, _Silhouette, _CityLights, _SunDir, _SunColor;
            float _Dawn, _Stars;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.dir = i.pos.xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise1(float x) { float i = floor(x), f = frac(x); float a = hash(float2(i, 0)), b = hash(float2(i + 1, 0)); return lerp(a, b, f * f * (3 - 2 * f)); }

            float4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float az = atan2(d.x, d.z);              // -pi..pi
                float h = d.y;

                float3 top = lerp(_NightTop.rgb, _DawnTop.rgb, _Dawn);
                float3 hor = lerp(_NightHorizon.rgb, _DawnHorizon.rgb, _Dawn);
                // 해 쪽 수평선이 더 붉다
                float3 sun = normalize(_SunDir.xyz);
                float toSun = saturate(dot(normalize(float3(d.x, 0, d.z)), normalize(float3(sun.x, 0, sun.z))));
                hor = lerp(hor, hor * float3(1.15, 0.85, 0.75), (1 - toSun) * _Dawn * 0.5);
                float t = pow(saturate(h), 0.45);
                float3 col = lerp(hor, top, t);

                // 밤: 비구름(도시 불빛을 받아 아래가 살짝 주황빛). 새벽엔 구름이 걷히며 붉게 물든다
                float2 cp = d.xz / max(0.08, d.y + 0.12) * 1.6;
                float cl = 0;
                float amp = 0.5;
                for (int o = 0; o < 4; o++) { cl += amp * (noise1(cp.x * 1.7 + cp.y * 0.6 + o * 13.1) * 0.5 + noise1(cp.y * 2.3 - cp.x * 0.4 + o * 7.7) * 0.5); cp *= 2.1; amp *= 0.5; }
                float cover = lerp(0.95, 0.45, _Dawn);
                float cloud = saturate((cl - (1 - cover) * 0.9) * 2.2) * saturate(h * 6);
                float3 cloudCol = lerp(float3(0.045, 0.045, 0.06) + float3(0.05, 0.03, 0.015) * (1 - t), lerp(hor, float3(0.55, 0.35, 0.35), 0.4) * 0.75, _Dawn);
                col = lerp(col, cloudCol, cloud * 0.85);
                col += step(0.9975, hash(floor(d.xz / max(0.15, d.y + 0.2) * 220))) * saturate(h * 4) * _Stars * (1 - cloud) * 0.4;

                // 해와 빛무리
                float sd = saturate(dot(d, sun));
                col += _SunColor.rgb * (pow(sd, 900) * 6 + pow(sd, 18) * 0.45) * _Dawn;

                // 수평선 실루엣 두 겹: 먼 산(안개에 묻혀 하늘색에 가깝다) · 가까운 도시(어둡고 불빛)
                float far = 0.010 + noise1(az * 5 + 3) * 0.016 + noise1(az * 17) * 0.004;
                float bx = floor(az * 210);
                float tall = hash(float2(bx, 3));
                float bld = step(0.42, hash(float2(bx, 7))) * (0.002 + tall * tall * 0.016) * step(0.35, noise1(az * 2.4 + 11));
                float near = 0.004 + noise1(az * 9 + 40) * 0.006 + bld;
                if (h < far && h > -0.02)
                    col = lerp(hor, _Silhouette.rgb, 0.45 - _Dawn * 0.15);
                if (h < near && h > -0.02)
                {
                    float3 sil = lerp(_Silhouette.rgb, _Silhouette.rgb + hor * 0.1, _Dawn);
                    float2 wp = float2(floor(az * 1400), floor(h * 1400));
                    float lit = step(0.975, hash(wp)) * step(0.002, near - h) * (1 - _Dawn * 0.85);
                    col = sil + _CityLights.rgb * lit * 1.3;
                }
                if (h < -0.02) col = lerp(hor * 0.5, _Ground.rgb, saturate(-h * 8));
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
