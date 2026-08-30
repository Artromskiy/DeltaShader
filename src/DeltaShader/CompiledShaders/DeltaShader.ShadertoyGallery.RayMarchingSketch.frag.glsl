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
    
    float travel = 0;
    
    float glow = 0;
    
            for (float step = 0;
     step < 8;
     step += 1)
            {
    vec2 samplePoint = p - vec2(0, travel);
    
    float distance = length(samplePoint)- 0.28 - 0.05 * sin(samplePoint.x* 10 + pushConstants.member_Time);
    
                glow += exp(-abs(distance)* 24);
    
                travel += max(distance, 0.025)* 0.18;
    
            }
    {fragColor = vec4(glow * 0.65, glow * 0.25, 0.08 + glow * 0.9, 1);
    return;
    }

}
