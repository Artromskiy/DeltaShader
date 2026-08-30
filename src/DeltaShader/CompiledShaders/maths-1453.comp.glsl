#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) uint member_Count;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input0
{
    vec4 data[];
} Input0_instance;

layout(set = 0, binding = 1, std430) buffer Output
{
    mat4 data_0[];
} Output_instance;

mat4 delta_createFromQuaternion(vec4 rotation) { float xx = rotation.x * rotation.x; float yy = rotation.y * rotation.y; float zz = rotation.z * rotation.z; float xy = rotation.x * rotation.y; float xz = rotation.x * rotation.z; float yz = rotation.y * rotation.z; float wx = rotation.w * rotation.x; float wy = rotation.w * rotation.y; float wz = rotation.w * rotation.z; return mat4(vec4(1.0 - 2.0 * (yy + zz), 2.0 * (xy + wz), 2.0 * (xz - wy), 0.0), vec4(2.0 * (xy - wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz + wx), 0.0), vec4(2.0 * (xz + wy), 2.0 * (yz - wx), 1.0 - 2.0 * (xx + yy), 0.0), vec4(0.0, 0.0, 0.0, 1.0)); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    mat4 local_result = delta_createFromQuaternion(Input0_instance.data[local_index]);
    Output_instance.data_0[local_index] = local_result;

}
