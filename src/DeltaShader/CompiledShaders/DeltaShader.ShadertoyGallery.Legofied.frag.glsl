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
        vec2 grid = uv * 9 + vec2(pushConstants.member_Time * 0.08, -pushConstants.member_Time * 0.05);
        vec2 cell = vec2(grid.x - floor(grid.x) - 0.5, grid.y - floor(grid.y) - 0.5);
        float seam = 1 - smoothstep(0.38, 0.49, max(abs(cell.x), abs(cell.y)));
        vec2 tile = vec2(floor(grid.x), floor(grid.y));
        float phase = sin(dot(tile, vec2(12.7, 28.3)));
        float red = 0.5 + 0.5 * sin(phase * 5 + 0.8);
        float green = 0.5 + 0.5 * sin(phase * 7 + 2.2);
        float blue = 0.5 + 0.5 * sin(phase * 9 + 4.1);
        float bevel = 0.7 + 0.3 * (1 - length(cell) * 1.5);
        {
            fragColor = vec4(red, green, blue, 1) * seam * bevel + vec4(0.015, 0.02, 0.035, 1) * (1 - seam);
            return;
        }

}
