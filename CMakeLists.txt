cmake_minimum_required(VERSION 3.25)

if(NOT WIN32)
    message(FATAL_ERROR "Moonshine supports Windows 11 only. Configure this project with a Windows MSVC toolchain.")
endif()

project(Moonshine VERSION 1.0.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Unified Output Directory so executables and shared DLLs share the same folder
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

# Option switches
option(MOONSHINE_ENABLE_AVX2 "Enable AVX2 SIMD optimizations" ON)
option(MOONSHINE_ENABLE_AVX512 "Enable AVX-512 and GFNI SIMD optimizations" OFF)
option(MOONSHINE_BUILD_TESTS "Build native test suite" ON)
option(MOONSHINE_BUILD_BENCHMARKS "Build native micro-benchmarks" ON)

if(NOT MSVC)
    message(FATAL_ERROR "Moonshine requires Microsoft Visual C++ on Windows 11.")
endif()

# External Dependencies (Official libopus v1.5.2)
include(FetchContent)
set(OPUS_BUILD_PROGRAMS OFF CACHE BOOL "" FORCE)
set(OPUS_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(OPUS_ENABLE_FLOAT_API ON CACHE BOOL "" FORCE)
set(OPUS_USE_ALLOCA ON CACHE BOOL "" FORCE)
set(OPUS_NONTHREADSAFE_PSEUDOSTACK OFF CACHE BOOL "" FORCE)
set(OPUS_HARDENING OFF CACHE BOOL "" FORCE)
set(OPUS_ASSERTIONS OFF CACHE BOOL "" FORCE)
set(OPUS_INSTALL_PKG_CONFIG_MODULE OFF CACHE BOOL "" FORCE)
set(OPUS_INSTALL_CMAKE_CONFIG_MODULE OFF CACHE BOOL "" FORCE)

FetchContent_Declare(
    opus
    GIT_REPOSITORY https://github.com/xiph/opus.git
    GIT_TAG        v1.5.2
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(opus)
if(TARGET opus)
    target_compile_options(opus PRIVATE /WX- /w)
    target_compile_definitions(opus PUBLIC USE_ALLOCA=1)
endif()

# Global MSVC flags for the Windows 11 native engine.
add_compile_options(
    /W4
    /WX
    /wd4324
    /wd4702
    /permissive-
    /Zc:__cplusplus
    /utf-8
    /Oi
    /Ot
    /favor:blend
)
if(MOONSHINE_ENABLE_AVX2)
    add_compile_options(/arch:AVX2)
    add_compile_definitions(MOONSHINE_HAS_AVX2=1)
endif()
if(MOONSHINE_ENABLE_AVX512)
    add_compile_options(/arch:AVX512)
    add_compile_definitions(MOONSHINE_HAS_AVX512=1)
endif()

# Link-time Code Generation / Whole Program Optimisation for Release.
set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} /GL /O2 /Gy")
set(CMAKE_SHARED_LINKER_FLAGS_RELEASE "${CMAKE_SHARED_LINKER_FLAGS_RELEASE} /LTCG /OPT:REF /OPT:ICF")

# Include Subdirectories
add_subdirectory(src/Moonshine.Native)

if(MOONSHINE_BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests/Moonshine.Native.Tests)
endif()
