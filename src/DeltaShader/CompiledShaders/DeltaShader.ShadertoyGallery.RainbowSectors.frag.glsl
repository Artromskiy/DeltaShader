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
        float angle = atan(p.y / (abs(p.x) + 0.001)) + pushConstants.member_Time * 0.4;
        float radius = length(p);
        float sector = 0.5 + 0.5 * cos(angle * 8);
        float red = 0.5 + 0.5 * cos(angle + 0.0);
        float green = 0.5 + 0.5 * cos(angle + 2.094);
        float blue = 0.5 + 0.5 * cos(angle + 4.188);
        float fade = max(0, 1 - radius);
        {
            fragColor = vec4(fade * red * (0.35 + 0.65 * sector), fade * green, fade * blue * (1.1 - sector * 0.4), 1);
            return;
        }

}
