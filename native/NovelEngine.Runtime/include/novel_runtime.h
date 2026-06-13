#pragma once

#ifdef _WIN32
#  ifdef NOVEL_RUNTIME_EXPORTS
#    define NOVEL_API __declspec(dllexport)
#  else
#    define NOVEL_API __declspec(dllimport)
#  endif
#else
#  define NOVEL_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct novel_runtime novel_runtime;

NOVEL_API novel_runtime* novel_runtime_create(void);
NOVEL_API void novel_runtime_destroy(novel_runtime* runtime);
NOVEL_API int novel_runtime_execute(novel_runtime* runtime, const char* script);
NOVEL_API int novel_runtime_evaluate(
    novel_runtime* runtime,
    const char* condition,
    int* result);
NOVEL_API const char* novel_runtime_last_error(const novel_runtime* runtime);
NOVEL_API const char* novel_runtime_get_string(
    novel_runtime* runtime,
    const char* name);
NOVEL_API double novel_runtime_get_number(
    const novel_runtime* runtime,
    const char* name,
    double fallback);
NOVEL_API int novel_runtime_get_bool(
    const novel_runtime* runtime,
    const char* name,
    int fallback);

#ifdef __cplusplus
}
#endif
