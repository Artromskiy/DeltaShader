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
    
    float angle = atan(p.y/ (abs(p.x)+ 0.001));
    
    float boundary = 0.48 + 0.1 * sin(angle * 7 + pushConstants.member_Time* 1.4);
    
    float shell = exp(-abs(radius - boundary)* 45);
    
    float fill = 1 - smoothstep(boundary - 0.02, boundary + 0.02, radius);
    
    {fragColor = vec4(0.08 + shell * 0.85, 0.12 + fill * 0.35, 0.2 + shell * 0.65, 1);
    return;
    }

}
