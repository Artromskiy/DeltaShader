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
    
    float radius = length(p);
    
    vec2 normalProxy = vec2(p.x, p.y)/ (radius + 0.08);
    
    vec2 refracted = p + normalProxy * (0.12 + 0.06 * sin(pushConstants.member_Time));
    
    float layerA = 0.5 + 0.5 * sin(refracted.x* 11 + refracted.y* 4);
    
    float layerB = 0.5 + 0.5 * cos(refracted.y* 15 - refracted.x* 3);
    
    float interfaceGlow = exp(-abs(radius - 0.48)* 45);
    
    float bubble = 1 - smoothstep(0.45, 0.5, radius);
    
    {fragColor = vec4(0.025 + layerA * 0.11 + interfaceGlow * 0.18, 0.08 + layerB * 0.28 + interfaceGlow * 0.32, 0.18 + (layerA + layerB) * 0.24 + bubble * 0.18, 1);
    return;
    }

}
