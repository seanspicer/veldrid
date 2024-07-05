#version 450

#extension GL_ARB_separate_shader_objects : enable
#extension GL_ARB_shading_language_420pack : enable
struct VertexInput
{
    vec3 Position;
    vec4 Color;
};

struct FragmentInput
{
    vec4 Position;
    vec4 Color;
};

layout(set = 0, binding = 0) uniform ModelViewProjection
{
    mat4 field_ModelViewProjection;
};

FragmentInput Calculate(VertexInput input_)
{
    FragmentInput output_;
    output_.Color = input_.Color;
    output_.Position = field_ModelViewProjection * vec4(input_.Position, 1.f);
    return output_;
}

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
layout(location = 0) out vec4 fsin_0;

void main()
{
    VertexInput input_;
    input_.Position = Position;
    input_.Color = Color;
    FragmentInput output_ = Calculate(input_);
    fsin_0 = output_.Color;
    gl_Position = output_.Position;
}
