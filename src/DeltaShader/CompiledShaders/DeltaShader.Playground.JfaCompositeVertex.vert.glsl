#version 460
layout(location = 0) out vec2 Uv;


void main()
{
    uint vertex = gl_VertexIndex;
    
            if (vertex == 0u)
            {
    {gl_Position = vec4(-1, -1, 0, 1);
    Uv = vec2(0, 0);
    return;
    }        }
    
            if (vertex == 1u)
            {
    {gl_Position = vec4(3, -1, 0, 1);
    Uv = vec2(2, 0);
    return;
    }        }
    {gl_Position = vec4(-1, 3, 0, 1);
    Uv = vec2(0, 2);
    return;
    }

}
