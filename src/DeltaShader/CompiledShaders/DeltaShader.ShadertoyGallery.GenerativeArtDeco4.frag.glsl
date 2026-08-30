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
    
            p.x= abs(p.x);
    
            p.y= abs(p.y);
    
    float diamond = abs(p.x)+ abs(p.y);
    
    float frame = exp(-abs(sin(diamond * 14 + pushConstants.member_Time* 0.18))* 22);
    
    float rays = exp(-abs(sin((p.x- p.y) * 18))* 16);
    
    float center = exp(-dot(p, p)* 18);
    
    float gold = frame * (0.5 + 0.5 * cos(p.x* 9 + p.y* 7));
    
    {fragColor = vec4(0.03 + gold * 0.5 + rays * 0.08, 0.06 + gold * 0.27 + rays * 0.16 + center * 0.12, 0.12 + rays * 0.42 + center * 0.5, 1);
    return;
    }

}
