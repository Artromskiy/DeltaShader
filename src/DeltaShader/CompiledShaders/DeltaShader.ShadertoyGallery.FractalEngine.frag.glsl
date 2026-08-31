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
        float ring = 0;
        for (float layer = 0; layer < 4; layer += 1)
        {
            float phase = angle * (6 + layer * 2) + radius * (18 - layer * 2) - pushConstants.member_Time * (0.8 + layer * 0.25);
            ring += exp(-abs(sin(phase)) * (10 + layer * 2)) * (0.9 - layer * 0.12);
        }
    
        float core = exp(-radius * radius * 45);
        {
            fragColor = vec4(0.02 + ring * 0.14, 0.12 + ring * 0.38 + core * 0.3, 0.22 + ring * 0.7, 1);
            return;
        }

}
