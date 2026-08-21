include_directories(
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/include
    ${CMAKE_SOURCE_DIR}/drivers/audio
)

add_executable(test_fec_simd 
    test_fec_simd.cpp 
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/src/fec/reed_solomon_simd.cpp
)
target_link_libraries(test_fec_simd PRIVATE Moonshine.Native)
add_test(NAME test_fec_simd COMMAND test_fec_simd)

add_executable(test_spsc_ring_buffer 
    test_spsc_ring_buffer.cpp
)
target_link_libraries(test_spsc_ring_buffer PRIVATE Moonshine.Native)
add_test(NAME test_spsc_ring_buffer COMMAND test_spsc_ring_buffer)

add_executable(test_jitter_buffer 
    test_jitter_buffer.cpp 
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/src/jitter_buffer/jitter_buffer.cpp
)
target_link_libraries(test_jitter_buffer PRIVATE Moonshine.Native)
add_test(NAME test_jitter_buffer COMMAND test_jitter_buffer)

add_executable(test_c_abi_export 
    test_c_abi_export.cpp
)
target_link_libraries(test_c_abi_export PRIVATE Moonshine.Native)
add_test(NAME test_c_abi_export COMMAND test_c_abi_export)

add_executable(test_desktop_capture 
    test_desktop_capture.cpp
)
target_link_libraries(test_desktop_capture PRIVATE Moonshine.Native)
add_test(NAME test_desktop_capture COMMAND test_desktop_capture)

add_executable(test_wgc_capture 
    test_wgc_capture.cpp
)
target_link_libraries(test_wgc_capture PRIVATE Moonshine.Native)
add_test(NAME test_wgc_capture COMMAND test_wgc_capture)

add_executable(test_hdr_colorimetry 
    test_hdr_colorimetry.cpp
)
target_link_libraries(test_hdr_colorimetry PRIVATE Moonshine.Native)
add_test(NAME test_hdr_colorimetry COMMAND test_hdr_colorimetry)

add_executable(test_hardware_encoders 
    test_hardware_encoders.cpp
)
target_link_libraries(test_hardware_encoders PRIVATE Moonshine.Native)
add_test(NAME test_hardware_encoders COMMAND test_hardware_encoders)

add_executable(test_nvenc_pipeline 
    test_nvenc_pipeline.cpp
)
target_link_libraries(test_nvenc_pipeline PRIVATE Moonshine.Native)
add_test(NAME test_nvenc_pipeline COMMAND test_nvenc_pipeline)

add_executable(test_amf_pipeline 
    test_amf_pipeline.cpp
)
target_link_libraries(test_amf_pipeline PRIVATE Moonshine.Native)
add_test(NAME test_amf_pipeline COMMAND test_amf_pipeline)

add_executable(test_qsv_pipeline 
    test_qsv_pipeline.cpp
)
target_link_libraries(test_qsv_pipeline PRIVATE Moonshine.Native)
add_test(NAME test_qsv_pipeline COMMAND test_qsv_pipeline)

add_executable(test_wasapi_capture 
    test_wasapi_capture.cpp
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/src/audio/wasapi_loopback_capture.cpp
)
target_link_libraries(test_wasapi_capture PRIVATE Moonshine.Native)
add_test(NAME test_wasapi_capture COMMAND test_wasapi_capture)

add_executable(test_opus_encoder 
    test_opus_encoder.cpp
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/src/audio/opus_audio_encoder.cpp
)
target_link_libraries(test_opus_encoder PRIVATE Moonshine.Native)
add_test(NAME test_opus_encoder COMMAND test_opus_encoder)

add_executable(test_mic_sink 
    test_mic_sink.cpp
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/src/audio/mic_audio_sink.cpp
)
target_link_libraries(test_mic_sink PRIVATE Moonshine.Native)
add_test(NAME test_mic_sink COMMAND test_mic_sink)

add_executable(test_virtual_audio_driver
    test_virtual_audio_driver.cpp
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/src/audio/virtual_audio_driver.cpp
    ${CMAKE_SOURCE_DIR}/drivers/audio/minwave.cpp
    ${CMAKE_SOURCE_DIR}/drivers/audio/mintopo.cpp
    ${CMAKE_SOURCE_DIR}/drivers/audio/adapter.cpp
)
target_link_libraries(test_virtual_audio_driver PRIVATE Moonshine.Native $<$<BOOL:${WIN32}>:avrt>)
add_test(NAME test_virtual_audio_driver COMMAND test_virtual_audio_driver)

add_executable(test_virtual_audio_ipc
    test_virtual_audio_ipc.cpp
    ${CMAKE_SOURCE_DIR}/src/Moonshine.Native/src/audio/virtual_audio_ipc.cpp
)
target_link_libraries(test_virtual_audio_ipc PRIVATE Moonshine.Native $<$<BOOL:${WIN32}>:avrt>)
add_test(NAME test_virtual_audio_ipc COMMAND test_virtual_audio_ipc)

if(WIN32)
    add_custom_command(TARGET test_c_abi_export POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:Moonshine.Native>
            $<TARGET_FILE_DIR:test_c_abi_export>
    )
endif()
