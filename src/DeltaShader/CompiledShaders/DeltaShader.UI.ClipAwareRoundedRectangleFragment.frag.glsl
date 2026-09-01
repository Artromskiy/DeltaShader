#version 460
layout(location = 0) in vec4 interstage_slot_0;
layout(location = 1) in vec4 interstage_slot_1;
layout(location = 2) in vec4 interstage_slot_2;
layout(location = 3) in vec4 interstage_slot_3;
layout(location = 4) in vec4 interstage_slot_4;
layout(location = 5) in vec4 interstage_slot_5;
layout(location = 0) out vec4 fragColor;

bool delta_helper_IsInsideClip(vec4 arg_clip) {
    float pixelX = gl_FragCoord.x;
    float pixelY = gl_FragCoord.y;
    return pixelX >= arg_clip.x && pixelY >= arg_clip.y && pixelX < arg_clip.x + arg_clip.z && pixelY < arg_clip.y + arg_clip.w;
}


void main()
{
    if (!delta_helper_IsInsideClip(interstage_slot_5))
        {
            discard;
        }
    
        vec4 rect = interstage_slot_1;
        vec2 size = vec2(rect.z, rect.w);
        vec4 cornerRadii = interstage_slot_4;
        float borderWidth = interstage_slot_0.z;
        vec2 pixel = interstage_slot_0.xy * size;
        vec2 halfSize = size * 0.5;
        vec2 centered = pixel - halfSize;
        float radius = cornerRadii.x;
        if (centered.x > 0)
        {
            if (centered.y > 0)
            {
                radius = cornerRadii.z;
            }
            else
            {
                radius = cornerRadii.y;
            }
        }
        else if (centered.y > 0)
        {
            radius = cornerRadii.w;
        }
    
        vec2 q = abs(centered) - halfSize + vec2(radius, radius);
        vec2 outside = max(q, 0);
        float outsideDistance = length(outside);
        float insideDistance = min(max(q.x, q.y), 0);
        float distance = outsideDistance + insideDistance - radius;
        float edge = fwidth(distance);
        float fillCoverage = 1 - smoothstep(-edge, edge, distance);
        float innerCoverage = 1 - smoothstep(-edge, edge, distance + borderWidth);
        float borderCoverage = max(fillCoverage - innerCoverage, 0);
        {
            fragColor = interstage_slot_2 * innerCoverage + interstage_slot_3 * borderCoverage;
            return;
        }

}
