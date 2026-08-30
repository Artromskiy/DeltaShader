#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) uint member_Count;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input0
{
    vec3 data[];
} Input0_instance;

layout(set = 0, binding = 1, std430) buffer Output
{
    vec3 data_0[];
} Output_instance;


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    vec3 local_result = (-Input0_instance.data[local_index]);
    Output_instance.data_0[local_index] = local_result;

}
