#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) mat4 member_ModelViewProjection;
    layout(offset = 64) vec3 member_LightDirection;
    layout(offset = 80) vec3 member_CameraPosition;
    layout(offset = 92) float member_Time;
} pushConstants;

layout(location = 0) in vec4 vertex_Position;
layout(location = 1) in vec3 vertex_WorldNormal;
layout(location = 2) in vec2 vertex_Uv;
layout(location = 3) in vec4 vertex_Tangent;

layout(location = 0) out vec3 WorldNormal;
layout(location = 1) out vec2 Uv;
layout(location = 2) out vec4 Tangent;
layout(location = 3) out vec3 WorldPosition;


void main()
{
    vec4 position = vertex_Position;
        {
            gl_Position = pushConstants.member_ModelViewProjection * position;
            WorldNormal = normalize(vertex_WorldNormal);
            Uv = vertex_Uv;
            Tangent = vertex_Tangent;
            WorldPosition = position.xyz;
            return;
        }

}
