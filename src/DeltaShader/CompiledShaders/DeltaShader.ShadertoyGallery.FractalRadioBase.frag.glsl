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
        float waves = 0;
        for (float band = 0; band < 5; band += 1)
        {
            float frequency = 8 + band * 5;
            float wave = 0.5 + 0.5 * cos(radius * frequency - pushConstants.member_Time * (1.1 + band * 0.14) + angle * (band + 1));
            waves += wave * (0.55 / (band + 1));
        }
    
        float halo = exp(-radius * radius * 2.4);
        {
            fragColor = vec4(0.03 + waves * 0.24 + halo * 0.12, 0.07 + waves * 0.35, 0.18 + waves * 0.5 + halo * 0.35, 1);
            return;
        }

}
