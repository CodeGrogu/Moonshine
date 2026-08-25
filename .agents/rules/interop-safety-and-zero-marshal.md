# Interop Safety & Zero-Marshal Rules

All native interop bindings and memory structures must conform to [`STANDARDS.md`](../../STANDARDS.md).

These rules govern the C# / C++ boundary in `Moonshine.Interop`.

## 1. Blittable Type Layouts
- All shared structs must specify explicit packing (`[StructLayout(LayoutKind.Sequential, Pack = 1)]` or matching native struct alignment).
- Field types must match 1:1 in byte size and offset across 64-bit architectures:
  - `uint8_t` <-> `byte`
  - `uint16_t` <-> `ushort`
  - `uint32_t` <-> `uint`
  - `uint64_t` <-> `ulong`
  - `size_t` <-> `nuint`
  - `void*` / `const uint8_t*` <-> `byte*` / `IntPtr`

## 2. [LibraryImport] Source Generators
- Never use legacy `[DllImport]`. Use `[LibraryImport]` with source generators for zero-overhead P/Invoke dispatch.
- Specify `[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]`.
- Avoid string marshaling in P/Invoke; pass raw UTF-8 byte pointers or spans.
