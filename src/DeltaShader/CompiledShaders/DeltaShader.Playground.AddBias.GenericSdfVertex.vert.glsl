#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Vertices
{
    vec4 data[];
} Vertices_instance;

layout(location = 0) out vec2 Uv;


void main()
{
    ;
    gl_Position= gl_Position;
    
    Uv= Uv;
    
    {gl_Position = gl_Position;
    Uv = Uv;
    return;
    }

}
