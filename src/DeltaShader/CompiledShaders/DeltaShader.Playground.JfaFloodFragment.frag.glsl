#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_TexelSize;
    layout(offset = 8) float member_Jump;
} pushConstants;

layout(set = 0, binding = 0) uniform sampler2D Seeds;

layout(location = 0) in vec2 Uv;
layout(location = 0) out vec4 fragColor;

vec2 delta_helper_ClampUv(vec2 arg_uv) {
    return clamp(arg_uv, vec2(0, 0), vec2(1, 1));
}

vec2 delta_helper_ChooseNearest(vec2 arg_pixel, vec2 arg_best, vec4 arg_candidate) {
    if (arg_candidate.z <= 0.5)
    {
        return arg_best;
    }

    if (arg_best.x < 0 || distance(arg_pixel, arg_candidate.xy) < distance(arg_pixel, arg_best))
    {
        return arg_candidate.xy;
    }

    return arg_best;
}


void main()
{
    vec2 uv = Uv;
        vec2 offset = pushConstants.member_TexelSize * pushConstants.member_Jump;
        vec4 center = texture(Seeds, delta_helper_ClampUv(uv));
        vec2 best = center.z > 0.5 ? center.xy : vec2(-1, -1);
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(-offset.x, -offset.y))));
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(0, -offset.y))));
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(offset.x, -offset.y))));
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(-offset.x, 0))));
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(offset.x, 0))));
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(-offset.x, offset.y))));
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(0, offset.y))));
        best = delta_helper_ChooseNearest(uv, best, texture(Seeds, delta_helper_ClampUv(uv + vec2(offset.x, offset.y))));
        float valid = best.x >= 0 ? 1 : 0;
        {
            fragColor = vec4(best.x, best.y, valid, 1);
            return;
        }

}
