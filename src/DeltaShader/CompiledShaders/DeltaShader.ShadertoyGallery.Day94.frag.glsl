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
    
    vec2 sunCenter = vec2(-0.22, 0.12 + 0.05 * sin(pushConstants.member_Time* 0.2));
    
    float sunDistance = length(p - sunCenter);
    
    float sun = 1 - smoothstep(0.17, 0.19, sunDistance);
    
    float corona = exp(-sunDistance * sunDistance * 12);
    
    vec2 planetCenter = vec2(0.2, -0.03);
    
    float planet = 1 - smoothstep(0.24, 0.27, length(p - planetCenter));
    
    float rays = 0.5 + 0.5 * sin((p.x- p.y) * 32 + pushConstants.member_Time* 0.8);
    
    float horizon = 1 - smoothstep(0.35, 0.95, abs(p.y+ 0.55));
    
    vec4 color = vec4(0.03 + corona * 0.42 + sun * 0.45, 0.05 + corona * 0.25 + sun * 0.4, 0.12 + corona * 0.08 + sun * 0.18, 1);
    
            color = color * (1 - planet * 0.92) + vec4(0.03, 0.06, 0.11, 1)* planet;
    
            color = color + vec4(horizon * rays * 0.05, horizon * rays * 0.035, horizon * rays * 0.02, 0);
    
    {fragColor = color;
    return;
    }

}
