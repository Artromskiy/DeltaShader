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
        vec2 p = uv * 6 - vec2(3, 3);
        p.x = abs(p.x);
        p.y = abs(p.y);
        vec2 tile = vec2(floor(p.x), floor(p.y));
        vec2 cell = vec2(p.x - tile.x - 0.5, p.y - tile.y - 0.5);
        float selector = sin(dot(tile, vec2(8.7, 13.1)));
        float corner = 0.5 * selector;
        vec2 arcCenter = vec2(corner, corner);
        float arc = abs(length(cell - arcCenter) - 0.5);
        float line = exp(-arc * 70);
        float center = exp(-dot(cell, cell) * 8);
        float pulse = 0.7 + 0.3 * sin(pushConstants.member_Time + tile.x * 0.7 + tile.y * 1.1);
        {
            fragColor = vec4(0.025 + line * 0.13 + center * 0.08, 0.04 + line * 0.27, 0.12 + line * 0.58 + center * 0.18, 1) * pulse;
            return;
        }

}
