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
        p.x = p.x * pushConstants.member_Resolution.x / pushConstants.member_Resolution.y;
        float radius = length(p);
        float angle = atan(p.y / (abs(p.x) + 0.001));
        float stars = 0;
        for (float star = 0; star < 6; star += 1)
        {
            float starRadius = 0.12 + star * 0.13;
            float armAngle = angle - starRadius * 4.5 - pushConstants.member_Time * 0.12 + star * 1.04;
            float radial = exp(-abs(radius - starRadius) * 50);
            stars += radial * exp(-abs(sin(armAngle * 4)) * 13);
        }
    
        float core = exp(-radius * radius * 18);
        float dust = 0.5 + 0.5 * sin(radius * 70 - angle * 8);
        {
            fragColor = vec4(0.015 + stars * 0.2 + core * 0.28, 0.025 + stars * 0.16 + core * 0.18, 0.07 + stars * 0.38 + core * 0.38 + dust * 0.025, 1);
            return;
        }

}
