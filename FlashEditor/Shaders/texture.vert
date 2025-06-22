#version 330 core
layout(location = 0) in vec3 in_position;
layout(location = 1) in vec2 in_uv;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec2 vUv;
void main() {
    vUv = in_uv;
    gl_Position = uProj * uView * uModel * vec4(in_position, 1.0);
}
