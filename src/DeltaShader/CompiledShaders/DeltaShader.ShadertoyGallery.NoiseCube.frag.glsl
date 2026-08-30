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
    
    vec2 q = p;
    
    float value = 0;
    
    float weight = 0.5;
    
            for (float octave = 0;
     octave < 4;
     octave += 1)
            {
                value += weight * (0.5 + 0.5 * sin(q.x* 3.1 + cos(q.y* 2.4 + pushConstants.member_Time)));
    
                q = q * 1.9 + vec2(0.17, -0.11);
    
                weight = weight * 0.5;
    
            }
    float cube = 1 - max(abs(p.x), abs(p.y));
    
    {fragColor = vec4(value * 0.45, value * cube * 0.75, value * 0.95, 1);
    return;
    }

}
