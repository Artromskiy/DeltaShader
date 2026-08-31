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
        vec2 q = p;
        float lines = 0;
        for (float pass = 0; pass < 4; pass += 1)
        {
            q = vec2(q.x + 0.22 * sin(q.y * 4 + pushConstants.member_Time), q.y + 0.18 * cos(q.x * 5 - pushConstants.member_Time));
            float gridX = abs(sin(q.x * (8 + pass * 2)));
            float gridY = abs(sin(q.y * (10 + pass)));
            lines += exp(-(gridX + gridY) * (7 + pass * 1.5));
            q = q * 1.34 + vec2(0.11, -0.07);
        }
    
        float center = exp(-dot(p, p) * 3);
        {
            fragColor = vec4(0.06 + lines * 0.09, 0.02 + lines * 0.22 + center * 0.15, 0.11 + lines * 0.38 + center * 0.45, 1);
            return;
        }

}
