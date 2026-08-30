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
    
    float ridgeA = 0.28 + 0.12 * sin(p.x* 2.4 + 0.4)+ 0.06 * sin(p.x* 7);
    
    float ridgeB = 0.02 + 0.15 * sin(p.x* 3.8 - 0.8)+ 0.04 * cos(p.x* 11);
    
    float ridgeC = -0.18 + 0.09 * sin(p.x* 5.2 + 1.5);
    
    float upper = 1 - smoothstep(ridgeA - 0.012, ridgeA + 0.012, p.y);
    
    float middle = (1 - upper) * (1 - smoothstep(ridgeB - 0.012, ridgeB + 0.012, p.y));
    
    float lower = (1 - upper - middle) * (1 - smoothstep(ridgeC - 0.012, ridgeC + 0.012, p.y));
    
    float ripple = 0.5 + 0.5 * cos(p.x* 34 + pushConstants.member_Time* 0.7);
    
    {fragColor = vec4(0.03 + upper * 0.08 + middle * 0.08 + lower * 0.03, 0.08 + upper * 0.13 + middle * 0.12 + ripple * 0.04, 0.18 + (1 - upper) * 0.22 + ripple * 0.06, 1);
    return;
    }

}
