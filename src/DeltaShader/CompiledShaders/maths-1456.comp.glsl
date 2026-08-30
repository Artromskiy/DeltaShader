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
    vec3 data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) readonly buffer Input2
{
    vec3 data_1[];
} Input2_instance;

layout(set = 0, binding = 3, std430) buffer Output
{
    mat4 data_2[];
} Output_instance;

mat4 delta_createLookTo(vec3 eyePosition, vec3 direction, vec3 up) { vec3 zAxis = normalize(direction); vec3 xAxis = normalize(cross(up, zAxis)); vec3 yAxis = cross(zAxis, xAxis); return mat4(vec4(xAxis.x, xAxis.y, xAxis.z, 0.0), vec4(yAxis.x, yAxis.y, yAxis.z, 0.0), vec4(zAxis.x, zAxis.y, zAxis.z, 0.0), vec4(-dot(xAxis, eyePosition), -dot(yAxis, eyePosition), -dot(zAxis, eyePosition), 1.0)); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    mat4 local_result = delta_createLookTo(Input0_instance.data[local_index], Input1_instance.data_0[local_index], Input2_instance.data_1[local_index]);
    Output_instance.data_2[local_index] = local_result;

}
