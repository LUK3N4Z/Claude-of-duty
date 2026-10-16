// PiP lens pass: turns the scope camera's render into what the eye sees through the ocular.
// Barrel refraction toward the rim, rim chromatic aberration, the eye-relief (parallax) shadow that follows the eye,
// an etched reticle drawn at the focal plane and its illuminated centre (HDR, so the main camera's bloom catches it).
// Input and output are scene radiance (the scope camera does not expose); the lens material exposes it with the main view.
Shader "Hidden/FPS/ScopeLens"
{
    Properties
    {
        _MainTex ("Scope Image", 2D) = "black" {}
        _Aberration ("Rim Chromatic Aberration", Range(0, 0.05)) = 0.012
        _Distortion ("Rim Refraction", Range(0, 0.5)) = 0.12
        _ShadowCenter ("Shadow Centre (uv)", Vector) = (0.5, 0.5, 0, 0)
        _ShadowRadius ("Clear Radius (1 = rim)", Float) = 1.0
        _ShadowSoftness ("Shadow Softness", Float) = 0.25
        _ReticleStyle ("Reticle (0 none, 1 duplex dot, 2 chevron)", Float) = 1
        _ReticleScale ("Reticle Scale", Float) = 1
        _ReticleColor ("Etched Reticle", Color) = (0, 0, 0, 1)
        [HDR] _GlowColor ("Illuminated Centre (exposed units at a mid-grey key)", Color) = (4, 0.25, 0.12, 1)
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Aberration;
            float _Distortion;
            float4 _ShadowCenter;
            float _ShadowRadius;
            float _ShadowSoftness;
            float _ReticleStyle;
            float _ReticleScale;
            float4 _ReticleColor;
            float4 _GlowColor;

            // Exposed value of mid grey: the illuminated centre is set against the scene's mean radiance like the eye sees it.
            static const float MidGrey = 0.18;
            // Brightest radiance passed on (cd/m^2): keeps a sun disk finite in the half-float lens target.
            static const float MaxRadiance = 60000.0;

            // Coverage of a line of half width w at distance d, antialiased over one texel.
            float Line(float d, float w, float aa)
            {
                return 1.0 - smoothstep(w, w + aa, d);
            }

            float4 Frag(v2f_img i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                float r = length(c) * 2.0; // 0 centre, 1 rim

                // Glass refraction: the image bends outward near the rim (barrel), stronger with radius.
                float2 bent = c * (1.0 + _Distortion * r * r);
                // Rim chromatic aberration: red focuses wide, blue narrow.
                float ca = _Aberration * r * r;
                float3 color;
                color.r = tex2D(_MainTex, 0.5 + bent * (1.0 + ca)).r;
                color.g = tex2D(_MainTex, 0.5 + bent).g;
                color.b = tex2D(_MainTex, 0.5 + bent * (1.0 - ca)).b;
                color = min(color, MaxRadiance); // also drops NaN (IEEE minNum)

                // Eye-relief shadow: the clear exit-pupil disc slides with the eye; outside it the view goes black.
                float d = length(i.uv - _ShadowCenter.xy) * 2.0;
                color *= 1.0 - smoothstep(_ShadowRadius, _ShadowRadius + _ShadowSoftness, d);
                color *= smoothstep(1.0, 0.94, r); // field stop

                if (_ReticleStyle > 0.5)
                {
                    float scale = max(_ReticleScale, 1e-3);
                    float aa = _MainTex_TexelSize.x * 2.0 / scale;
                    float2 q = c * 2.0 / scale;
                    float2 a = abs(q);
                    float along = max(a.x, a.y);
                    float across = min(a.x, a.y);

                    // Etched glass: thick posts outside, fine crosshair inside (black ink, absorbs the image).
                    float posts = step(0.42, along) * Line(across, 0.012, aa);
                    float fine = step(along, 0.42) * Line(across, 0.0018, aa);
                    float ink = saturate(posts + fine) * step(r, 0.97);
                    color = lerp(color, _ReticleColor.rgb, ink * _ReticleColor.a);

                    // Illuminated centre: a dot (duplex) or an inverted chevron with its tip on the point of aim. Its
                    // brightness follows the light in view (mean of five taps), as a shooter sets the rheostat.
                    float3 taps = tex2D(_MainTex, 0.5).rgb + tex2D(_MainTex, float2(0.3, 0.3)).rgb + tex2D(_MainTex, float2(0.7, 0.3)).rgb +
                                  tex2D(_MainTex, float2(0.3, 0.7)).rgb + tex2D(_MainTex, float2(0.7, 0.7)).rgb;
                    float key = dot(min(taps, MaxRadiance), float3(0.2126, 0.7152, 0.0722)) / 5.0;
                    float glow;
                    if (_ReticleStyle < 1.5)
                    {
                        glow = Line(length(q), 0.007, aa);
                    }
                    else
                    {
                        float ax = abs(q.x);
                        float stroke = abs(q.y + ax * 1.15) * 0.66;
                        glow = Line(stroke, 0.0035, aa) * step(ax, 0.035) * step(q.y, 0.003);
                    }

                    color += _GlowColor.rgb * glow * (key / MidGrey);
                }

                return float4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
