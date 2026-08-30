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
    
    vec2 center = vec2(0.28 * cos(pushConstants.member_Time), 0.18 * sin(pushConstants.member_Time* 1.3));
    
    vec2 q = p - center;
    
    float radius = length(q);
    
    float ripple = 0.5 + 0.5 * cos(radius * 30 - pushConstants.member_Time* 4);
    
    float glow = 1 / (1 + radius * radius * 18);
    
    {fragColor = vec4(glow * (0.2 + 0.8 * ripple), glow * 0.4, glow * (1 - ripple), 1);
    return;
    }

}
