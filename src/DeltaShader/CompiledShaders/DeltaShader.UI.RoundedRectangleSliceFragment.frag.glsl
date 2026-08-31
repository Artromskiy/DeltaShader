#version 460
layout(location = 0) in vec4 interstage_slot_0;
layout(location = 1) in vec4 interstage_slot_1;
layout(location = 2) in vec4 interstage_slot_2;
layout(location = 3) in vec4 interstage_slot_3;
layout(location = 4) in vec4 interstage_slot_4;
layout(location = 0) out vec4 fragColor;


void main()
{
    vec4 cornerData = interstage_slot_4;
        vec4 segmentRect = interstage_slot_3;
        float borderWidth = interstage_slot_0.z;
        vec2 pixel = interstage_slot_0.xy;
        float isCorner = cornerData.w;
        float distance = 0;
        if (isCorner > 0.5)
        {
            distance = length(pixel - vec2(cornerData.x, cornerData.y)) - cornerData.z;
        }
        else
        {
            float left = segmentRect.x - pixel.x;
            float right = pixel.x - (segmentRect.x + segmentRect.z);
            float top = segmentRect.y - pixel.y;
            float bottom = pixel.y - (segmentRect.y + segmentRect.w);
            distance = max(max(left, right), max(top, bottom));
        }
    
        float edge = fwidth(distance);
        float fillCoverage = 1 - smoothstep(-edge, edge, distance);
        float innerDistance = 0;
        if (isCorner > 0.5)
        {
            float innerRadius = max(cornerData.z - borderWidth, 0);
            innerDistance = length(pixel - vec2(cornerData.x, cornerData.y)) - innerRadius;
        }
        else
        {
            float left = segmentRect.x + borderWidth - pixel.x;
            float right = pixel.x - (segmentRect.x + segmentRect.z - borderWidth);
            float top = segmentRect.y + borderWidth - pixel.y;
            float bottom = pixel.y - (segmentRect.y + segmentRect.w - borderWidth);
            innerDistance = max(max(left, right), max(top, bottom));
        }
    
        float innerCoverage = 1 - smoothstep(-edge, edge, innerDistance);
        float borderCoverage = max(fillCoverage - innerCoverage, 0);
        {
            fragColor = interstage_slot_1 * innerCoverage + interstage_slot_2 * borderCoverage;
            return;
        }

}
