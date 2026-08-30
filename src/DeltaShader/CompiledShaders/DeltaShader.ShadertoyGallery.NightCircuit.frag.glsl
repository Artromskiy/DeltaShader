#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 uv = vec2(gl_FragCoord.x, gl_FragCoord.y)/ pushConstants.member_Resolution;
    
    vec2 p = uv * 2 - vec2(1, 1);
    
            p.x= p.x* pushConstants.member_Resolution.x/ pushConstants.member_Resolution.y;
    
    float horizontal = exp(-abs(sin(p.y* 9 + p.x* 2))* 28);
    
    float vertical = exp(-abs(sin(p.x* 11 - p.y* 1.5))* 28);
    
    float pulseA = 0.5 + 0.5 * sin(p.x* 17 - pushConstants.member_Time* 2.3);
    
    float pulseB = 0.5 + 0.5 * cos(p.y* 19 + pushConstants.member_Time* 1.7);
    
    float nodes = 0;
    
            for (float node = 0;
     node < 4;
     node += 1)
            {
    vec2 center = vec2(-0.58 + node * 0.38, 0.24 * sin(node * 2.7));
    
                nodes += exp(-dot(p - center, p - center)* 95);
    
            }
    float trace = clamp(horizontal * (0.25 + pulseA * 0.75) + vertical * (0.2 + pulseB * 0.8) + nodes, 0, 1.5);
    
    {fragColor = vec4(0.01 + trace * 0.05, 0.025 + trace * 0.18, 0.08 + trace * 0.45, 1);
    return;
    }

}
