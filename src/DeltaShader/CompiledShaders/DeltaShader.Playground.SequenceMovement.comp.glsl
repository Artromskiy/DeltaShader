#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) float member_DeltaTime;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input
{
    uint data[];
} Input_instance;

layout(set = 0, binding = 1, std430) buffer Output
{
    uint data_0[];
} Output_instance;


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index< Input_instance.data.length())
    {Output_instance.data_0[local_index] = Output_instance.data_0[local_index]+ Input_instance.data[local_index];}

}
