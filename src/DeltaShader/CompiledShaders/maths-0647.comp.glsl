#version 460
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;
layout(push_constant, std430) uniform DeltaPushConstants
{
    layout(offset = 0) uint member_Count;
} pushConstants;

layout(set = 0, binding = 0, std430) readonly buffer Input0
{
    vec3 data[];
} Input0_instance;

layout(set = 0, binding = 1, std430) readonly buffer Input1
{
    vec3 data_0[];
} Input1_instance;

layout(set = 0, binding = 2, std430) readonly buffer Input2
{
    bvec3 data_1[];
} Input2_instance;

layout(set = 0, binding = 3, std430) buffer Output
{
    vec3 data_2[];
} Output_instance;

vec2 delta_select(vec2 falseValue, vec2 trueValue, bvec2 mask) { return vec2(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y); }

vec3 delta_select(vec3 falseValue, vec3 trueValue, bvec3 mask) { return vec3(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y, mask.z ? trueValue.z : falseValue.z); }

vec4 delta_select(vec4 falseValue, vec4 trueValue, bvec4 mask) { return vec4(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y, mask.z ? trueValue.z : falseValue.z, mask.w ? trueValue.w : falseValue.w); }

ivec2 delta_select(ivec2 falseValue, ivec2 trueValue, bvec2 mask) { return ivec2(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y); }

ivec3 delta_select(ivec3 falseValue, ivec3 trueValue, bvec3 mask) { return ivec3(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y, mask.z ? trueValue.z : falseValue.z); }

ivec4 delta_select(ivec4 falseValue, ivec4 trueValue, bvec4 mask) { return ivec4(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y, mask.z ? trueValue.z : falseValue.z, mask.w ? trueValue.w : falseValue.w); }

uvec2 delta_select(uvec2 falseValue, uvec2 trueValue, bvec2 mask) { return uvec2(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y); }

uvec3 delta_select(uvec3 falseValue, uvec3 trueValue, bvec3 mask) { return uvec3(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y, mask.z ? trueValue.z : falseValue.z); }

uvec4 delta_select(uvec4 falseValue, uvec4 trueValue, bvec4 mask) { return uvec4(mask.x ? trueValue.x : falseValue.x, mask.y ? trueValue.y : falseValue.y, mask.z ? trueValue.z : falseValue.z, mask.w ? trueValue.w : falseValue.w); }


void main()
{
    uint local_index = gl_GlobalInvocationID.x;
    if (local_index>= pushConstants.member_Count|| local_index>= Input0_instance.data.length())
    {return;}
    vec3 local_result = delta_select(Input0_instance.data[local_index], Input1_instance.data_0[local_index], Input2_instance.data_1[local_index]);
    Output_instance.data_2[local_index] = local_result;

}
