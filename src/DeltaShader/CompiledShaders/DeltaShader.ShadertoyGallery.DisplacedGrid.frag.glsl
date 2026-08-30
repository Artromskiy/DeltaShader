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
    
    float displacedX = p.x+ 0.16 * sin(p.y* 9 + pushConstants.member_Time);
    
    float displacedY = p.y+ 0.16 * cos(p.x* 8 - pushConstants.member_Time* 0.8);
    
    float lineX = exp(-abs(sin(displacedX * 18))* 18);
    
    float lineY = exp(-abs(sin(displacedY * 18))* 18);
    
    float light = clamp(lineX + lineY, 0, 1);
    
    {fragColor = vec4(0.04 + light * 0.24, 0.08 + light * 0.5, 0.16 + light * 0.75, 1);
    return;
    }

}
