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
        p.x = p.x * pushConstants.member_Resolution.x / pushConstants.member_Resolution.y;
        float radius = length(p);
        float angle = atan(p.y / (abs(p.x) + 0.001));
        float facets = 0;
        for (float facet = 0; facet < 5; facet += 1)
        {
            float phase = angle * (7 + facet) + radius * (10 + facet * 2) - pushConstants.member_Time * (0.5 + facet * 0.17);
            facets += (0.5 + 0.5 * cos(phase)) * exp(-radius * (1.4 + facet * 0.3)) / (facet + 1);
        }
    
        float jewel = exp(-abs(radius - 0.22) * 38);
        {
            fragColor = vec4(0.07 + facets * 0.22 + jewel * 0.18, 0.02 + facets * 0.08 + jewel * 0.35, 0.18 + facets * 0.48 + jewel * 0.56, 1);
            return;
        }

}
