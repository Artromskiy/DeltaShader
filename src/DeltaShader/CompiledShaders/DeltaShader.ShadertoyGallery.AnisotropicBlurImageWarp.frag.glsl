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
        vec2 warp = vec2(0.18 * sin(p.y * 5 + pushConstants.member_Time), 0.16 * cos(p.x * 4 - pushConstants.member_Time * 0.7));
        vec2 q = p + warp;
        float blurred = 0;
        float totalWeight = 0;
        for (float sampleIndex = 0; sampleIndex < 5; sampleIndex += 1)
        {
            float offset = (sampleIndex - 2) * 0.08;
            vec2 samplePoint = vec2(q.x + offset * (0.7 + 0.3 * sin(pushConstants.member_Time)), q.y + offset * 0.18);
            float signal = 0.5 + 0.5 * sin(samplePoint.x * 13 + cos(samplePoint.y * 8));
            float weight = 1 - abs(sampleIndex - 2) * 0.28;
            blurred += signal * weight;
            totalWeight += weight;
        }
    
        float value = blurred / totalWeight;
        float edge = exp(-abs(value - 0.5) * 13);
        {
            fragColor = vec4(0.04 + value * 0.18, 0.07 + value * 0.35 + edge * 0.08, 0.16 + value * 0.58, 1);
            return;
        }

}
