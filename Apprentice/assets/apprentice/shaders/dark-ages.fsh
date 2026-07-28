#version 330

uniform vec4 playerPosition;

uniform sampler2D colorTex;
uniform sampler2D depthTex;

uniform vec4 screenSize;

uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;

uniform float darkIntensity;
uniform float darkRadius;

uniform float nearZ;
uniform float farZ;
uniform float depthFactor;

in vec2 texCoord;

out vec4 fragColor;

float LinearizeDepth(float depth, float near, float far) {
	float z = depth * 2.0 - 1.0; // Back to NDC
	return (2.0 * near * far) / (far + near - z * (far - near));
}

vec3 ReconstructWorldPosition(vec2 uv, float depth)
{
	// NDC coordinates
	vec4 clip;
	clip.xy = uv * 2.0 - 1.0;
	clip.z = depth * 2.0 - 1.0;
	clip.w = 1.0;

	// View space
	vec4 view = inverse(projectionMatrix) * clip;
	view /= view.w;

	// World space
	vec4 world = inverse(viewMatrix) * view;

	return world.xyz;
}

void main()
{
	// TODO
	// vec3 delta = worldPosition - playerPosition;
	// float dist2 = dot(delta, delta);
	// float dist = length(worldPosition - playerPosition.xyz);
	// dist *= darkIntensity;
	// if (dist > darkRadius)
	// {
	// 	// discard; // TODO
	// }

	// fragColor = color;
	// fragColor = texture(tex, texCoord).rgba * darkIntensity;
	// fragColor = vec4(texCoord, 0, 1);
	// fragColor = vec4(worldPosition, 1);
	// fragColor = vec4(uv, 0, 1);
	// fragColor = texture(depthTex, texCoord);

	// float depth = texture(depthTex, texCoord).r;
	// float depthLinear = LinearizeDepth(depth, nearZ, farZ) / depthFactor;
	// fragColor = vec4(depthLinear, depthLinear, depthLinear, 1);

	float depth = texture(depthTex, texCoord).r;
	float depthLinear = LinearizeDepth(depth, nearZ, farZ) / depthFactor;

	if (depthLinear > darkRadius)
	{
		fragColor = vec4(depthLinear, depthLinear, depthLinear, 1);
	}
	else
	{
		// vec2 uv = gl_FragCoord.xy / screenSize.xy;
		// vec3 worldPosition = ReconstructWorldPosition(uv, depthLinear);
		// float dist = length(worldPosition - playerPosition.xyz);

		fragColor = texture(colorTex, texCoord);
	}
}