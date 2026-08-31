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
        float radius = length(p);
        float angle = atan(p.y / (abs(p.x) + 0.001));
        float ribbon = 0.5 + 0.5 * sin(angle * 5 + radius * 11 - pushConstants.member_Time * 1.7);
        float haze = exp(-radius * radius * 1.8);
        float dust = 0.5 + 0.5 * cos(radius * 42 - angle * 3 + pushConstants.member_Time);
        {
            fragColor = vec4(haze * (0.15 + 0.75 * ribbon), haze * (0.1 + 0.45 * dust), haze * (0.3 + 0.65 * (1 - ribbon)), 1);
            return;
        }

}
