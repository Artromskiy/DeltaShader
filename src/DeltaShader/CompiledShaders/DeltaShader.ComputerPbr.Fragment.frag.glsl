#version 460
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) vec2 member_Resolution;
    layout(offset = 8) float member_Time;
} pushConstants;

layout(location = 0) in vec2 Uv;
layout(location = 0) out vec4 fragColor;

float delta_helper_RoundedBox(vec2 arg_point, vec2 arg_halfSize, float arg_radius) {
    vec2 q = abs(arg_point) - arg_halfSize + vec2(arg_radius, arg_radius);
    vec2 outside = max(q, vec2(0.0, 0.0));
    float inside = min(max(q.x, q.y), 0.0);
    return length(outside) + inside - arg_radius;
}

vec4 delta_helper_SampleComputer(vec2 arg_point) {
    vec4 result = vec4(delta_helper_RoundedBox(arg_point - vec2(0.0, 0.24), vec2(0.72, 0.43), 0.07), 0.0, 0.58, 0.0);
    float screen = delta_helper_RoundedBox(arg_point - vec2(0.0, 0.24), vec2(0.60, 0.31), 0.035);
    if (screen < 0.0)
    {
        result = vec4(screen, 1.0, 0.24, 0.08);
    }

    float stand = delta_helper_RoundedBox(arg_point - vec2(0.0, -0.34), vec2(0.11, 0.16), 0.025);
    if (stand < result.x)
    {
        result = vec4(stand, 2.0, 0.68, 0.0);
    }

    float baseDistance = delta_helper_RoundedBox(arg_point - vec2(0.0, -0.47), vec2(0.79, 0.11), 0.04);
    if (baseDistance < result.x)
    {
        result = vec4(baseDistance, 3.0, 0.72, 0.0);
    }

    float keyboard = delta_helper_RoundedBox(arg_point - vec2(0.0, -0.62), vec2(0.53, 0.065), 0.025);
    if (keyboard < result.x)
    {
        result = vec4(keyboard, 4.0, 0.42, 0.0);
    }

    float indicator = delta_helper_RoundedBox(arg_point - vec2(0.60, -0.47), vec2(0.025, 0.018), 0.012);
    if (indicator < 0.0)
    {
        result = vec4(indicator, 5.0, 0.18, 1.0);
    }

    return result;
}

float delta_helper_SceneDistance(vec2 arg_point) { return delta_helper_SampleComputer(arg_point).x; }

vec3 delta_helper_SurfaceNormal(vec2 arg_point) {
    float epsilon = 0.002;
    float dx = delta_helper_SceneDistance(arg_point + vec2(epsilon, 0.0)) - delta_helper_SceneDistance(arg_point - vec2(epsilon, 0.0));
    float dy = delta_helper_SceneDistance(arg_point + vec2(0.0, epsilon)) - delta_helper_SceneDistance(arg_point - vec2(0.0, epsilon));
    return normalize(vec3(dx, dy, 0.72));
}

vec3 delta_helper_SurfaceAlbedo(float arg_material, vec2 arg_point, float arg_time) {
    if (arg_material > 0.5 && arg_material < 1.5)
    {
        float scan = 0.5 + 0.5 * sin(arg_point.y * 28.0 + arg_time * 0.8);
        return vec3(0.04, 0.28 + scan * 0.08, 0.56 + scan * 0.16);
    }

    if (arg_material > 4.5)
    {
        return vec3(0.9, 0.32, 0.05);
    }

    if (arg_material > 3.5)
    {
        return vec3(0.12, 0.14, 0.18);
    }

    if (arg_material > 2.5)
    {
        return vec3(0.16, 0.19, 0.24);
    }

    return vec3(0.24, 0.28, 0.34);
}

vec3 delta_helper_AmbientLayer(vec3 arg_albedo, vec3 arg_normal) {
    float hemisphere = 0.5 + 0.5 * arg_normal.y;
    return arg_albedo * (0.045 + hemisphere * 0.12);
}

vec3 delta_helper_DirectLightLayer(vec3 arg_albedo, vec3 arg_normal, float arg_roughness) {
    vec3 light = normalize(vec3(-0.45, 0.65, 0.75));
    vec3 view = vec3(0.0, 0.0, 1.0);
    vec3 halfway = normalize(light + view);
    float diffuse = max(dot(arg_normal, light), 0.0);
    float specularPower = mix(8.0, 64.0, 1.0 - arg_roughness);
    float specular = pow(max(dot(arg_normal, halfway), 0.0), specularPower);
    return arg_albedo * diffuse * 0.9 + vec3(specular, specular, specular) * 0.32;
}

vec3 delta_helper_ClearCoatLayer(vec3 arg_normal, float arg_roughness, float arg_material) {
    vec3 view = vec3(0.0, 0.0, 1.0);
    float fresnel = pow(1.0 - max(dot(arg_normal, view), 0.0), 5.0);
    float coat = arg_material < 2.5 ? 0.16 : 0.05;
    float strength = coat * (0.35 + fresnel) * (1.0 - arg_roughness * 0.5);
    return vec3(strength, strength, strength);
}

vec3 delta_helper_EmissionLayer(float arg_material, float arg_emission, float arg_time) {
    float pulse = 0.8 + 0.2 * sin(arg_time * 1.4);
    if (arg_material > 0.5 && arg_material < 1.5)
    {
        return vec3(0.01, 0.04, 0.09) * pulse;
    }

    if (arg_material > 4.5)
    {
        return vec3(0.7, 0.08, 0.01) * arg_emission * pulse;
    }

    return vec3(0.0, 0.0, 0.0);
}

vec3 delta_helper_ComposePbrLayers(vec2 arg_point, vec4 arg_scene, float arg_time) {
    vec3 normal = delta_helper_SurfaceNormal(arg_point);
    vec3 albedo = delta_helper_SurfaceAlbedo(arg_scene.y, arg_point, arg_time);
    vec3 ambient = delta_helper_AmbientLayer(albedo, normal);
    vec3 direct = delta_helper_DirectLightLayer(albedo, normal, arg_scene.z);
    vec3 clearCoat = delta_helper_ClearCoatLayer(normal, arg_scene.z, arg_scene.y);
    vec3 emission = delta_helper_EmissionLayer(arg_scene.y, arg_scene.w, arg_time);
    return clamp(ambient + direct + clearCoat + emission, vec3(0.0, 0.0, 0.0), vec3(1.5, 1.5, 1.5));
}

vec3 delta_helper_BackgroundLayer(vec2 arg_point, float arg_time) {
    float horizon = 0.5 + 0.5 * arg_point.y;
    float shimmer = 0.5 + 0.5 * sin(arg_point.x * 2.0 + arg_time * 0.12);
    return vec3(0.008 + horizon * 0.012, 0.012 + horizon * 0.018, 0.025 + horizon * 0.045 + shimmer * 0.006);
}


void main()
{
    vec2 point = Uv * 2.0 - vec2(1.0, 1.0);
        point.x *= pushConstants.member_Resolution.x / pushConstants.member_Resolution.y;
        point.y += 0.03;
        vec4 scene = delta_helper_SampleComputer(point);
        vec3 color = delta_helper_ComposePbrLayers(point, scene, pushConstants.member_Time);
        float coverage = 1.0 - smoothstep(-0.006, 0.006, scene.x);
        vec3 background = delta_helper_BackgroundLayer(point, pushConstants.member_Time);
        {
            fragColor = vec4(background * (1.0 - coverage) + color * coverage, 1.0);
            return;
        }

}
