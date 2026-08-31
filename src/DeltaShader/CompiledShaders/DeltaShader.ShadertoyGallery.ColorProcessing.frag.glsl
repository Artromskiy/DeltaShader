#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 uv = vec2(gl_FragCoord.x, gl_FragCoord.y) / pushConstants.member_Resolution;
        vec2 p = uv * 2 - vec2(1, 1);
        p.x = p.x * pushConstants.member_Resolution.x / pushConstants.member_Resolution.y;
        float red = 0.5 + 0.5 * sin(p.x * 4 + pushConstants.member_Time);
        float green = 0.5 + 0.5 * sin(p.y * 5 - pushConstants.member_Time * 0.7 + 2.1);
        float blue = 0.5 + 0.5 * sin((p.x + p.y) * 3 + pushConstants.member_Time * 0.4 + 4.2);
        vec3 source = vec3(red, green, blue);
        vec3 processed = vec3(source.x * 0.8 + source.y * 0.15, source.y * 0.72 + source.z * 0.2, source.z * 0.9 + source.x * 0.12);
        float contrast = 0.72 + 0.28 * cos(length(p) * 5 - pushConstants.member_Time);
        {
            fragColor = vec4(processed.x * contrast, processed.y * contrast, processed.z * contrast, 1);
            return;
        }

}
