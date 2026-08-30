#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) uint member_Count;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input0
{
    float data[];
} Input0_instance;

layout(set = 0, binding = 1, std430) readonly buffer Input1
{
    float data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) readonly buffer Input2
{
    float data_1[];
} Input2_instance;

layout(set = 0, binding = 3, std430) buffer Output
{
    vec4 data_2[];
} Output_instance;

vec4 delta_quaternionFromYawPitchRoll(float yaw, float pitch, float roll) { float halfYaw = yaw * 0.5; float halfPitch = pitch * 0.5; float halfRoll = roll * 0.5; float sinYaw = sin(halfYaw); float cosYaw = cos(halfYaw); float sinPitch = sin(halfPitch); float cosPitch = cos(halfPitch); float sinRoll = sin(halfRoll); float cosRoll = cos(halfRoll); return vec4(-(cosYaw * sinPitch * cosRoll + sinYaw * cosPitch * sinRoll), -(sinYaw * cosPitch * cosRoll - cosYaw * sinPitch * sinRoll), -(cosYaw * cosPitch * sinRoll - sinYaw * sinPitch * cosRoll), cosYaw * cosPitch * cosRoll + sinYaw * sinPitch * sinRoll); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    vec4 local_result = delta_quaternionFromYawPitchRoll(Input0_instance.data[local_index], Input1_instance.data_0[local_index], Input2_instance.data_1[local_index]);
    Output_instance.data_2[local_index] = local_result;

}
