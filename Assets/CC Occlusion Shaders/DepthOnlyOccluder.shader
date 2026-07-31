// Depth-only occluder for real-world furniture (the study desk).
//
// WHY THIS EXISTS, alongside the Meta Depth API path used by every other shader
// in this folder:
//   The Depth API re-estimates environment depth every frame from the headset
//   cameras. On a flat, low-texture surface like a desk that estimate is noisy —
//   it wobbles a few cm frame to frame. Because the occlusion shaders resolve it
//   with a binary clip(), that wobble becomes a visible POP of the whole occluded
//   region (the seated avatar's lower body flashing into view at the desk edge).
//
//   A desk, however, is STATIC and its pose is already known from the room scan.
//   So we don't need to re-estimate it at all: we render an invisible box matching
//   the desk that writes ONLY to the depth buffer. Anything behind it fails the
//   depth test and is never shaded, so passthrough shows through instead. The edge
//   is geometric — perfectly straight, and completely stable frame to frame.
//
// HOW IT COMPOSITES: with ColorMask 0 nothing is written to the eye buffer, so
// occluded pixels keep the cleared alpha=0 and the Quest compositor shows real
// passthrough there. This is the same end result as the Depth API's clip(), just
// driven by known geometry rather than a per-frame estimate.
//
// Queue is Geometry-1 (1999) so it renders BEFORE the avatar's opaque materials
// (Geometry, 2000) and its depth is already present when they are tested.
//
// Cull Off is deliberate: the occluder box extends down to the floor and the
// participant may lean over it, putting the camera inside the volume. With back
// faces culled the box would stop occluding from in there; with Cull Off the
// near face still writes depth and it keeps working from any viewpoint.
//
// Driven by SceneDepthOccluder.cs, which sizes and places the box from the MRUK
// TABLE anchor. See also quest-depth-occlusion-recipe notes.

Shader "QuestOcclusion/DepthOnlyOccluder"
{
    // Debug visibility is a PROPERTY of this shader rather than a separate debug shader,
    // deliberately. A second shader fetched with Shader.Find would be stripped from the
    // Android build (nothing references it from an asset), so the debug view would silently
    // do nothing on device — which is the one place you actually need it. This shader is kept
    // in the build by the DepthOnlyOccluder.mat asset reference, so anything driven by its
    // own properties is guaranteed to survive.
    Properties
    {
        // 0 = write no colour channels (invisible occluder, the normal mode).
        // 15 = write RGBA (solid _DebugColor, for checking alignment against the real desk).
        [HideInInspector] _ColorMask ("Colour Mask", Float) = 0
        _DebugColor ("Debug Colour", Color) = (1, 0, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-1" }

        Pass
        {
            Name "DepthOnly"

            ColorMask [_ColorMask]   // 0 = invisible; 15 = visible for on-site debugging
            ZWrite On                // ...but always write depth, which is the whole point
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _DebugColor;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Discarded by ColorMask 0 in normal use; shows as solid _DebugColor when
                // SceneDepthOccluder sets _ColorMask to 15 for alignment checking.
                return _DebugColor;
            }
            ENDCG
        }
    }

    Fallback Off
}
