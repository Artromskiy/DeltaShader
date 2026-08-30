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
    
    float glow = 0;
    
    vec3 colorMix = vec3(0.02, 0.05, 0.12);
    
            for (float ball = 0;
     ball < 4;
     ball += 1)
            {
    float phase = pushConstants.member_Time* (1.2 + ball * 0.17) + ball * 1.57;
    
    vec2 center = vec2(-0.62 + ball * 0.4 + 0.08 * sin(phase), -0.2 + 0.34 * abs(sin(phase * 0.83)));
    
    float distance = length(p - center);
    
    float light = exp(-distance * distance * 70);
    
                glow += light;
    
                colorMix += vec3(0.14 + ball * 0.03, 0.07 + ball * 0.05, 0.2 - ball * 0.025)* light;
    
            }
    {fragColor = vec4(colorMix.x+ glow * 0.02, colorMix.y+ glow * 0.04, colorMix.z+ glow * 0.08, 1);
    return;
    }

}
