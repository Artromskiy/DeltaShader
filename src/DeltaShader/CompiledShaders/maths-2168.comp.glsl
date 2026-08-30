#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) uint member_Count;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input0
{
    uint data[];
} Input0_instance;

layout(set = 0, binding = 1, std430) readonly buffer Input1
{
    uint data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) buffer Out0
{
    uint data_1[];
} Out0_instance;

layout(set = 0, binding = 3, std430) buffer Out1
{
    uint data_2[];
} Out1_instance;


void main()
{
    uint local_out0;
    uint local_out1;
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    ;
    ;
    umulExtended(Input0_instance.data[local_index], Input1_instance.data_0[local_index], local_out0, local_out1);
    Out0_instance.data_1[local_index] = local_out0;
    Out1_instance.data_2[local_index] = local_out1;

}
