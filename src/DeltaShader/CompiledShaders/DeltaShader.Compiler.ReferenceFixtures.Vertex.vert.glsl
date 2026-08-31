#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) mat4 member_Model;
    layout(offset = 64) mat4 member_View;
    layout(offset = 128) mat4 member_Projection;
} pushConstants;



void main()
{
    vec3 vertex = vec3(1, 2, 3);
        {
            gl_Position = pushConstants.member_Projection * pushConstants.member_View * pushConstants.member_Model * vec4(vertex, 1);
            return;
        }

}
