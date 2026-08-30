#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 16) vec4 member_TextColor;
    layout(offset = 32) vec4 member_OutlineColor;
    layout(offset = 48) float member_OutlineWidth;
    layout(offset = 52) float member_DistanceRange;
} pushConstants;

layout(set = 0, binding = 4) uniform sampler2D Atlas;

layout(location = 0) in vec2 Uv;
layout(location = 1) in vec4 GlyphColor;
layout(location = 0) out vec4 fragColor;


void main()
{
    vec4 texel = texture(Atlas, Uv);
    
    float median = max(min(texel.x, texel.y), min(max(texel.x, texel.y), texel.z));
    
    float signedDistance = median - 0.5;
    
            signedDistance *= pushConstants.member_DistanceRange;
    
    float edge = fwidth(signedDistance);
    
    float fillCoverage = smoothstep(-edge, edge, signedDistance);
    
    float outlineWidth = max(pushConstants.member_OutlineWidth, 0);
    
    float outerCoverage = smoothstep(-outlineWidth - edge, -outlineWidth + edge, signedDistance);
    
    float outlineContribution = max(outerCoverage - fillCoverage, 0);
    
    {fragColor = pushConstants.member_TextColor* GlyphColor* fillCoverage +
    pushConstants.member_OutlineColor* GlyphColor* outlineContribution;
    return;
    }

}
