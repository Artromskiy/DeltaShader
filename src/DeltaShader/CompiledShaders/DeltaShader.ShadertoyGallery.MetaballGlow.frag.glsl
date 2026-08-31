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
        float energy = 0;
        for (float ball = 0; ball < 3; ball += 1)
        {
            float phase = pushConstants.member_Time * (0.7 + ball * 0.17) + ball * 2.094;
            vec2 center = vec2(0.48 * cos(phase), 0.38 * sin(phase * 1.3));
            vec2 delta = p - center;
            float distanceSquared = dot(delta, delta);
            energy += exp(-distanceSquared * 12);
        }
    
        float core = clamp(energy, 0, 1);
        {
            fragColor = vec4(core * (1 - 0.35 * core), core * 0.45, 0.08 + core * 0.8, 1);
            return;
        }

}
