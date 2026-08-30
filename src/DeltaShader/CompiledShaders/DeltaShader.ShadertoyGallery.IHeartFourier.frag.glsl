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
    
            p.y+= 0.08;
    
    float radius = length(p);
    
    float angle = atan(p.y/ (abs(p.x)+ 0.001));
    
    float outline = 0.43 + 0.1 * cos(angle * 2)- 0.04 * cos(angle * 4)+ 0.025 * sin(angle * 7 + pushConstants.member_Time);
    
    float fill = 1 - smoothstep(outline - 0.018, outline + 0.018, radius);
    
    float rim = exp(-abs(radius - outline)* 55);
    
    float pulse = 0.75 + 0.25 * sin(pushConstants.member_Time* 3);
    
    {fragColor = vec4(0.35 + fill * 0.5 + rim * 0.2, 0.025 + fill * 0.07, 0.06 + fill * 0.12 + rim * 0.28, 1)* pulse;
    return;
    }

}
