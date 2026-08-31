#version 460
layout(location = 0) in vec2 Pixel;
layout(location = 1) in vec4 FillColor;
layout(location = 2) in vec4 BorderColor;
layout(location = 3) in vec4 SegmentRect;
layout(location = 4) in vec4 CornerData;
layout(location = 5) in float BorderWidth;
layout(location = 0) out vec4 fragColor;


void main()
{
    vec4 cornerData = CornerData;
    
    vec4 segmentRect = SegmentRect;
    
    float borderWidth = BorderWidth;
    
    vec2 pixel = Pixel;
    
    float isCorner = cornerData.w;
    
    float distance = 0;
    
            if (isCorner > 0.5)
            {
                distance = length(pixel - vec2(cornerData.x, cornerData.y))- cornerData.z;
    
            }
            else
            {
    float left = segmentRect.x- pixel.x;
    
    float right = pixel.x- (segmentRect.x+ segmentRect.z);
    
    float top = segmentRect.y- pixel.y;
    
    float bottom = pixel.y- (segmentRect.y+ segmentRect.w);
    
                distance = max(max(left, right), max(top, bottom));
    
            }
    float edge = fwidth(distance);
    
    float fillCoverage = 1 - smoothstep(-edge, edge, distance);
    
    float innerDistance = 0;
    
            if (isCorner > 0.5)
            {
    float innerRadius = max(cornerData.z- borderWidth, 0);
    
                innerDistance = length(pixel - vec2(cornerData.x, cornerData.y))- innerRadius;
    
            }
            else
            {
    float left = segmentRect.x+ borderWidth - pixel.x;
    
    float right = pixel.x- (segmentRect.x+ segmentRect.z- borderWidth);
    
    float top = segmentRect.y+ borderWidth - pixel.y;
    
    float bottom = pixel.y- (segmentRect.y+ segmentRect.w- borderWidth);
    
                innerDistance = max(max(left, right), max(top, bottom));
    
            }
    float innerCoverage = 1 - smoothstep(-edge, edge, innerDistance);
    
    float borderCoverage = max(fillCoverage - innerCoverage, 0);
    
    {fragColor = FillColor* innerCoverage +
    BorderColor* borderCoverage;
    return;
    }

}
