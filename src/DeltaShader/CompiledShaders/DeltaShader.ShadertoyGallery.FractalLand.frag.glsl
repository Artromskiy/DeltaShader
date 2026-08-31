#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 uv = vec2(gl_FragCoord.x, gl_FragCoord.y) / pushConstants.member_Resolution;
        vec2 p = uv * 2 - vec2(1, 1);
        p.x = p.x * pushConstants.member_Resolution.x / pushConstants.member_Resolution.y;
        float terrain = 0;
        float amplitude = 0.55;
        float frequency = 1.8;
        for (float octave = 0; octave < 4; octave += 1)
        {
            terrain += amplitude * sin(p.x * frequency + octave * 1.7 + pushConstants.member_Time * 0.12) * (0.5 + 0.5 * cos(p.x * frequency * 0.37));
            frequency = frequency * 1.9;
            amplitude = amplitude * 0.48;
        }
    
        float horizon = p.y - terrain * 0.18 + 0.1;
        float land = 1 - smoothstep(-0.025, 0.025, horizon);
        float snow = land * (0.5 + 0.5 * sin(p.x * 14 + terrain * 10));
        {
            fragColor = vec4(0.04 + land * 0.16 + snow * 0.14, 0.1 + land * 0.2 + snow * 0.22, 0.22 + (1 - land) * 0.42 + snow * 0.26, 1);
            return;
        }

}
