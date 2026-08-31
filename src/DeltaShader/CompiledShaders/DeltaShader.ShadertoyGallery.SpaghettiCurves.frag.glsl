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
        vec3 glow = vec3(0, 0, 0);
        for (float strand = 0; strand < 3; strand += 1)
        {
            float phase = strand * 2.1 + pushConstants.member_Time;
            float curve = 0.38 * sin(p.x * (4 + strand) + phase) + 0.15 * cos(p.x * 9 - phase);
            float distance = abs(p.y - curve - (strand - 1) * 0.34);
            float intensity = exp(-distance * distance * 180);
            glow = glow + vec3(intensity * (0.3 + strand * 0.2), intensity * (1 - strand * 0.25), intensity * (0.25 + (2 - strand) * 0.25));
        }
    
        {
            fragColor = vec4(glow.x, glow.y, glow.z, 1);
            return;
        }

}
