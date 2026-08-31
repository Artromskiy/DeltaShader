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
        vec2 grid = uv * vec2(18, 10);
        vec2 cell = vec2(grid.x - floor(grid.x) - 0.5, grid.y - floor(grid.y) - 0.5);
        float column = floor(grid.x);
        float drift = pushConstants.member_Time * (0.7 + 0.08 * sin(column * 4.1));
        float trail = 0.5 + 0.5 * sin((grid.y - drift) * 5.2 + column * 1.8);
        float glyph = 1 - smoothstep(0.13, 0.24, abs(cell.x + 0.08 * sin(column)));
        float head = exp(-abs(grid.y - drift - trail * 0.2) * 3.5);
        float intensity = clamp(glyph * (0.15 + trail * 0.35 + head * 0.8), 0, 1);
        {
            fragColor = vec4(0.005 + intensity * 0.02, 0.04 + intensity * 0.64, 0.025 + intensity * 0.18, 1);
            return;
        }

}
