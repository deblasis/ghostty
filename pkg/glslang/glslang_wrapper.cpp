// glslang_wrapper.cpp
// A thin wrapper that isolates all glslang C++ operations inside a single DLL
// built entirely with MSVC, avoiding C++ ABI issues when loaded from .NET.
//
// Export: glslang_compile_glsl_to_spirv(source, source_len, out_buf, out_buf_cap, out_len)
// Returns: 0 on success, non-zero on failure.

#include <cstdlib>
#include <cstring>

// Pull in glslang C interface headers
#include "glslang/Include/glslang_c_interface.h"
#include "glslang/Public/resource_limits_c.h"

static int g_initialized = 0;

extern "C" {

__declspec(dllexport)
int glslang_wrapper_init(void) {
    if (!g_initialized) {
        if (!glslang_initialize_process())
            return -1;
        g_initialized = 1;
    }
    return 0;
}

__declspec(dllexport)
int glslang_wrapper_compile(
    const char* source,       // null-terminated GLSL source
    int source_len,           // source length (not including null)
    unsigned char* out_buf,   // output buffer for SPIR-V
    int out_buf_cap,          // capacity of output buffer in bytes
    int* out_len              // [out] actual SPIR-V size in bytes
) {
    if (!g_initialized) return -1;
    if (!source || !out_buf || !out_len) return -2;

    *out_len = 0;

    const glslang_resource_t* resource = glslang_default_resource();
    if (!resource) return -3;

    glslang_input_t input = {};
    input.language = GLSLANG_SOURCE_GLSL;
    input.stage = GLSLANG_STAGE_FRAGMENT;
    input.client = GLSLANG_CLIENT_VULKAN;
    input.client_version = GLSLANG_TARGET_VULKAN_1_2;
    input.target_language = GLSLANG_TARGET_SPV;
    input.target_language_version = GLSLANG_TARGET_SPV_1_5;
    input.code = source;
    input.default_version = 100;
    input.default_profile = GLSLANG_NO_PROFILE;
    input.force_default_version_and_profile = 0;
    input.forward_compatible = 0;
    input.messages = GLSLANG_MSG_DEFAULT_BIT;
    input.resource = resource;

    glslang_shader_t* shader = glslang_shader_create(&input);
    if (!shader) return -4;

    int result = -5;

    if (!glslang_shader_preprocess(shader, &input)) {
        glslang_shader_delete(shader);
        return -5;
    }

    if (!glslang_shader_parse(shader, &input)) {
        glslang_shader_delete(shader);
        return -6;
    }

    glslang_program_t* program = glslang_program_create();
    if (!program) {
        glslang_shader_delete(shader);
        return -7;
    }

    glslang_program_add_shader(program, shader);

    if (!glslang_program_link(program,
        GLSLANG_MSG_SPV_RULES_BIT | GLSLANG_MSG_VULKAN_RULES_BIT)) {
        glslang_program_delete(program);
        glslang_shader_delete(shader);
        return -8;
    }

    glslang_program_SPIRV_generate(program, GLSLANG_STAGE_FRAGMENT);

    size_t spirv_size = glslang_program_SPIRV_get_size(program);
    size_t byte_size = spirv_size * sizeof(unsigned int);

    if ((int)byte_size > out_buf_cap) {
        glslang_program_delete(program);
        glslang_shader_delete(shader);
        return -9;
    }

    const unsigned int* spirv = glslang_program_SPIRV_get_ptr(program);
    memcpy(out_buf, spirv, byte_size);
    *out_len = (int)byte_size;

    result = 0;

    glslang_program_delete(program);
    glslang_shader_delete(shader);
    return result;
}

} // extern "C"
