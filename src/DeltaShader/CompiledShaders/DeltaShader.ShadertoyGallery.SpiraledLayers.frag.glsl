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
    
    float angle = atan(p.y/ (abs(p.x)+ 0.001));
    
    float layers = 0;
    
            for (float layer = 0;
     layer < 5;
     layer += 1)
            {
    float spiral = angle * (3 + layer * 0.8) + (radius * 8 - (1 - radius) * 0.9) * (1 + layer * 0.12) - pushConstants.member_Time* (0.5 + layer * 0.1);
    
                layers += exp(-abs(sin(spiral))* 13)/ (layer + 1);
    
            }
    float center = exp(-radius * radius * 50);
    
    {fragColor = vec4(0.03 + layers * 0.14, 0.025 + layers * 0.22 + center * 0.18, 0.11 + layers * 0.52 + center * 0.48, 1);
    return;
    }

}
