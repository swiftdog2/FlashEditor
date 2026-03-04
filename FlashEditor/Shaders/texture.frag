#version 330 core
in vec2 vUv;
in float vAlpha;
in vec3 vColour;
uniform sampler2D uTexture;
out vec4 FragColor;
void main() {
    vec4 texel = texture(uTexture, vUv);
    FragColor = vec4(texel.rgb * vColour, texel.a * vAlpha);
}
