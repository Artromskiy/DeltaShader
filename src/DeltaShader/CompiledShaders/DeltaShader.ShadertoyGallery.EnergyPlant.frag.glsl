#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 p = (vec2(gl_FragCoord.x, gl_FragCoord.y) / pushConstants.member_Resolution) * 2 - vec2(1, 1);
        float radius = length(p);
        float core = exp(-radius * radius * 28);
        float branches = 0;
        for (float branch = 0; branch < 5; branch += 1)
        {
            float angle = branch * 1.257 + pushConstants.member_Time * 0.25;
            vec2 axis = vec2(cos(angle), sin(angle));
            float across = abs(p.x * axis.y - p.y * axis.x);
            float along = dot(p, axis);
            branches += exp(-across * 90) * exp(-abs(along - 0.36) * 9);
        }
    
        {
            fragColor = vec4(0.05 + core * 0.9, 0.12 + branches * 0.28, 0.1 + branches * 0.75 + core * 0.25, 1);
            return;
        }

}
