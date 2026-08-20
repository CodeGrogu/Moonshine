add_library(Moonshine.Native SHARED
    src/export/moonshine_native_api.cpp
    src/fec/reed_solomon_simd.cpp
    src/jitter_buffer/jitter_buffer.cpp
    src/video/d3d11_video_decoder.cpp
    src/video/dxgi_swapchain.cpp
    src/audio/wasapi_renderer.cpp
)

target_include_directories(Moonshine.Native
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

set_target_properties(Moonshine.Native PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
    OUTPUT_NAME "Moonshine.Native"
)

if(WIN32)
    target_link_libraries(Moonshine.Native PRIVATE
        d3d11
        d3d12
        dxgi
        d3dcompiler
        ole32
        mmdevapi
        avrt
    )
endif()
