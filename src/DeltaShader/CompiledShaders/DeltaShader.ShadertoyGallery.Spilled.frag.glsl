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
    
    float drops = 0;
    
    float trails = 0;
    
            for (float drop = 0;
     drop < 5;
     drop += 1)
            {
    float phase = drop * 1.37;
    
    vec2 center = vec2(-0.58 + drop * 0.28 + 0.06 * sin(pushConstants.member_Time* 0.4 + phase), 0.2 * cos(phase * 2)- 0.12);
    
    vec2 delta = p - center;
    
                drops += exp(-dot(delta, delta)* (80 - drop * 5));
    
    float trailDistance = abs(delta.y+ 0.28 * sin(delta.x* 8 + phase));
    
                trails += exp(-trailDistance * 70)* (1 - smoothstep(0.12, 0.8, abs(delta.x)));
    
            }
    float sheen = 0.5 + 0.5 * sin(p.x* 15 + p.y* 4);
    
    {fragColor = vec4(0.025 + drops * 0.32 + trails * 0.08, 0.04 + drops * 0.15 + trails * 0.18, 0.08 + drops * 0.05 + trails * 0.42 + sheen * 0.03, 1);
    return;
    }

}
