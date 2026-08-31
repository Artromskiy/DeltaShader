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
        float separator = 0.12 * sin(p.x * 3.4 + pushConstants.member_Time * 0.45);
        float heaven = 1 - smoothstep(separator - 0.02, separator + 0.02, p.y);
        float hell = 1 - heaven;
        float embers = 0.5 + 0.5 * sin(p.x * 16 - p.y * 11 - pushConstants.member_Time * 1.4);
        float seam = exp(-abs(p.y - separator) * 50);
        vec4 color = vec4(0.08 * heaven + (0.35 + embers * 0.25) * hell + seam * 0.85, 0.18 * heaven + (0.035 + embers * 0.08) * hell + seam * 0.35, 0.36 * heaven + (0.015 + embers * 0.02) * hell + seam * 0.05, 1);
        color = color * (0.75 + 0.25 * (1 - smoothstep(0.65, 1.25, length(p))));
        {
            fragColor = color;
            return;
        }

}
