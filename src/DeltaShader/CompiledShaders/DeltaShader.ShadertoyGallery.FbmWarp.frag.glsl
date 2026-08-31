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
        vec2 q = p + vec2(0.25 * sin(p.y * 3 + pushConstants.member_Time), 0.2 * cos(p.x * 4 - pushConstants.member_Time));
        float value = 0;
        float weight = 0.5;
        for (float octave = 0; octave < 4; octave += 1)
        {
            value += weight * (0.5 + 0.5 * sin(q.x * (3.2 + octave) + cos(q.y * 2.7 - pushConstants.member_Time)));
            q = q * 1.85 + vec2(0.13, -0.09);
            weight = weight * 0.5;
        }
    
        {
            fragColor = vec4(0.05 + value * 0.6, 0.08 + value * value * 0.5, 0.18 + (1 - value) * 0.7, 1);
            return;
        }

}
