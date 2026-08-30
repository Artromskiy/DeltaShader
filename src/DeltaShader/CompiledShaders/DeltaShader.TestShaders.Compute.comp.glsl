#version 460
layout(local_size_x = 8, local_size_y = 1, local_size_z = 1) in;
struct DeltaStruct_Delta_Shader_TestShaders_VectorAdd_TransformBase
{
    vec3 member_Position;
};

struct DeltaStruct_Delta_Shader_TestShaders_VectorAdd_TransformRecord
{
    DeltaStruct_Delta_Shader_TestShaders_VectorAdd_TransformBase member_Base;
    vec4 member_Rotation;
    mat4 member_Transform;
};

layout(set = 0, binding = 0, std430) readonly buffer Input
{
    DeltaStruct_Delta_Shader_TestShaders_VectorAdd_TransformRecord data[];
} Input_instance;

layout(set = 0, binding = 1, std430) buffer Output
{
    DeltaStruct_Delta_Shader_TestShaders_VectorAdd_TransformRecord data_0[];
} Output_instance;


void main()
{
    uint local_invocation = gl_GlobalInvocationID.x;
    if (local_invocation< Input_instance.data.length())
    {Output_instance.data_0[local_invocation] = Input_instance.data[local_invocation];}

}
