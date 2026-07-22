#version 330

#define SAMPLES 12

uniform sampler2D tex;
uniform vec3 cameraDirection;
uniform vec3 worldVelocity;
uniform float blurAmount;

in vec2 texCoord;

out vec4 fragColor;

void main()
{
	vec3 up = vec3(0, 1, 0);
	vec3 normal = cameraDirection;
	vec3 tangent = normalize(cross(up, normal));
	vec3 bitangent = normalize(cross(normal, tangent));
	mat3 TBN = mat3(tangent, bitangent, normal);

	vec3 localVelocity = TBN * worldVelocity;
	vec2 blurDirection = normalize(localVelocity.xy);

	vec4 color = vec4(0.0);

	for (int i = 0; i < SAMPLES; i++)
	{
		float t = float(i) / float(SAMPLES - 1) - 0.5;

		vec2 offset = blurDirection * t * blurAmount;

		// TODO: remove me..
		color += texture(tex, texCoord + offset);

		// Radial smear
		// float weight = 1.0 - abs(t) * 2.0;
		// color += texture(tex, texCoord + offset) * weight;
	}

	// color /= float(SAMPLES) * 0.5;
	color /= float(SAMPLES);

	// fragColor = color;
	// fragColor = ReconstructWorldPosition(texCoord);
	// fragColor = vec4(blurDirection, 1);
	fragColor = vec4(cameraDirection * 0.5 + 0.5, 1.0);
}