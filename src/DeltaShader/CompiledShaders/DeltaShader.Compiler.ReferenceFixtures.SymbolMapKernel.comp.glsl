#version 460
layout(local_size_x = 4, local_size_y = 2, local_size_z = 1) in;
layout(set = 0, binding = 0, std430) readonly buffer Input
{
    vec3 data[];
} Input_instance;

layout(set = 0, binding = 1, std430) buffer Output
{
    vec2 data_0[];
} Output_instance;


void main()
{
    uint local_invocationIndex = gl_GlobalInvocationID.x;
    vec3 local_a = Input_instance.data[local_invocationIndex];
    vec3 local_b = vec3(1, 2, 3);
    vec3 local_c = local_a+ local_b;
    vec2 local_xy = local_c.xy;
    Output_instance.data_0[local_invocationIndex] = vec2(local_xy.x, local_xy.y);
    dot(local_a, local_b);
    normalize(local_c);

}
