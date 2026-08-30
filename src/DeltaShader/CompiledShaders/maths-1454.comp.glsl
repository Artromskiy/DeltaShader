#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) uint member_Count;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input0
{
    mat4 data[];
} Input0_instance;

layout(set = 0, binding = 1, std430) buffer Output
{
    vec4 data_0[];
} Output_instance;

vec4 delta_quaternionFromMatrix(mat4 matrix) { float trace = matrix[0][0] + matrix[1][1] + matrix[2][2]; if (trace > 0.0) { float s = sqrt(trace + 1.0) * 2.0; return vec4((matrix[1][2] - matrix[2][1]) / s, (matrix[2][0] - matrix[0][2]) / s, (matrix[0][1] - matrix[1][0]) / s, 0.25 * s); } if (matrix[0][0] > matrix[1][1] && matrix[0][0] > matrix[2][2]) { float s = sqrt(1.0 + matrix[0][0] - matrix[1][1] - matrix[2][2]) * 2.0; return vec4(0.25 * s, (matrix[0][1] + matrix[1][0]) / s, (matrix[0][2] + matrix[2][0]) / s, (matrix[1][2] - matrix[2][1]) / s); } if (matrix[1][1] > matrix[2][2]) { float s = sqrt(1.0 + matrix[1][1] - matrix[0][0] - matrix[2][2]) * 2.0; return vec4((matrix[0][1] + matrix[1][0]) / s, 0.25 * s, (matrix[1][2] + matrix[2][1]) / s, (matrix[2][0] - matrix[0][2]) / s); } float s = sqrt(1.0 + matrix[2][2] - matrix[0][0] - matrix[1][1]) * 2.0; return vec4((matrix[0][2] + matrix[2][0]) / s, (matrix[1][2] + matrix[2][1]) / s, 0.25 * s, (matrix[0][1] - matrix[1][0]) / s); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    vec4 local_result = delta_quaternionFromMatrix(Input0_instance.data[local_index]);
    Output_instance.data_0[local_index] = local_result;

}
