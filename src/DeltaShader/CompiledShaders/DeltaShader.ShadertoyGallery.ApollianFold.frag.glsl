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
    
    vec2 q = p * 1.6;
    
    float scale = 1;
    
            for (float fold = 0;
     fold < 5;
     fold += 1)
            {
                q = abs(q)- vec2(0.52, 0.48);
    
    float radiusSquared = max(dot(q, q), 0.08);
    
                q = q / radiusSquared - vec2(0.24, 0.18);
    
                scale = scale * 1.35;
    
            }
    float distance = length(q)/ scale;
    
    float glow = exp(-distance * 80);
    
    float colorPhase = 0.5 + 0.5 * sin(distance * 90 - pushConstants.member_Time);
    
    {fragColor = vec4(glow * (0.5 + colorPhase), glow * 0.35, glow * (1 - colorPhase) + 0.04, 1);
    return;
    }

}
