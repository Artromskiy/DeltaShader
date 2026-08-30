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
    
    float cycle = pushConstants.member_Time* 2.2;
    
    float stride = sin(cycle);
    
    float torso = exp(-abs(p.x+ 0.03 * stride)* 70)* (1 - smoothstep(0.35, 0.72, abs(p.y)));
    
    float head = exp(-dot(p - vec2(0.03 + 0.03 * stride, 0.48), p - vec2(0.03 + 0.03 * stride, 0.48))* 85);
    
    float legs = 0;
    
            for (float leg = 0;
     leg < 2;
     leg += 1)
            {
    float side = leg * 2 - 1;
    
    float swing = side * 0.23 * stride;
    
    float line = abs((p.x- swing) * (0.45 + side * 0.08) + (p.y+ 0.3) * (0.9 - side * 0.08));
    
    float reach = 1 - smoothstep(0.28, 0.62, length(p - vec2(swing, -0.34)));
    
                legs += exp(-line * 95)* reach;
    
            }
    float ground = exp(-abs(p.y+ 0.62)* 65)* (0.35 + 0.65 * (0.5 + 0.5 * sin(p.x* 9)));
    
    {fragColor = vec4(0.03 + legs * 0.2 + head * 0.14, 0.08 + torso * 0.5 + legs * 0.12, 0.15 + torso * 0.7 + ground * 0.18, 1);
    return;
    }

}
