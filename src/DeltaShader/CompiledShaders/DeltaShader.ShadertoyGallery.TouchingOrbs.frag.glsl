#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 p = (vec2(gl_FragCoord.x, gl_FragCoord.y) / pushConstants.member_Resolution) * 2 - vec2(1, 1);
        float left = length(p - vec2(-0.28, 0)) - 0.3;
        float right = length(p - vec2(0.28, 0)) - 0.3;
        float field = min(left, right);
        float fill = 1 - smoothstep(-0.01, 0.01, field);
        float seam = exp(-abs(left - right) * 30) * fill;
        {
            fragColor = vec4(0.06 + fill * 0.25 + seam * 0.5, 0.1 + fill * 0.5, 0.18 + fill * 0.75, 1);
            return;
        }

}
