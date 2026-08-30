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
    
    vec2 q = p;
    
    float glow = 0;
    
            for (float fold = 0;
     fold < 5;
     fold += 1)
            {
                q = vec2(abs(q.x), abs(q.y))- vec2(0.34, 0.28);
    
    float radiusSquared = dot(q, q);
    
    float inversion = 0.42 / (radiusSquared + 0.08);
    
                q = q * inversion + vec2(0.08 * sin(pushConstants.member_Time), -0.06);
    
                glow += exp(-abs(length(q)- 0.34)* (18 + fold * 4));
    
            }
    float vignette = 1 - smoothstep(0.72, 1.35, length(p));
    
    {fragColor = vec4(0.12 + glow * 0.07, 0.03 + glow * 0.18, 0.2 + glow * 0.55, 1)* vignette;
    return;
    }

}
