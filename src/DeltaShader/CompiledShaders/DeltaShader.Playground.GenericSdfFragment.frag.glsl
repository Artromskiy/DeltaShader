#version 460
struct DeltaStruct_Delta_Shader_Playground_SpaceInfRepeat
{
    vec3 member_Period;
};

struct DeltaStruct_Delta_Shader_Playground_SdfBox
{
    vec3 member_Size;
};

struct DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox_
{
    DeltaStruct_Delta_Shader_Playground_SpaceInfRepeat member_Modifier;
    DeltaStruct_Delta_Shader_Playground_SdfBox member_Shape;
};

struct DeltaStruct_Delta_Shader_Playground_SpaceTwist
{
    float member_Amount;
};

struct DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox__
{
    DeltaStruct_Delta_Shader_Playground_SpaceTwist member_Modifier;
    DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox_ member_Shape;
};

struct DeltaStruct_Delta_Shader_Playground_Raymarcher_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox___
{
    DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox__ member_Scene;
};

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

vec3 delta_helper_ModifySpace(DeltaStruct_Delta_Shader_Playground_SpaceTwist self, vec3 arg_p, float arg_time) {
    float c = cos(self.member_Amount * arg_p.y + arg_time);
    float s = sin(self.member_Amount * arg_p.y + arg_time);
    vec2 rotated = vec2(c * arg_p.x - s * arg_p.z, s * arg_p.x + c * arg_p.z);
    return vec3(rotated.x, arg_p.y, rotated.y);
}

vec3 delta_helper_ModifySpace_2(DeltaStruct_Delta_Shader_Playground_SpaceInfRepeat self, vec3 arg_p, float arg_time) {
    return fract((arg_p + self.member_Period * 0.5) / self.member_Period) * self.member_Period - self.member_Period * 0.5;
}

float delta_helper_Evaluate_3(DeltaStruct_Delta_Shader_Playground_SdfBox self, vec3 arg_p, float arg_time) {
    vec3 d = abs(arg_p) - self.member_Size;
    return length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
}

float delta_helper_Evaluate_2(DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox_ self, vec3 arg_p, float arg_time) {
    vec3 transformedSpace = delta_helper_ModifySpace_2(self.member_Modifier, arg_p, arg_time);
    return delta_helper_Evaluate_3(self.member_Shape, transformedSpace, arg_time);
}

float delta_helper_Evaluate(DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox__ self, vec3 arg_p, float arg_time) {
    vec3 transformedSpace = delta_helper_ModifySpace(self.member_Modifier, arg_p, arg_time);
    return delta_helper_Evaluate_2(self.member_Shape, transformedSpace, arg_time);
}

vec3 delta_helper_Render(DeltaStruct_Delta_Shader_Playground_Raymarcher_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox___ self, vec3 arg_ro, vec3 arg_rd, float arg_time) {
    float t = 0.0;
    float maxDist = 20.0;
    float glow = 0.0;
    for (int i = 0; i < 48; i++)
    {
        vec3 p = arg_ro + arg_rd * t;
        float d = delta_helper_Evaluate(self.member_Scene, p, arg_time);
        // Накапливаем glow-эффект для мистического неонового вида
        glow += 0.01 / (0.01 + d * d);
        if (d < 0.002)
        {
            break;
        }

        if (t > maxDist)
        {
            break;
        }

        t += d;
    }

    vec3 baseColor = vec3(0.1, 0.4, 0.8);
    vec3 glowColor = vec3(0.9, 0.2, 0.6);
    vec3 finalColor = mix(vec3(0.02, 0.02, 0.05), baseColor, exp(-0.1 * t));
    return finalColor + glowColor * glow * 0.25;
}


void main()
{
    vec2 uv = Uv * 2.0 - 1.0;
        float aspectRatio = pushConstants.member_Resolution.x / pushConstants.member_Resolution.y;
        uv.x *= aspectRatio;
        float time = pushConstants.member_Time;
        vec3 ro = vec3(0.0, 0.0, -4.0);
        vec3 rd = normalize(vec3(uv.x, uv.y, 1.5));
        DeltaStruct_Delta_Shader_Playground_SdfBox box = DeltaStruct_Delta_Shader_Playground_SdfBox(vec3(0.4, 0.4, 0.4));
        DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox_ repeatedBox = DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox_(DeltaStruct_Delta_Shader_Playground_SpaceInfRepeat(vec3(2.5, 2.5, 2.5)), box);
        DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox__ twistedScene = DeltaStruct_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox__(DeltaStruct_Delta_Shader_Playground_SpaceTwist(1.5 * sin(time * 0.5)), repeatedBox);
        DeltaStruct_Delta_Shader_Playground_Raymarcher_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox___ pipeline = DeltaStruct_Delta_Shader_Playground_Raymarcher_Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceTwist__Delta_Shader_Playground_ModifiedShape_Delta_Shader_Playground_SpaceInfRepeat__Delta_Shader_Playground_SdfBox___(twistedScene);
        vec3 sceneColor = delta_helper_Render(pipeline, ro, rd, time);
        {
            fragColor = vec4(sceneColor.x, sceneColor.y, sceneColor.z, 1.0);
            return;
        }

}
