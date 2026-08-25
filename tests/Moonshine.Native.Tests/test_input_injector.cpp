#include <iostream>
#include <cstdlib>
#include "moonshine/input/windows_input_injector.h"
#include "moonshine/export/moonshine_native_api.h"

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

using namespace moonshine::input;

void test_native_injector_lifecycle() {
    std::cout << "[Test] Native Input Injector Lifecycle..." << std::endl;
    WindowsInputInjector injector;
    auto bounds = injector.get_virtual_desktop_bounds();
    TEST_ASSERT(bounds.cx_virtual_screen > 0);
    TEST_ASSERT(bounds.cy_virtual_screen > 0);
    std::cout << "  Virtual desktop: (" << bounds.x_virtual_screen << ", " << bounds.y_virtual_screen 
              << ") " << bounds.cx_virtual_screen << "x" << bounds.cy_virtual_screen << std::endl;
}

void test_native_mouse_move_and_abs() {
    std::cout << "[Test] Native Mouse Movement & Absolute Mapping..." << std::endl;
    WindowsInputInjector injector;

    // Relative move (0,0 is safe no-op)
    TEST_ASSERT(injector.inject_mouse_move(0, 0));
    TEST_ASSERT(injector.inject_mouse_move(5, -5));

    // Invalid dimensions fail-closed
    TEST_ASSERT(!injector.inject_mouse_abs(100, 100, 0, 1080));
    TEST_ASSERT(!injector.inject_mouse_abs(100, 100, 1920, -5));

    // Valid absolute move (center of 1080p stream)
    TEST_ASSERT(injector.inject_mouse_abs(960, 540, 1920, 1080));

    // Multi-monitor offset absolute move (secondary monitor offset)
    TEST_ASSERT(injector.inject_mouse_abs(960, 540, 1920, 1080, 1920, 0, 1920, 1080));
}

void test_native_mouse_buttons_and_scroll() {
    std::cout << "[Test] Native Mouse Buttons & Scroll..." << std::endl;
    WindowsInputInjector injector;

    // Invalid button
    TEST_ASSERT(!injector.inject_mouse_button(0, true));
    TEST_ASSERT(!injector.inject_mouse_button(6, true));

    // Valid buttons: Left(1), Right(2), Middle(3), X1(4), X2(5)
    for (uint8_t b = 1; b <= 5; ++b) {
        TEST_ASSERT(injector.inject_mouse_button(b, true));
        TEST_ASSERT(injector.inject_mouse_button(b, false));
    }

    // Scroll: vertical and horizontal
    TEST_ASSERT(injector.inject_mouse_scroll(0, false));
    TEST_ASSERT(injector.inject_mouse_scroll(120, false));
    TEST_ASSERT(injector.inject_mouse_scroll(-120, true));
}

void test_native_keyboard_and_extended_keys() {
    std::cout << "[Test] Native Keyboard & Extended Keys..." << std::endl;
    WindowsInputInjector injector;

    // Invalid vkey
    TEST_ASSERT(!injector.inject_keyboard_key(-1, 0, true, 0));
    TEST_ASSERT(!injector.inject_keyboard_key(256, 0, true, 0));

    // Standard key: VK_A (0x41)
    TEST_ASSERT(injector.inject_keyboard_key(0x41, 0, true, 0));
    TEST_ASSERT(injector.inject_keyboard_key(0x41, 0, false, 0));

    // Extended key: VK_RIGHT (0x27)
    TEST_ASSERT(injector.inject_keyboard_key(0x27, 0, true, 0));
    TEST_ASSERT(injector.inject_keyboard_key(0x27, 0, false, 0));
}

void test_native_stuck_key_release() {
    std::cout << "[Test] Native Stuck Key & Button Release..." << std::endl;
    WindowsInputInjector injector;

    // Hold left button and two keys
    TEST_ASSERT(injector.inject_mouse_button(1, true));
    TEST_ASSERT(injector.inject_keyboard_key(0x11, 0, true, 0)); // VK_CONTROL
    TEST_ASSERT(injector.inject_keyboard_key(0x57, 0, true, 0)); // 'W' key

    uint32_t released = injector.release_all_held_inputs();
    TEST_ASSERT(released >= 3);
    (void)released;

    // Second release should be zero
    uint32_t released_second = injector.release_all_held_inputs();
    TEST_ASSERT(released_second == 0);
    (void)released_second;
}

void test_c_abi_input_exports() {
    std::cout << "[Test] C-ABI Input Injector Exports..." << std::endl;
    void* injector = moonshine_input_injector_create();
    TEST_ASSERT(injector != nullptr);

    MoonshineVirtualDesktopBoundsC bounds{};
    TEST_ASSERT(moonshine_input_get_virtual_desktop_bounds(injector, &bounds) == 1);
    TEST_ASSERT(bounds.cx_virtual_screen > 0);

    TEST_ASSERT(moonshine_input_inject_mouse_move(injector, 0, 0) == 1);
    TEST_ASSERT(moonshine_input_inject_mouse_abs(injector, 100, 100, 1920, 1080, 0, 0, 1920, 1080) == 1);
    TEST_ASSERT(moonshine_input_inject_mouse_button(injector, 1, 0) == 1);
    TEST_ASSERT(moonshine_input_inject_mouse_scroll(injector, 120, 0) == 1);
    TEST_ASSERT(moonshine_input_inject_keyboard(injector, 0x41, 0, 0, 0) == 1);

    uint32_t released = moonshine_input_release_all_held(injector);
    (void)released;

    moonshine_input_injector_destroy(injector);
}

int main() {
    std::cout << "========================================" << std::endl;
    std::cout << "Running Moonshine Native Input Injector Tests" << std::endl;
    std::cout << "========================================" << std::endl;

    test_native_injector_lifecycle();
    test_native_mouse_move_and_abs();
    test_native_mouse_buttons_and_scroll();
    test_native_keyboard_and_extended_keys();
    test_native_stuck_key_release();
    test_c_abi_input_exports();

    std::cout << "[+] All Native Input Injector Tests Passed Successfully." << std::endl;
    return 0;
}
