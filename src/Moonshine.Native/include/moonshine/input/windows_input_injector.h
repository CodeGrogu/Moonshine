#ifndef MOONSHINE_WINDOWS_INPUT_INJECTOR_H
#define MOONSHINE_WINDOWS_INPUT_INJECTOR_H

#include <cstdint>
#include <mutex>
#include <algorithm>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <windows.h>
#endif

namespace moonshine::input {

/**
 * @brief Represents the geometric bounds of the Windows virtual desktop spanning all active monitors.
 */
struct VirtualDesktopBounds {
    int32_t x_virtual_screen{0};
    int32_t y_virtual_screen{0};
    int32_t cx_virtual_screen{1920};
    int32_t cy_virtual_screen{1080};
};

/**
 * @brief High-performance Windows mouse and keyboard input injector with multi-monitor virtual-desktop
 * mapping, DPI-aware scaling, extended key translation, hardware scan code support, and stuck-key tracking.
 */
class WindowsInputInjector {
public:
    WindowsInputInjector();
    ~WindowsInputInjector();

    WindowsInputInjector(const WindowsInputInjector&) = delete;
    WindowsInputInjector& operator=(const WindowsInputInjector&) = delete;

    /**
     * @brief Injects high-DPI relative mouse movement deltas into the Windows OS input stream.
     */
    bool inject_mouse_move(int16_t delta_x, int16_t delta_y);

    /**
     * @brief Injects normalized absolute mouse positions mapped across multi-monitor virtual desktop topology.
     */
    bool inject_mouse_abs(int32_t x, int32_t y, int32_t client_width, int32_t client_height,
                          int32_t monitor_offset_x = 0, int32_t monitor_offset_y = 0,
                          int32_t monitor_width = 0, int32_t monitor_height = 0);

    /**
     * @brief Injects mouse button press or release transitions (1: Left, 2: Right, 3: Middle, 4: X1, 5: X2).
     */
    bool inject_mouse_button(uint8_t button_index, bool is_down);

    /**
     * @brief Injects vertical or horizontal mouse wheel scroll deltas.
     */
    bool inject_mouse_scroll(int16_t scroll_delta, bool is_horizontal);

    /**
     * @brief Injects keyboard key transitions with extended key flag and hardware scan code translation.
     */
    bool inject_keyboard_key(int16_t virtual_key_code, int16_t scan_code, bool is_down, uint8_t modifiers);

#if defined(_WIN32)
    /**
     * @brief Injects a batch of pre-constructed Win32 INPUT structures in a single system call.
     */
    uint32_t inject_batch(const INPUT* inputs, uint32_t count);
#endif

    /**
     * @brief Releases all currently held keyboard keys and mouse buttons to prevent stuck states on session loss.
     * @return Number of release events dispatched.
     */
    uint32_t release_all_held_inputs();

    /**
     * @brief Retrieves the cached virtual desktop bounding box.
     */
    VirtualDesktopBounds get_virtual_desktop_bounds() const;

    /**
     * @brief Refreshes the virtual desktop geometry from Win32 system metrics.
     */
    void refresh_virtual_desktop_bounds();

private:
    mutable std::mutex sync_root_{};
    VirtualDesktopBounds bounds_{};
    uint64_t held_keys_[4]{0, 0, 0, 0}; // 256-bit bitmask for VK codes 0..255
    uint8_t held_buttons_{0};           // 5-bit bitmask for buttons 1..5
    bool disposed_{false};
};

} // namespace moonshine::input

#endif // MOONSHINE_WINDOWS_INPUT_INJECTOR_H
