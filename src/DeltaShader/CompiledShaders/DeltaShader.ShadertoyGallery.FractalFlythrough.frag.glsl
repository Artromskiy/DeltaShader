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
    
    float radius = length(p);
    
    float angle = atan(p.y/ (abs(p.x)+ 0.001));
    
    float tunnel = 0;
    
            for (float layer = 0;
     layer < 6;
     layer += 1)
            {
    float depth = radius * (13 + layer * 5) - pushConstants.member_Time* (1.2 + layer * 0.12);
    
    float spoke = sin(angle * (5 + layer) + depth * 0.35);
    
                tunnel += exp(-abs(sin(depth + spoke))* 18)/ (layer + 1);
    
            }
    float center = exp(-radius * radius * 28);
    
    {fragColor = vec4(0.02 + tunnel * 0.13, 0.04 + tunnel * 0.2 + center * 0.18, 0.13 + tunnel * 0.52 + center * 0.45, 1);
    return;
    }

}
