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
    
    float radius = length(p);
    
    float bands = 0.5 + 0.5 * cos(18 * radius - pushConstants.member_Time* 2);
    
    float glow = 1 / (1 + 4 * radius * radius);
    
    float red = glow * (0.35 + 0.65 * bands);
    
    float green = glow * (0.12 + 0.48 * (1 - bands));
    
    float blue = glow * (0.08 + 0.55 * bands);
    
    {fragColor = vec4(red, green, blue, 1);
    return;
    }

}
