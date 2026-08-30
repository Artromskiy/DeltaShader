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
    
    float body = 1 - smoothstep(0.42, 0.5, radius);
    
    float rim = exp(-abs(radius - 0.43)* 65);
    
    vec2 highlightPoint = vec2(-0.15, 0.16 + 0.04 * sin(pushConstants.member_Time));
    
    float highlight = exp(-dot(p - highlightPoint, p - highlightPoint)* 150);
    
    {fragColor = vec4(0.04 + rim * 0.45 + highlight, 0.16 + body * 0.22 + rim * 0.5, 0.22 + body * 0.65 + highlight * 0.6, 1);
    return;
    }

}
