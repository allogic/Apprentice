#version 330

in vec3 vertex;
in vec2 uv;

out vec2 texCoord;

void main()
{
	texCoord = uv;

	gl_Position = vec4(vertex, 1.0);
}
