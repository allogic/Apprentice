#version 330

in vec3 inVertex;
in int inColor;

out vec4 color;

uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;

void main()
{	
	float a = floor(float(inColor) / 16777216.0);
	float r = mod(floor(float(inColor) / 65536.0), 256.0);
	float g = mod(floor(float(inColor) / 256.0), 256.0);
	float b = mod(float(inColor), 256.0);

	color = vec4(r, g, b, a) / 255.0;

	gl_Position = projectionMatrix * viewMatrix * vec4(inVertex, 1.0);
}
