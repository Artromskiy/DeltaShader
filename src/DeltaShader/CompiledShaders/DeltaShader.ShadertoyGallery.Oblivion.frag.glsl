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
        float rings = 0;
        for (float ring = 0; ring < 5; ring += 1)
        {
            float phase = radius * (20 + ring * 7) - angle * (3 + ring) - pushConstants.member_Time * (0.4 + ring * 0.13);
            rings += exp(-abs(sin(phase)) * 14) / (ring + 1);
        }
    
        float singularity = exp(-radius * radius * 80);
        float vignette = 1 - smoothstep(0.5, 1.3, radius);
        {
            fragColor = vec4(0.005 + rings * 0.03 + singularity * 0.02, 0.008 + rings * 0.06 + singularity * 0.18, 0.02 + rings * 0.12 + singularity * 0.58, 1) * vignette;
            return;
        }

}
