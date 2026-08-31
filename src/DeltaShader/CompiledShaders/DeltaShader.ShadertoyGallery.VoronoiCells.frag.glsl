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
        vec2 grid = p * 4;
        vec2 cell = vec2(grid.x - floor(grid.x), grid.y - floor(grid.y));
        float nearest = 1.5;
        float ring = 0;
        for (float x = -1; x <= 1; x += 1)
        {
            for (float y = -1; y <= 1; y += 1)
            {
                vec2 id = vec2(floor(grid.x) + x, floor(grid.y) + y);
                float seedValue = sin(dot(id, vec2(17.1, 41.7))) * 43758.5;
                float seed = seedValue - floor(seedValue);
                vec2 point = vec2(x + 0.5 + 0.35 * sin(seed * 6.28), y + 0.5 + 0.35 * cos(seed * 6.28));
                float distance = length(cell - point);
                nearest = min(nearest, distance);
                ring += exp(-distance * distance * 28);
            }
        }
    
        {
            fragColor = vec4(0.03 + 0.5 * ring, 0.1 + 0.65 * nearest, 0.35 + 0.5 * (1 - nearest), 1);
            return;
        }

}
