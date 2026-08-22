#include "moonshine/input/windows_input_injector.h"

namespace moonshine::input {

WindowsInputInjector::WindowsInputInjector() {
    refresh_virtual_desktop_bounds();
}

WindowsInputInjector::~WindowsInputInjector() {
    release_all_held_inputs();
    disposed_ = true;
}

void WindowsInputInjector::refresh_virtual_desktop_bounds() {
    std::lock_guard<std::mutex> lock(sync_root_);
#if defined(_WIN32)
    int32_t x = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int32_t y = GetSystemMetrics(SM_YVIRTUALSCREEN);
    int32_t cx = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    int32_t cy = GetSystemMetrics(SM_CYVIRTUALSCREEN);

    if (cx <= 0 || cy <= 0) {
        x = 0;
        y = 0;
        cx = GetSystemMetrics(SM_CXSCREEN);
        cy = GetSystemMetrics(SM_CYSCREEN);
        if (cx <= 0) cx = 1920;
        if (cy <= 0) cy = 1080;
    }

    bounds_.x_virtual_screen = x;
    bounds_.y_virtual_screen = y;
    bounds_.cx_virtual_screen = cx;
    bounds_.cy_virtual_screen = cy;
#else
    bounds_ = {0, 0, 1920, 1080};
#endif
}

VirtualDesktopBounds WindowsInputInjector::get_virtual_desktop_bounds() const {
    std::lock_guard<std::mutex> lock(sync_root_);
    return bounds_;
}

bool WindowsInputInjector::inject_mouse_move(int16_t delta_x, int16_t delta_y) {
    if (disposed_) return false;
    if (delta_x == 0 && delta_y == 0) return true;

#if defined(_WIN32)
    INPUT input{};
    input.type = INPUT_MOUSE;
    input.mi.dx = delta_x;
    input.mi.dy = delta_y;
    input.mi.dwFlags = MOUSEEVENTF_MOVE;

    UINT sent = SendInput(1, &input, sizeof(INPUT));
    if (sent == 1) return true;
    DWORD err = GetLastError();
    return (err == ERROR_ACCESS_DENIED || err == 0);
#else
    return false;
#endif
}

bool WindowsInputInjector::inject_mouse_abs(int32_t x, int32_t y, int32_t client_width, int32_t client_height,
                                           int32_t monitor_offset_x, int32_t monitor_offset_y,
                                           int32_t monitor_width, int32_t monitor_height) {
    if (disposed_) return false;
    if (client_width <= 0 || client_height <= 0) return false;

    VirtualDesktopBounds b;
    {
        std::lock_guard<std::mutex> lock(sync_root_);
        b = bounds_;
    }

    if (b.cx_virtual_screen <= 1 || b.cy_virtual_screen <= 1) {
        return false;
    }

    if (monitor_width <= 0) monitor_width = b.cx_virtual_screen;
    if (monitor_height <= 0) monitor_height = b.cy_virtual_screen;

    int32_t clamped_x = std::clamp(x, 0, client_width - 1);
    int32_t clamped_y = std::clamp(y, 0, client_height - 1);

    int64_t target_virt_x = monitor_offset_x + ((int64_t)clamped_x * monitor_width) / client_width;
    int64_t target_virt_y = monitor_offset_y + ((int64_t)clamped_y * monitor_height) / client_height;

    int32_t norm_x = static_cast<int32_t>(((target_virt_x - b.x_virtual_screen) * 65535LL + ((b.cx_virtual_screen - 1) / 2)) / (b.cx_virtual_screen - 1));
    int32_t norm_y = static_cast<int32_t>(((target_virt_y - b.y_virtual_screen) * 65535LL + ((b.cy_virtual_screen - 1) / 2)) / (b.cy_virtual_screen - 1));

    norm_x = std::clamp(norm_x, 0, 65535);
    norm_y = std::clamp(norm_y, 0, 65535);

#if defined(_WIN32)
    INPUT input{};
    input.type = INPUT_MOUSE;
    input.mi.dx = norm_x;
    input.mi.dy = norm_y;
    input.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

    UINT sent = SendInput(1, &input, sizeof(INPUT));
    if (sent == 1) return true;
    DWORD err = GetLastError();
    return (err == ERROR_ACCESS_DENIED || err == 0);
#else
    return false;
#endif
}

bool WindowsInputInjector::inject_mouse_button(uint8_t button_index, bool is_down) {
    if (disposed_) return false;
    if (button_index < 1 || button_index > 5) return false;

#if defined(_WIN32)
    DWORD flags = 0;
    DWORD mouse_data = 0;

    switch (button_index) {
        case 1:
            flags = is_down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
            break;
        case 2:
            flags = is_down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
            break;
        case 3:
            flags = is_down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
            break;
        case 4:
            flags = is_down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
            mouse_data = XBUTTON1;
            break;
        case 5:
            flags = is_down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
            mouse_data = XBUTTON2;
            break;
        default:
            return false;
    }

    {
        std::lock_guard<std::mutex> lock(sync_root_);
        uint8_t mask = static_cast<uint8_t>(1U << (button_index - 1));
        if (is_down) {
            held_buttons_ |= mask;
        } else {
            held_buttons_ &= static_cast<uint8_t>(~mask);
        }
    }

    INPUT input{};
    input.type = INPUT_MOUSE;
    input.mi.mouseData = mouse_data;
    input.mi.dwFlags = flags;

    UINT sent = SendInput(1, &input, sizeof(INPUT));
    if (sent == 1) return true;
    DWORD err = GetLastError();
    return (err == ERROR_ACCESS_DENIED || err == 0);
#else
    return false;
#endif
}

bool WindowsInputInjector::inject_mouse_scroll(int16_t scroll_delta, bool is_horizontal) {
    if (disposed_) return false;
    if (scroll_delta == 0) return true;

#if defined(_WIN32)
    INPUT input{};
    input.type = INPUT_MOUSE;
    input.mi.mouseData = static_cast<DWORD>(scroll_delta);
    input.mi.dwFlags = is_horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;

    UINT sent = SendInput(1, &input, sizeof(INPUT));
    if (sent == 1) return true;
    DWORD err = GetLastError();
    return (err == ERROR_ACCESS_DENIED || err == 0);
#else
    return false;
#endif
}

bool WindowsInputInjector::inject_keyboard_key(int16_t virtual_key_code, int16_t scan_code, bool is_down, uint8_t modifiers) {
    (void)modifiers;
    if (disposed_) return false;
    if (virtual_key_code < 0 || virtual_key_code > 255) return false;

#if defined(_WIN32)
    uint8_t vkey = static_cast<uint8_t>(virtual_key_code);
    DWORD flags = is_down ? 0 : KEYEVENTF_KEYUP;

    if (scan_code == 0) {
        scan_code = static_cast<int16_t>(MapVirtualKeyW(vkey, MAPVK_VK_TO_VSC));
    }

    if (scan_code != 0) {
        flags |= KEYEVENTF_SCANCODE;
    }

    // Extended key detection:
    // PageUp (0x21), PageDown (0x22), End (0x23), Home (0x24),
    // Left (0x25), Up (0x26), Right (0x27), Down (0x28),
    // Insert (0x2D), Delete (0x2E), Right Ctrl (0xA3), Right Alt (0xA5),
    // NumLock (0x90), Divide (0x6F), PrintScreen (0x2C)
    if (vkey == 0x21 || vkey == 0x22 || vkey == 0x23 || vkey == 0x24 ||
        vkey == 0x25 || vkey == 0x26 || vkey == 0x27 || vkey == 0x28 ||
        vkey == 0x2D || vkey == 0x2E || vkey == 0xA3 || vkey == 0xA5 ||
        vkey == 0x90 || vkey == 0x6F || vkey == 0x2C) {
        flags |= KEYEVENTF_EXTENDEDKEY;
    }

    {
        std::lock_guard<std::mutex> lock(sync_root_);
        int block = vkey >> 6;
        int bit = vkey & 63;
        uint64_t mask = 1ULL << bit;

        if (is_down) {
            held_keys_[block] |= mask;
        } else {
            held_keys_[block] &= ~mask;
        }
    }

    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = vkey;
    input.ki.wScan = static_cast<WORD>(scan_code);
    input.ki.dwFlags = flags;

    UINT sent = SendInput(1, &input, sizeof(INPUT));
    if (sent == 1) return true;
    DWORD err = GetLastError();
    return (err == ERROR_ACCESS_DENIED || err == 0);
#else
    return false;
#endif
}

#if defined(_WIN32)
uint32_t WindowsInputInjector::inject_batch(const INPUT* inputs, uint32_t count) {
    if (disposed_ || inputs == nullptr || count == 0) return 0;
    UINT sent = SendInput(count, const_cast<INPUT*>(inputs), sizeof(INPUT));
    if (sent == count) return count;
    DWORD err = GetLastError();
    if (err == ERROR_ACCESS_DENIED || err == 0) return count;
    return sent;
}
#endif

uint32_t WindowsInputInjector::release_all_held_inputs() {
    if (disposed_) return 0;

#if defined(_WIN32)
    uint32_t release_count = 0;
    INPUT inputs[32];

    std::lock_guard<std::mutex> lock(sync_root_);

    // Release mouse buttons
    for (uint8_t b = 1; b <= 5; ++b) {
        if ((held_buttons_ & (1U << (b - 1))) != 0) {
            DWORD flags = 0;
            DWORD mouse_data = 0;

            switch (b) {
                case 1: flags = MOUSEEVENTF_LEFTUP; break;
                case 2: flags = MOUSEEVENTF_RIGHTUP; break;
                case 3: flags = MOUSEEVENTF_MIDDLEUP; break;
                case 4: flags = MOUSEEVENTF_XUP; mouse_data = XBUTTON1; break;
                case 5: flags = MOUSEEVENTF_XUP; mouse_data = XBUTTON2; break;
                default: break;
            }

            if (flags != 0 && release_count < 32) {
                INPUT& inp = inputs[release_count++];
                inp.type = INPUT_MOUSE;
                inp.mi.dx = 0;
                inp.mi.dy = 0;
                inp.mi.mouseData = mouse_data;
                inp.mi.dwFlags = flags;
                inp.mi.time = 0;
                inp.mi.dwExtraInfo = 0;
            }
        }
    }
    held_buttons_ = 0;

    // Release keyboard keys
    for (int block = 0; block < 4; ++block) {
        if (held_keys_[block] == 0) continue;

        for (int bit = 0; bit < 64; ++bit) {
            if ((held_keys_[block] & (1ULL << bit)) != 0) {
                uint8_t vkey = static_cast<uint8_t>((block << 6) | bit);
                DWORD flags = KEYEVENTF_KEYUP;

                if (vkey == 0x21 || vkey == 0x22 || vkey == 0x23 || vkey == 0x24 ||
                    vkey == 0x25 || vkey == 0x26 || vkey == 0x27 || vkey == 0x28 ||
                    vkey == 0x2D || vkey == 0x2E || vkey == 0xA3 || vkey == 0xA5 ||
                    vkey == 0x90 || vkey == 0x6F || vkey == 0x2C) {
                    flags |= KEYEVENTF_EXTENDEDKEY;
                }

                if (release_count < 32) {
                    INPUT& inp = inputs[release_count++];
                    inp.type = INPUT_KEYBOARD;
                    inp.ki.wVk = vkey;
                    inp.ki.wScan = 0;
                    inp.ki.dwFlags = flags;
                    inp.ki.time = 0;
                    inp.ki.dwExtraInfo = 0;
                }
            }
        }
        held_keys_[block] = 0;
    }

    if (release_count > 0) {
        SendInput(release_count, inputs, sizeof(INPUT));
    }

    return release_count;
#else
    return 0;
#endif
}

} // namespace moonshine::input
