#version 460
struct DeltaStruct_Delta_Shader_UI_ClipAwareRoundedRectangleSliceParameters
{
    vec4 member_FillColor;
    vec4 member_BorderColor;
    vec4 member_CornerRadii;
    vec4 member_SegmentRect;
    vec4 member_CornerData;
    float member_BorderWidth;
    vec4 member_ClipRect;
};

layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Instances
{
    DeltaStruct_Delta_Shader_UI_ClipAwareRoundedRectangleSliceParameters data[];
} Instances_instance;

layout(location = 0) out vec4 interstage_slot_0;
layout(location = 1) out vec4 interstage_slot_1;
layout(location = 2) out vec4 interstage_slot_2;
layout(location = 3) out vec4 interstage_slot_3;
layout(location = 4) out vec4 interstage_slot_4;
layout(location = 5) out vec4 interstage_slot_5;


void main()
{
    DeltaStruct_Delta_Shader_UI_ClipAwareRoundedRectangleSliceParameters instance = Instances_instance.data[gl_InstanceIndex];
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
    
        vec2 pixel = vec2(instance.member_SegmentRect.x + local.x * instance.member_SegmentRect.z, instance.member_SegmentRect.y + local.y * instance.member_SegmentRect.w);
        vec2 clip = vec2(pixel.x / pushConstants.member_Resolution.x * 2 - 1, pixel.y / pushConstants.member_Resolution.y * 2 - 1);
        {
            gl_Position = vec4(vec4(clip.x, clip.y, 0, 1));
            interstage_slot_0.xy = vec2(pixel);
            interstage_slot_1 = vec4(instance.member_FillColor);
            interstage_slot_2 = vec4(instance.member_BorderColor);
            interstage_slot_3 = vec4(instance.member_SegmentRect);
            interstage_slot_4 = vec4(instance.member_CornerData);
            interstage_slot_0.z = float (instance.member_BorderWidth);
            interstage_slot_5 = vec4(instance.member_ClipRect);
            return;
        }

}
