#version 460
struct DeltaStruct_Delta_Shader_Text_GlyphInstance
{
    vec2 member_PixelMin;
    vec2 member_PixelMax;
    vec4 member_UvRect;
    vec4 member_Color;
};

layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 16) vec4 member_TextColor;
    layout(offset = 32) vec4 member_OutlineColor;
    layout(offset = 48) float member_OutlineWidth;
    layout(offset = 52) float member_DistanceRange;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Glyphs
{
    DeltaStruct_Delta_Shader_Text_GlyphInstance data[];
} Glyphs_instance;

layout(location = 0) out vec2 Uv;
layout(location = 1) out vec4 GlyphColor;


void main()
{
    uint instanceIndex = gl_InstanceIndex;
    
    uint vertexIndex = gl_VertexIndex;
    
    DeltaStruct_Delta_Shader_Text_GlyphInstance glyph = Glyphs_instance.data[instanceIndex];
    
    vec2 min = glyph.member_PixelMin;
    
    vec2 max = glyph.member_PixelMax;
    
    vec2 uvMin = vec2(glyph.member_UvRect.x, glyph.member_UvRect.y);
    
    vec2 uvMax = vec2(glyph.member_UvRect.z, glyph.member_UvRect.w);
    
    
            if (vertexIndex == 0u)
            {
    {gl_Position = vec4((min.x/ pushConstants.member_Resolution.x) * 2 - 1, (min.y/ pushConstants.member_Resolution.y) * 2 - 1, 0, 1);
    Uv = uvMin;
    GlyphColor = glyph.member_Color;
    return;
    }        }
            else if (vertexIndex == 1u)
            {
    {gl_Position = vec4((max.x/ pushConstants.member_Resolution.x) * 2 - 1, (min.y/ pushConstants.member_Resolution.y) * 2 - 1, 0, 1);
    Uv = vec2(uvMax.x, uvMin.y);
    GlyphColor = glyph.member_Color;
    return;
    }        }
            else if (vertexIndex == 2u)
            {
    {gl_Position = vec4((min.x/ pushConstants.member_Resolution.x) * 2 - 1, (max.y/ pushConstants.member_Resolution.y) * 2 - 1, 0, 1);
    Uv = vec2(uvMin.x, uvMax.y);
    GlyphColor = glyph.member_Color;
    return;
    }        }
            else if (vertexIndex == 3u)
            {
    {gl_Position = vec4((min.x/ pushConstants.member_Resolution.x) * 2 - 1, (max.y/ pushConstants.member_Resolution.y) * 2 - 1, 0, 1);
    Uv = vec2(uvMin.x, uvMax.y);
    GlyphColor = glyph.member_Color;
    return;
    }        }
            else if (vertexIndex == 4u)
            {
    {gl_Position = vec4((max.x/ pushConstants.member_Resolution.x) * 2 - 1, (min.y/ pushConstants.member_Resolution.y) * 2 - 1, 0, 1);
    Uv = vec2(uvMax.x, uvMin.y);
    GlyphColor = glyph.member_Color;
    return;
    }        }
    {gl_Position = vec4((max.x/ pushConstants.member_Resolution.x) * 2 - 1, (max.y/ pushConstants.member_Resolution.y) * 2 - 1, 0, 1);
    Uv = uvMax;
    GlyphColor = glyph.member_Color;
    return;
    }

}
