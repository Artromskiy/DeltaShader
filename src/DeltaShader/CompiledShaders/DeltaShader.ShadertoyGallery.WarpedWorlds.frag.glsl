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
        vec2 warp = vec2(sin(p.y * 4 + pushConstants.member_Time), cos(p.x * 3 - pushConstants.member_Time * 0.7));
        vec2 q = p + 0.22 * warp;
        float radius = length(q);
        float bands = 0.5 + 0.5 * sin(q.x * 12 + q.y * 7 + pushConstants.member_Time * 2);
        float glow = exp(-radius * radius * 2.2);
        {
            fragColor = vec4(glow * (0.1 + 0.85 * bands), glow * 0.3 + 0.2 * bands, glow * (0.8 - 0.45 * bands), 1);
            return;
        }

}
