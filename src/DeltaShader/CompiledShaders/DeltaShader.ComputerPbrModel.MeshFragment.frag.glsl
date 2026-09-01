#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) mat4 member_ModelViewProjection;
    layout(offset = 64) vec3 member_LightDirection;
    layout(offset = 80) vec3 member_CameraPosition;
    layout(offset = 92) float member_Time;
} pushConstants;

layout(set = 0, binding = 4) uniform sampler2D BaseColor;

layout(set = 0, binding = 5) uniform sampler2D Metallic;

layout(set = 0, binding = 6) uniform sampler2D Normal;

layout(set = 0, binding = 7) uniform sampler2D Roughness;

layout(set = 0, binding = 8) uniform sampler2D Occlusion;

layout(set = 0, binding = 9) uniform sampler2D Emissive;

layout(location = 0) in vec3 WorldNormal;
layout(location = 1) in vec2 Uv;
layout(location = 2) in vec4 Tangent;
layout(location = 3) in vec3 WorldPosition;
layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 uv = Uv;
        vec4 baseColor = texture(BaseColor, uv);
        vec4 normalSample = texture(Normal, uv);
        float metallic = texture(Metallic, uv).x;
        float roughness = max(texture(Roughness, uv).x, 0.04);
        float occlusion = texture(Occlusion, uv).x;
        vec3 emissive = texture(Emissive, uv).xyz;
        vec3 geometricNormal = normalize(WorldNormal);
        vec4 tangentValue = Tangent;
        vec3 tangent = normalize(tangentValue.xyz);
        vec3 bitangent = normalize(cross(geometricNormal, tangent) * tangentValue.w);
        vec3 tangentNormal = normalize(normalSample.xyz * 2.0 - 1.0);
        vec3 normal = normalize(tangent * tangentNormal.x + bitangent * tangentNormal.y + geometricNormal * tangentNormal.z);
        vec3 lightDirection = normalize(-pushConstants.member_LightDirection);
        vec3 viewDirection = normalize(pushConstants.member_CameraPosition - WorldPosition);
        vec3 halfDirection = normalize(lightDirection + viewDirection);
        float diffuse = max(dot(normal, lightDirection), 0.0);
        float specular = max(dot(normal, halfDirection), 0.0);
        specular = specular * specular;
        specular = specular * specular;
        specular *= max(0.1, 1.0 - roughness);
        vec3 dielectricF0 = vec3(0.04, 0.04, 0.04);
        vec3 f0 = dielectricF0 * (1.0 - metallic) + baseColor.xyz * metallic;
        vec3 ambient = baseColor.xyz * (0.03 + 0.07 * diffuse) * occlusion;
        vec3 direct = baseColor.xyz * diffuse * (1.0 - metallic) + f0 * specular;
        vec3 color = ambient + direct + emissive;
        {
            fragColor = vec4(color, baseColor.w);
            return;
        }

}
