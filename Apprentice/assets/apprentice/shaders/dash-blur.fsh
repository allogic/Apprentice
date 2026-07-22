#version 330

uniform sampler2D blitTex;
uniform sampler2D accTex;

in vec2 texCoord;

out vec4 fragColor;

void main()
{
	vec4 color = vec4(0);
	vec4 prevColor = texture(accTex, texCoord);
	vec4 currColor = texture(blitTex, texCoord);

	color = prevColor * 0.95 + currColor * 0.05;

	fragColor = color;
}