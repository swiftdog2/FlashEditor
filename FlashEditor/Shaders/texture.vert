#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in vec3 in_normal;
layout(location = 2) in vec2 in_uv;
layout(location = 3) in float in_alpha;
layout(location = 4) in vec3 in_colour;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec2 uTexOffset;
uniform vec3 uLightDir;
out vec2 vUv;
out float vAlpha;
out vec3 vColour;
void main() {
    vUv = in_uv + uTexOffset;
    vAlpha = in_alpha;

    // Dynamic directional lighting — the light direction follows the camera
    vec3 normal = normalize(mat3(uModel) * in_normal);
    float NdotL = max(dot(normal, uLightDir), 0.0);
    float lighting = 1.2 * (0.3 + 0.7 * NdotL);
    vColour = in_colour * lighting;

    gl_Position = uProj * uView * uModel * vec4(in_position, 1.0);
}
