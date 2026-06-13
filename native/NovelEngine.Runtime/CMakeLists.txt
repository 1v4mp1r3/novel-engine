cmake_minimum_required(VERSION 3.20)
project(NovelEngineRuntime LANGUAGES CXX)

add_library(NovelEngineRuntime SHARED
    src/novel_runtime.cpp
)

target_include_directories(NovelEngineRuntime
    PUBLIC include
)

target_compile_features(NovelEngineRuntime PUBLIC cxx_std_20)
target_compile_definitions(NovelEngineRuntime PRIVATE NOVEL_RUNTIME_EXPORTS)
