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
        float height = 0;
        float weight = 0.5;
        for (float wave = 0; wave < 5; wave += 1)
        {
            vec2 direction = vec2(cos(wave * 1.7), sin(wave * 1.7));
            float phase = dot(p, direction) * (4 + wave * 2.3) + pushConstants.member_Time * (0.45 + wave * 0.12);
            height += weight * (0.5 + 0.5 * sin(phase));
            weight = weight * 0.55;
        }
    
        float horizon = 0.02 + (height - 0.4) * 0.35;
        float water = 1 - smoothstep(horizon - 0.02, horizon + 0.02, p.y);
        float foam = exp(-abs(p.y - horizon) * 55);
        {
            fragColor = vec4(0.03 + water * 0.02 + foam * 0.5, 0.08 + water * 0.25 + foam * 0.35, 0.18 + water * 0.45 + foam * 0.18, 1);
            return;
        }

}
