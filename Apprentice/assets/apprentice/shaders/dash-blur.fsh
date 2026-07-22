#version 330

uniform sampler2D blitTex;
uniform sampler2D accTex;

uniform float blurIntensity;

in vec2 texCoord;

out vec4 fragColor;

void main()
{
	vec4 prevColor = texture(accTex, texCoord);
	vec4 currColor = texture(blitTex, texCoord);

	float t = clamp(blurIntensity, 0.0, 3.0);

	float historyWeight = mix(0.0, 0.95, t);
	float currentWeight = 1.0 - historyWeight;

	// vec4 color = prevColor * 0.95 + currColor * 0.05;
	vec4 color = prevColor * historyWeight + currColor * currentWeight;

	fragColor = color;
}