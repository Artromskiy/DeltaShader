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

layout(set = 0, binding = 1, std430) readonly buffer Input1
{
    float data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) buffer Output
{
    vec4 data_1[];
} Output_instance;

vec4 delta_quaternionFromAxisAngle(vec3 axis, float angle) { float axisLength = length(axis); vec3 normalizedAxis = axisLength <= 1e-10 ? vec3(0.0) : axis / axisLength; float sine = sin(angle * 0.5); return vec4(-normalizedAxis * sine, cos(angle * 0.5)); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    vec4 local_result = delta_quaternionFromAxisAngle(Input0_instance.data[local_index], Input1_instance.data_0[local_index]);
    Output_instance.data_1[local_index] = local_result;

}
