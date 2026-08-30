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
    vec4 data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) readonly buffer Input2
{
    vec3 data_1[];
} Input2_instance;

layout(set = 0, binding = 3, std430) buffer Output
{
    mat4 data_2[];
} Output_instance;

mat4 delta_createTranslation(vec3 translation) { return mat4(vec4(1.0, 0.0, 0.0, 0.0), vec4(0.0, 1.0, 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(translation.x, translation.y, translation.z, 1.0)); }

mat4 delta_createScale(float scale) { return mat4(vec4(scale, 0.0, 0.0, 0.0), vec4(0.0, scale, 0.0, 0.0), vec4(0.0, 0.0, scale, 0.0), vec4(0.0, 0.0, 0.0, 1.0)); }

mat4 delta_createScale(vec3 scale) { return mat4(vec4(scale.x, 0.0, 0.0, 0.0), vec4(0.0, scale.y, 0.0, 0.0), vec4(0.0, 0.0, scale.z, 0.0), vec4(0.0, 0.0, 0.0, 1.0)); }

mat4 delta_createFromQuaternion(vec4 rotation) { float xx = rotation.x * rotation.x; float yy = rotation.y * rotation.y; float zz = rotation.z * rotation.z; float xy = rotation.x * rotation.y; float xz = rotation.x * rotation.z; float yz = rotation.y * rotation.z; float wx = rotation.w * rotation.x; float wy = rotation.w * rotation.y; float wz = rotation.w * rotation.z; return mat4(vec4(1.0 - 2.0 * (yy + zz), 2.0 * (xy + wz), 2.0 * (xz - wy), 0.0), vec4(2.0 * (xy - wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz + wx), 0.0), vec4(2.0 * (xz + wy), 2.0 * (yz - wx), 1.0 - 2.0 * (xx + yy), 0.0), vec4(0.0, 0.0, 0.0, 1.0)); }

mat4 delta_createTRS(vec3 translation, vec4 rotation, vec3 scale) { return delta_createTranslation(translation) * delta_createFromQuaternion(rotation) * delta_createScale(scale); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    mat4 local_result = delta_createTRS(Input0_instance.data[local_index], Input1_instance.data_0[local_index], Input2_instance.data_1[local_index]);
    Output_instance.data_2[local_index] = local_result;

}
