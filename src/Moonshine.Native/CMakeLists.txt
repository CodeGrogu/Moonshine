add_library(Moonshine.Native SHARED
    src/export/moonshine_native_api.cpp
    src/fec/reed_solomon_simd.cpp
    src/jitter_buffer/jitter_buffer.cpp
    src/video/d3d11_video_decoder.cpp
    src/video/dxgi_swapchain.cpp
    src/audio/wasapi_renderer.cpp
    src/audio/wasapi_loopback_capture.cpp
    src/audio/wasapi_mic_capture.cpp
    src/audio/opus_audio_encoder.cpp
    src/audio/opus_audio_decoder.cpp
    src/audio/mic_audio_sink.cpp
    src/audio/virtual_audio_driver.cpp
    src/audio/virtual_audio_ipc.cpp
    src/capture/dxgi_desktop_duplicator.cpp
    src/capture/wgc_desktop_capture.cpp
    src/color/hdr_metadata_extractor.cpp
    src/color/d3d_color_converter.cpp
    src/encoder/nvenc/nvenc_api.cpp
    src/encoder/nvenc/nvenc_session.cpp
    src/encoder/nvenc/nvenc_surface_pool.cpp
    src/encoder/nvenc/nvenc_bitstream_pool.cpp
    src/encoder/nvenc_video_encoder.cpp
    src/encoder/amf_video_encoder.cpp
    src/encoder/qsv_video_encoder.cpp
    src/encoder/d3d11_hardware_encoder.cpp
    src/encoder/unified_video_encoder.cpp
    src/input/windows_input_injector.cpp
)

target_include_directories(Moonshine.Native
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<BUILD_INTERFACE:${CMAKE_SOURCE_DIR}/drivers/audio>
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
        opus
        d3d11
        d3d12
        dxgi
        d3dcompiler
        ole32
        mmdevapi
        avrt
        setupapi
    )
endif()
