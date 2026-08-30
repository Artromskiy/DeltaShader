#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 p = (vec2(gl_FragCoord.x, gl_FragCoord.y)/ pushConstants.member_Resolution) * 2 - vec2(1, 1);
    
    vec2 tile = p * 5;
    
    vec2 cell = vec2(tile.x- floor(tile.x)- 0.5, tile.y- floor(tile.y)- 0.5);
    
    float edge = max(abs(cell.x), abs(cell.y));
    
    float window = 1 - smoothstep(0.28, 0.48, edge);
    
    float pulse = 0.5 + 0.5 * sin(pushConstants.member_Time+ floor(tile.x)* 1.7 + floor(tile.y)* 0.9);
    
    {fragColor = vec4(0.03 + 0.45 * window * pulse, 0.06 + 0.3 * window, 0.14 + 0.65 * window * (1 - pulse), 1);
    return;
    }

}
