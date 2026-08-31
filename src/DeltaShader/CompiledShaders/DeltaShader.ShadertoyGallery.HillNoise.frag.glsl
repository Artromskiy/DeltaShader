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
        float value = 0;
        float weight = 0.55;
        for (float layer = 0; layer < 4; layer += 1)
        {
            float frequency = 2.5 + layer * 2.1;
            value += weight * (0.5 + 0.5 * sin(p.x * frequency + p.y * 3 + pushConstants.member_Time * (0.4 + layer * 0.1)));
            weight = weight * 0.52;
        }
    
        float slope = 0.5 + 0.5 * sin(p.x * 5 - p.y * 2 + value * 4);
        {
            fragColor = vec4(0.05 + 0.35 * value, 0.12 + 0.7 * value * slope, 0.2 + 0.45 * (1 - value), 1);
            return;
        }

}
