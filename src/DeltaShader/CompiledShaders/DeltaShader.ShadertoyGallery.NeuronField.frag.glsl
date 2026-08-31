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
        float pulse = 0;
        float connections = 0;
        for (float i = 0; i < 4; i += 1)
        {
            float phase = i * 1.73 + pushConstants.member_Time * (0.6 + i * 0.08);
            vec2 node = vec2(0.55 * cos(phase), 0.42 * sin(phase * 1.21));
            float distance = length(p - node);
            pulse += exp(-distance * distance * 95) * (0.5 + 0.5 * sin(phase * 3));
            connections += exp(-abs(p.x * sin(phase) - p.y * cos(phase)) * 30) * exp(-distance * 1.5);
        }
    
        {
            fragColor = vec4(0.02 + 0.8 * pulse, 0.06 + 0.25 * connections, 0.12 + 0.9 * connections, 1);
            return;
        }

}
