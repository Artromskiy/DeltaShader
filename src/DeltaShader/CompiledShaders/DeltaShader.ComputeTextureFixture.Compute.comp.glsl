#version 460
layout(local_size_x = 8, local_size_y = 1, local_size_z = 1) in;
layout(set = 0, binding = 2) uniform sampler2D Atlas;

layout(set = 0, binding = 1, std430) buffer Output
{
    vec4 data[];
} Output_instance;


void main()
{
    uint local_id = gl_GlobalInvocationID.x;
    if (local_id< Output_instance.data.length())
    {Output_instance.data[local_id] = texture(Atlas, vec2(0.5, 0.5));}

}
