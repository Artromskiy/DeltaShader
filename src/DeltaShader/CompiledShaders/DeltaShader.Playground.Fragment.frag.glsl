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

layout(location = 0) in vec2 Uv;
layout(location = 0) out vec4 fragColor;

vec3 delta_helper_RotateX(vec3 arg_value, float arg_angle) {
float sine = sin(arg_angle);

float cosine = cos(arg_angle);

        return vec3(arg_value.x, arg_value.y* cosine - arg_value.z* sine, arg_value.y* sine + arg_value.z* cosine);

    }

vec3 delta_helper_RotateY(vec3 arg_value, float arg_angle) {
float sine = sin(arg_angle);

float cosine = cos(arg_angle);

        return vec3(arg_value.x* cosine + arg_value.z* sine, arg_value.y,             -arg_value.x* sine + arg_value.z* cosine);

    }

float delta_helper_SceneSdf(vec3 arg_p, float arg_time) {
vec3 spacing = vec3(3.0, 3.0, 3.0);

vec3 q = fract((arg_p+ spacing * 0.5) / spacing)            * spacing - spacing * 0.5;

float wave = sin(arg_p.x* 0.5 + arg_time)            * cos(arg_p.y* 0.5 + arg_time)* 0.2;

float sphereRadius = 0.5 + wave;

        return length(q)- sphereRadius;

    }

vec3 delta_helper_CalculateNormal(vec3 arg_p, float arg_time) {
float epsilon = 0.001;

float distance = delta_helper_SceneSdf(arg_p, arg_time);

vec3 normal = vec3(delta_helper_SceneSdf(vec3(arg_p.x+ epsilon, arg_p.y, arg_p.z), arg_time)- distance, delta_helper_SceneSdf(vec3(arg_p.x, arg_p.y+ epsilon, arg_p.z), arg_time)- distance, delta_helper_SceneSdf(vec3(arg_p.x, arg_p.y, arg_p.z+ epsilon), arg_time)- distance);

        return normalize(normal);

    }


void main()
{
    vec2 uv = Uv* 2.0 - 1.0;
    
    float aspectRatio = pushConstants.member_Resolution.x/ pushConstants.member_Resolution.y;
    
            uv.x*= aspectRatio;
    
    float time = pushConstants.member_Time;
    
    vec3 ro = vec3(0.0, 0.0, time * 1.5);
    
    vec3 rd = normalize(vec3(uv.x, uv.y, 1.0));
    
    float angleX = sin(time * 0.3)* 0.2;
    
    float angleY = cos(time * 0.2)* 0.2;
    
            rd = delta_helper_RotateX(rd, angleX);
    
            rd = delta_helper_RotateY(rd, angleY);
    
    float t = 0.0;
    
    float maxDist = 40.0;
    
    float glow = 0.0;
    
    bool hit = false;
    
    
            for (int i = 0;
     i < 64;
     i++)
            {
    vec3 p = ro + rd * t;
    
    float distance = delta_helper_SceneSdf(p, time);
    
                glow += 0.015 / (0.015 + distance * distance);
    
    
                if (distance < 0.001)
                {
                    hit = true;
    
                    break;
    
                }
    
                if (t > maxDist)
                {
                    break;
    
                }
    
                t += distance;
    
            }
    vec3 finalColor = vec3(0.0, 0.0, 0.0);
    
    vec3 neonColor = vec3(sin(time * 0.5)* 0.5 + 0.5, sin(time * 0.7 + 2.0)* 0.5 + 0.5, cos(time * 0.3)* 0.5 + 0.5);
    
    
            if (hit)
            {
    vec3 p = ro + rd * t;
    
    vec3 normal = delta_helper_CalculateNormal(p, time);
    
    vec3 lightDirection = normalize(vec3(0.5, 1.0, -0.5));
    
    float diffuse = max(dot(normal, lightDirection), 0.0);
    
    float fog = exp(-0.08 * t);
    
                finalColor = (neonColor * diffuse + vec3(0.1, 0.1, 0.2)) * fog;
    
            }
    
            finalColor += neonColor * glow * 0.4;
    
    float vignette = Uv.x            * Uv.y            * (1.0 - Uv.x)
                * (1.0 - Uv.y);
    
            vignette = clamp(pow(vignette * 16.0, 0.25), 0.0, 1.0);
    
            finalColor *= vignette;
    
    {fragColor = vec4(finalColor.x, finalColor.y, finalColor.z, 1.0);
    return;
    }

}
