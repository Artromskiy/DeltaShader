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
    
            p.x= p.x* pushConstants.member_Resolution.x/ pushConstants.member_Resolution.y;
    
    float stem = exp(-abs(p.x- 0.18 * sin(p.y* 4 + pushConstants.member_Time* 0.5))* 48)* (1 - smoothstep(0.7, 1.1, abs(p.y)));
    
    float leaves = 0;
    
            for (float leaf = 0;
     leaf < 5;
     leaf += 1)
            {
    float y = -0.64 + leaf * 0.3;
    
    float x = 0.18 * sin(y * 4 + pushConstants.member_Time* 0.5);
    
    vec2 leafPoint = vec2(x + 0.22 * sin(leaf * 2.1), y);
    
    float distance = length(p - leafPoint);
    
                leaves += exp(-distance * distance * 140);
    
            }
    float tendril = exp(-abs(p.y- 0.3 * sin(p.x* 8 + pushConstants.member_Time))* 65)* (1 - smoothstep(0.45, 0.95, abs(p.x)));
    
    {fragColor = vec4(0.015 + stem * 0.05 + leaves * 0.08, 0.05 + stem * 0.32 + leaves * 0.2, 0.025 + stem * 0.1 + tendril * 0.25 + leaves * 0.06, 1);
    return;
    }

}
