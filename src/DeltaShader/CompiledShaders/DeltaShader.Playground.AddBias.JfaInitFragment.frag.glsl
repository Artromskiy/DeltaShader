#version 460
layout(set = 0, binding = 0) uniform sampler2D Silhouette;

layout(location = 0) in vec2 Uv;
layout(location = 0) out vec4 fragColor;


void main()
{
    vec2 uv = Uv;
    
    vec4 silhouette = texture(Silhouette, uv);
    
    float valid = silhouette.a> 0.001 ? 1 : 0;
    
    {fragColor = vec4(uv.x, uv.y, valid, 1);
    return;
    }

}
