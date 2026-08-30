#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) uint member_Count;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input0
{
    vec2 data[];
} Input0_instance;

layout(set = 0, binding = 1, std430) readonly buffer Input1
{
    float data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) buffer Output
{
    vec2 data_1[];
} Output_instance;


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    vec2 local_result = min(Input0_instance.data[local_index], Input1_instance.data_0[local_index]);
    Output_instance.data_1[local_index] = local_result;

}
