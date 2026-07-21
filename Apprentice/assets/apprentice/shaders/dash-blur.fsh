#version 330

uniform sampler2D tex;
uniform float strength;

in vec2 uv;
out vec4 fragColor;

void main()
{
	vec4 color = texture(tex, uv);

	color.r += strength;
	color.g *= 0.8;

	fragColor = color;
}
