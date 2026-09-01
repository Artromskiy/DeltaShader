#version 460
layout(location = 0) in vec4 Color;
layout(location = 1) in vec4 ClipRect;
layout(location = 0) out vec4 fragColor;

bool delta_helper_IsInsideClip(vec4 arg_clip) {
    float pixelX = gl_FragCoord.x;
    float pixelY = gl_FragCoord.y;
    return pixelX >= arg_clip.x && pixelY >= arg_clip.y && pixelX < arg_clip.x + arg_clip.z && pixelY < arg_clip.y + arg_clip.w;
}


void main()
{
    if (!delta_helper_IsInsideClip(ClipRect))
        {
            discard;
        }
    
        {
            fragColor = Color;
            return;
        }

}
