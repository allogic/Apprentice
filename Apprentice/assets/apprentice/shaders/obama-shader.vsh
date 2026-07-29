#version 330

uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;
uniform mat4 modelMatrix;

in vec3 vertex;
in vec2 uv;

out vec2 texCoord;

void main()
{
	texCoord = uv;

	gl_Position = projectionMatrix * viewMatrix * modelMatrix * vec4(vertex, 1.0);
}
