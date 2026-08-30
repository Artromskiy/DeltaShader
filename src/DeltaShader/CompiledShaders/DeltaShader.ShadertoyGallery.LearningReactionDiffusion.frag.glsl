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
    
    float activator = 0.5 + 0.5 * sin(p.x* 3 + pushConstants.member_Time* 0.25);
    
    float inhibitor = 0.5 + 0.5 * cos(p.y* 4 - pushConstants.member_Time* 0.18);
    
            for (float iteration = 0;
     iteration < 5;
     iteration += 1)
            {
    float neighborhood = 0.5 + 0.5 * sin((p.x+ activator * 0.3) * (5 + iteration) + (p.y- inhibitor * 0.2) * 3);
    
    float reaction = activator * activator * inhibitor;
    
                activator = clamp(activator + 0.18 * (neighborhood - reaction), 0, 1);
    
                inhibitor = clamp(inhibitor + 0.13 * (activator - inhibitor * 0.7), 0, 1);
    
            }
    {fragColor = vec4(0.025 + activator * 0.22, 0.04 + activator * 0.52, 0.1 + inhibitor * 0.54, 1);
    return;
    }

}
