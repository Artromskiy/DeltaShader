#version 460
struct DeltaStruct_Delta_Shader_UI_ClipAwareSolidRectangleParameters
{
    vec4 member_Rect;
    vec4 member_Color;
    vec4 member_ClipRect;
};

layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Instances
{
    DeltaStruct_Delta_Shader_UI_ClipAwareSolidRectangleParameters data[];
} Instances_instance;

layout(location = 0) out vec4 Color;
layout(location = 1) out vec4 ClipRect;


void main()
{
    DeltaStruct_Delta_Shader_UI_ClipAwareSolidRectangleParameters instance = Instances_instance.data[gl_InstanceIndex];
        uint vertexIndex = gl_VertexIndex;
        vec2 local = vec2(0, 0);
        if (vertexIndex == 1u || vertexIndex == 2u || vertexIndex == 4u)
        {
            local = vec2(1, local.y);
        }
    
        if (vertexIndex == 2u || vertexIndex == 4u || vertexIndex == 5u)
        {
            local = vec2(local.x, 1);
        }
    
        vec2 pixel = vec2(instance.member_Rect.x + local.x * instance.member_Rect.z, instance.member_Rect.y + local.y * instance.member_Rect.w);
        vec2 clip = vec2(pixel.x / pushConstants.member_Resolution.x * 2 - 1, pixel.y / pushConstants.member_Resolution.y * 2 - 1);
        {
            gl_Position = vec4(vec4(clip.x, clip.y, 0, 1));
            Color = vec4(instance.member_Color);
            ClipRect = vec4(instance.member_ClipRect);
            return;
        }

}
