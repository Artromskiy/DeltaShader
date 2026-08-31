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
        float value = 0;
        float weight = 0.5;
        for (float layer = 0; layer < 7; layer += 1)
        {
            float phase = dot(p, vec2(3.1 + layer * 1.7, 5.2 - layer * 0.63)) + pushConstants.member_Time * (0.18 + layer * 0.04);
            value += weight * (0.5 + 0.5 * sin(phase + cos(phase * 1.73)));
            p = p * 1.92 + vec2(0.17, -0.11);
            weight = weight * 0.53;
        }
    
        {
            fragColor = vec4(0.04 + value * 0.22, 0.06 + value * 0.38, 0.12 + value * 0.62, 1);
            return;
        }

}
