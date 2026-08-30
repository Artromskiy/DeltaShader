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

layout(set = 0, binding = 1, std430) readonly buffer Input1
{
    vec4 data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) readonly buffer Input2
{
    float data_1[];
} Input2_instance;

layout(set = 0, binding = 3, std430) buffer Output
{
    vec4 data_2[];
} Output_instance;

vec4 delta_quaternionLerp(vec4 start, vec4 endValue, float amount) { if (dot(start, endValue) < 0.0) { endValue = -endValue; } vec4 value = start + (endValue - start) * amount; float lengthSquared = dot(value, value); if (lengthSquared <= 1e-20) { return vec4(0.0, 0.0, 0.0, 1.0); } return value / sqrt(lengthSquared); }

vec4 delta_quaternionSlerp(vec4 start, vec4 endValue, float amount) { float dotValue = dot(start, endValue); if (dotValue < 0.0) { endValue = -endValue; dotValue = -dotValue; } if (dotValue > 0.9995) { return delta_quaternionLerp(start, endValue, amount); } dotValue = clamp(dotValue, -1.0, 1.0); float angle = acos(dotValue); float scale = 1.0 / sin(angle); return start * (sin((1.0 - amount) * angle) * scale) + endValue * (sin(amount * angle) * scale); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    vec4 local_result = delta_quaternionSlerp(Input0_instance.data[local_index], Input1_instance.data_0[local_index], Input2_instance.data_1[local_index]);
    Output_instance.data_2[local_index] = local_result;

}
