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
