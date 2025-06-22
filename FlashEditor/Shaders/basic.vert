#version 330 core
layout(location = 0) in vec3 in_position;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
void main() {
    gl_Position = uProj * uView * uModel * vec4(in_position, 1.0);
}
