#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_TexelSize;
    layout(offset = 8) float member_OutlineWidth;
    layout(offset = 16) vec4 member_Color;
} pushConstants;

layout(set = 0, binding = 0) uniform sampler2D Seeds;

layout(set = 0, binding = 1) uniform sampler2D Silhouette;

layout(location = 0) in vec2 Uv;
layout(location = 0) out vec4 fragColor;

vec2 delta_helper_ClampUv(vec2 arg_uv) {
    return clamp(arg_uv, vec2(0, 0), vec2(1, 1));
}


void main()
{
    vec2 uv = Uv;
        vec4 silhouette = texture(Silhouette, delta_helper_ClampUv(uv));
        if (silhouette.a > 0.001 || pushConstants.member_OutlineWidth <= 0)
        {
            {
                fragColor = vec4(0, 0, 0, 0);
                return;
            }
        }
    
        vec4 seed = texture(Seeds, delta_helper_ClampUv(uv));
        if (seed.z <= 0.5)
        {
            {
                fragColor = vec4(0, 0, 0, 0);
                return;
            }
        }
    
        float texel = max(pushConstants.member_TexelSize.x, pushConstants.member_TexelSize.y);
        float distanceInPixels = distance(uv, seed.xy) / texel;
        float aa = fwidth(distanceInPixels);
        float coverage = 1 - smoothstep(pushConstants.member_OutlineWidth - aa, pushConstants.member_OutlineWidth + aa, distanceInPixels);
        {
            fragColor = pushConstants.member_Color * coverage;
            return;
        }

}
