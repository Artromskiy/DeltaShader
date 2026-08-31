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
        float trunk = exp(-abs(p.x + 0.08 * sin(p.y * 8 + pushConstants.member_Time)) * 55) * (1 - smoothstep(0.55, 0.9, abs(p.y)));
        float branches = 0;
        for (float branch = 0; branch < 5; branch += 1)
        {
            float level = -0.45 + branch * 0.2;
            float span = 0.2 + (branch + 1) * 0.09;
            float sway = 0.08 * sin(pushConstants.member_Time * 1.4 + branch);
            float line = abs((p.x - sway) * cos(branch * 0.5) + (p.y - level) * sin(branch * 0.5));
            float extent = 1 - smoothstep(span, span + 0.08, abs(p.x));
            branches += exp(-line * 85) * extent;
        }
    
        float crown = exp(-dot(p - vec2(0, 0.5), p - vec2(0, 0.5)) * 8);
        {
            fragColor = vec4(0.015 + branches * 0.08, 0.04 + trunk * 0.23 + branches * 0.18, 0.025 + trunk * 0.08 + branches * 0.05 + crown * 0.16, 1);
            return;
        }

}
