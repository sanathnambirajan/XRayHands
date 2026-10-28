// X-Ray Hands volume ray marcher (URP, single-pass-instanced stereo safe).
//
// Geometry: a unit cube [-0.5, 0.5]^3 in object space, drawn with front faces culled so the
// volume still renders when the camera is inside it. For each pixel we build the ray from the
// eye (per eye, in object space), intersect it with the box, the clip plane and the opaque
// scene depth, then composite front to back.
//
// Texture layout (see VolumeDataset):
//   _DensityTex  RGBA8: rgb = gradient packed to [0,1], a = density      (trilinear)
//   _LabelTex    RG8:   label id = r + 256 g                              (point)
//   _BrickTex    RG8:   min / max density per 8^3 brick                   (point)
//   _TransferTex 256x1: rgba transfer function by density
//   _IdLut       256xN: per-id display colour (rgb) and opacity (a), computed on the CPU
//                       from class / ground truth / selection, so all mode logic is C#.
Shader "XRayHands/XRayVolume"
{
    Properties
    {
        _DensityTex ("Density + gradient", 3D) = "" {}
        _LabelTex ("Labels", 3D) = "" {}
        _BrickTex ("Brick min/max", 3D) = "" {}
        _TransferTex ("Transfer function", 2D) = "white" {}
        _IdLut ("Per-id colours", 2D) = "black" {}
        _StepCount ("Max steps along the diagonal", Range(16, 512)) = 160
        _Peel ("Peel threshold (0..1 density)", Range(0, 1)) = 0
        _SilhouetteStrength ("Glass silhouette strength", Range(0, 4)) = 1.5
        _GradientWeight ("Gradient-magnitude weighting", Range(0, 1)) = 0.6
        _WindowLevel ("Slice window level", Range(0, 1)) = 0.5
        _WindowWidth ("Slice window width", Range(0.01, 1)) = 1
        _DefectGlow ("Defect glow", Range(0, 4)) = 1.5
        _ClipPlane ("Clip plane (object space n.xyz, d)", Vector) = (0, 0, 1, 10)
        _ClipEnabled ("Clip enabled", Float) = 0
        _LensCentreWS ("Lens centre (world)", Vector) = (0, 0, 0, 0)
        _LensNormalWS ("Lens normal (world)", Vector) = (0, 0, 1, 0)
        _LensRadius ("Lens radius (m)", Float) = 0
        _LensPeel ("Peel inside the lens", Range(0, 1)) = 0.6
        _IdLutSize ("Id LUT size (w, h, 1/w, 1/h)", Vector) = (256, 1, 0.00390625, 1)
        _BrickGrid ("Brick grid dims (x, y, z, brick voxels)", Vector) = (32, 32, 32, 8)
        _VolumeDims ("Volume dims (x, y, z, 0)", Vector) = (256, 256, 256, 0)
        _DebugMode ("Debug: 0 off, 1 box hit, 2 ignore scene depth", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "VolumeRaymarch"
            Cull Front
            ZWrite Off
            ZTest Always   // we clip against scene depth ourselves, per sample
            Blend One OneMinusSrcAlpha // premultiplied alpha output

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE3D(_DensityTex);
            TEXTURE3D(_LabelTex);
            TEXTURE3D(_BrickTex);
            TEXTURE2D(_TransferTex);
            TEXTURE2D(_IdLut);
            // Inline sampler states: Unity derives filter/wrap from the names.
            SAMPLER(my_linear_clamp_sampler);
            SAMPLER(my_point_clamp_sampler);

            CBUFFER_START(UnityPerMaterial)
                float _StepCount;
                float _Peel;
                float _SilhouetteStrength;
                float _GradientWeight;
                float _WindowLevel;
                float _WindowWidth;
                float _DefectGlow;
                float4 _ClipPlane;
                float _ClipEnabled;
                float4 _LensCentreWS;
                float4 _LensNormalWS;
                float _LensRadius;
                float _LensPeel;
                float4 _IdLutSize;
                float4 _BrickGrid;
                float4 _VolumeDims;
                float _DebugMode;
            CBUFFER_END

            static const float kOpaqueAlpha = 0.97;   // early ray termination
            static const float kReferenceSteps = 160; // opacity is authored for this step count

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            // Slab test against the unit box; returns (tNear, tFar).
            float2 IntersectBox(float3 origin, float3 dir)
            {
                float3 inv = 1.0 / dir;
                float3 t0 = (-0.5 - origin) * inv;
                float3 t1 = (0.5 - origin) * inv;
                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);
                return float2(max(max(tMin.x, tMin.y), tMin.z), min(min(tMax.x, tMax.y), tMax.z));
            }

            // Interleaved gradient noise (Jimenez 2014): cheap, texture-free, low-discrepancy
            // per-pixel jitter. Offsetting each ray's start by a fraction of a step turns
            // wood-grain banding into fine noise that the eye ignores.
            float InterleavedGradientNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            int LabelAt(float3 uvw)
            {
                float2 rg = SAMPLE_TEXTURE3D_LOD(_LabelTex, my_point_clamp_sampler, uvw, 0).rg;
                return (int)round(rg.r * 255.0) + 256 * (int)round(rg.g * 255.0);
            }

            float4 IdColour(int id)
            {
                float2 uv = float2((id % (int)_IdLutSize.x) + 0.5, (id / (int)_IdLutSize.x) + 0.5) * _IdLutSize.zw;
                return SAMPLE_TEXTURE2D_LOD(_IdLut, my_point_clamp_sampler, uv, 0);
            }

            // Transfer function with peel: material below the peel threshold fades to glass,
            // keeping only a view-dependent silhouette so the outer shape stays readable.
            float4 Classify(float density, float3 gradient, float3 viewDirOS, float peel)
            {
                float4 tf = SAMPLE_TEXTURE2D_LOD(_TransferTex, my_linear_clamp_sampler, float2(density, 0.5), 0);
                float gradMag = saturate(length(gradient));
                // Gradient-magnitude weighting: boundaries opaque, homogeneous interiors lighter.
                tf.a *= lerp(1.0, saturate(gradMag * 4.0), _GradientWeight);
                if (density < peel)
                {
                    float3 n = gradMag > 1e-4 ? gradient / gradMag : float3(0, 0, 1);
                    float rim = pow(1.0 - abs(dot(n, viewDirOS)), 3.0);
                    tf.a *= 0.02 + _SilhouetteStrength * rim * gradMag;
                    tf.rgb = lerp(tf.rgb, float3(0.75, 0.85, 1.0), 0.6); // cool glass tint
                }
                return tf;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Per-eye camera position in object space (GetCameraPositionWS is stereo-aware).
                float3 camWS = GetCameraPositionWS();
                float3 camOS = TransformWorldToObject(camWS);
                float3 dirOS = normalize(i.positionOS - camOS);

                float2 hit = IntersectBox(camOS, dirOS);
                float tNear = max(hit.x, 0.0);
                float tFar = hit.y;
                if (tFar <= tNear) discard;
                if (_DebugMode > 0.5 && _DebugMode < 1.5) return half4(0.3 + 0.7 * frac((camOS + dirOS * tNear) + 0.5), 1);

                // Clip plane: keep n.p + d >= 0. Track whether the ray enters through the plane.
                bool enteredThroughPlane = false;
                if (_ClipEnabled > 0.5)
                {
                    float denom = dot(_ClipPlane.xyz, dirOS);
                    float side = dot(_ClipPlane.xyz, camOS + dirOS * tNear) + _ClipPlane.w;
                    float tPlane = -(dot(_ClipPlane.xyz, camOS) + _ClipPlane.w) / denom;
                    if (abs(denom) < 1e-6)
                    {
                        if (side < 0.0) discard;
                    }
                    else if (denom > 0.0)
                    {
                        if (tPlane > tNear) { tNear = tPlane; enteredThroughPlane = true; }
                    }
                    else
                    {
                        tFar = min(tFar, tPlane);
                    }
                    if (tFar <= tNear) discard;
                }

                // Stop at opaque scene geometry (hands, table, chart) so it occludes the volume.
                float2 screenUV = i.positionCS.xy / _ScaledScreenParams.xy;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float3 viewForwardWS = -UNITY_MATRIX_V[2].xyz;
                float3 dirWS = normalize(TransformObjectToWorldDir(dirOS));
                float3 entryWS = TransformObjectToWorld(camOS + dirOS * tNear);
                float3 exitWS = TransformObjectToWorld(camOS + dirOS * tFar);
                float depthEntry = dot(entryWS - camWS, viewForwardWS);
                float depthExit = dot(exitWS - camWS, viewForwardWS);
                if (_DebugMode > 2.5) return half4(saturate(sceneDepth / 2.0), saturate(depthEntry / 2.0), saturate(SampleSceneDepth(screenUV) * 10.0), 1);
                if (_DebugMode > 1.5) sceneDepth = 1e6;
                if (sceneDepth < depthEntry) discard;
                if (sceneDepth < depthExit)
                {
                    tFar = lerp(tNear, tFar, saturate((sceneDepth - depthEntry) / max(depthExit - depthEntry, 1e-5)));
                }

                // Magic lens: if this pixel's view ray passes through the lens disc, the whole
                // ray uses the lens peel (focus + context).
                float peel = _Peel;
                if (_LensRadius > 0.0)
                {
                    float d = dot(dirWS, _LensNormalWS.xyz);
                    if (abs(d) > 1e-4)
                    {
                        float t = dot(_LensCentreWS.xyz - camWS, _LensNormalWS.xyz) / d;
                        float3 p = camWS + dirWS * t;
                        if (t > 0.0 && length(p - _LensCentreWS.xyz) < _LensRadius) peel = max(peel, _LensPeel);
                    }
                }

                // Step size: the unit cube diagonal (sqrt 3) is divided into _StepCount steps.
                float dt = 1.7320508 / _StepCount;
                float jitter = InterleavedGradientNoise(i.positionCS.xy);
                float t = tNear + jitter * dt;
                float opacityScale = kReferenceSteps / _StepCount; // opacity correction for step size

                float4 acc = 0;

                // Cut face: real CT slice look. If the first sample behind the plane is material
                // or a defect, show the windowed grey value opaquely (with defect tint).
                if (enteredThroughPlane)
                {
                    float3 uvw = camOS + dirOS * (tNear + 1e-4) + 0.5;
                    float density = SAMPLE_TEXTURE3D_LOD(_DensityTex, my_linear_clamp_sampler, uvw, 0).a;
                    int id = LabelAt(uvw);
                    float4 idc = id > 0 ? IdColour(id) : 0;
                    float lo = _WindowLevel - 0.5 * _WindowWidth;
                    float grey = saturate((density - lo) / _WindowWidth);
                    float4 tf = SAMPLE_TEXTURE2D_LOD(_TransferTex, my_linear_clamp_sampler, float2(density, 0.5), 0);
                    if (tf.a > 0.01 || idc.a > 0.01)
                    {
                        float3 rgb = lerp(grey.xxx, idc.rgb, idc.a > 0.01 ? 0.75 : 0.0);
                        return half4(rgb, 1.0);
                    }
                }

                float3 voxelsPerBrick = _BrickGrid.www;
                [loop]
                for (int s = 0; s < 1024; s++)
                {
                    if (t > tFar || s >= (int)_StepCount * 2) break;
                    float3 pos = camOS + dirOS * t;
                    float3 uvw = pos + 0.5;

                    // Empty-space skipping: if nothing in this brick can be visible, jump to its exit.
                    float3 brick = floor(uvw * _VolumeDims.xyz / voxelsPerBrick);
                    float2 mm = SAMPLE_TEXTURE3D_LOD(_BrickTex, my_point_clamp_sampler, (brick + 0.5) / _BrickGrid.xyz, 0).rg;
                    float4 tfMax = SAMPLE_TEXTURE2D_LOD(_TransferTex, my_linear_clamp_sampler, float2(mm.y, 0.5), 0);
                    if (tfMax.a < 0.002 && mm.y < 0.999)
                    {
                        float3 bMin = brick * voxelsPerBrick / _VolumeDims.xyz - 0.5;
                        float3 bMax = (brick + 1.0) * voxelsPerBrick / _VolumeDims.xyz - 0.5;
                        float3 tExit3 = (dirOS > 0 ? bMax - pos : bMin - pos) / dirOS;
                        float tExit = min(min(tExit3.x, tExit3.y), tExit3.z);
                        t += max(ceil(tExit / dt), 1.0) * dt; // stay on the jittered step grid
                        continue;
                    }

                    float4 v = SAMPLE_TEXTURE3D_LOD(_DensityTex, my_linear_clamp_sampler, uvw, 0);
                    float3 gradient = v.rgb * 2.0 - 1.0;
                    float4 c = Classify(v.a, gradient, -dirOS, peel);

                    int id = LabelAt(uvw);
                    if (id > 0)
                    {
                        float4 idc = IdColour(id);
                        // Defects are emissive and survive peeling: they are the analysis target.
                        c = lerp(c, float4(idc.rgb * _DefectGlow, idc.a), idc.a);
                    }

                    // Headlight shading from the baked gradient.
                    float gm = saturate(length(gradient) * 4.0);
                    float3 n = gm > 1e-3 ? normalize(gradient) : 0;
                    float diffuse = abs(dot(n, dirOS));
                    c.rgb *= lerp(1.0, 0.35 + 0.65 * diffuse, gm);

                    c.a = 1.0 - pow(saturate(1.0 - c.a), opacityScale);
                    acc.rgb += (1.0 - acc.a) * c.a * c.rgb;
                    acc.a += (1.0 - acc.a) * c.a;
                    if (acc.a > kOpaqueAlpha) break;
                    t += dt;
                }

                if (acc.a < 0.002) discard;
                return half4(acc);
            }
            ENDHLSL
        }
    }
}
