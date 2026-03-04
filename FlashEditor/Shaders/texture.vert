#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 2) in float in_alpha;
layout(location = 3) in vec3 in_colour;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec2 uTexOffset;
out vec2 vUv;
out float vAlpha;
out vec3 vColour;
void main() {
    vUv = in_uv + uTexOffset;
    vAlpha = in_alpha;
    vColour = in_colour;
    gl_Position = uProj * uView * uModel * vec4(in_position, 1.0);
}
