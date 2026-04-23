const std = @import("std");
const builtin = @import("builtin");
const windows = std.os.windows;

const log = std.log.scoped(.shader_wrapper);

const HMODULE = windows.HMODULE;

const WrapperInitFn = *const fn () callconv(.c) c_int;
const WrapperCompileHlslFn = *const fn (
    source: [*:0]const u8,
    source_len: c_int,
    out_buf: [*]u8,
    out_buf_cap: c_int,
    out_len: *c_int,
) callconv(.c) c_int;
const WrapperGetErrorFn = *const fn () callconv(.c) [*:0]const u8;

var dll: ?HMODULE = null;
var fn_init: ?WrapperInitFn = null;
var fn_compile_hlsl: ?WrapperCompileHlslFn = null;
var fn_get_error: ?WrapperGetErrorFn = null;

pub fn ensureLoaded() !void {
    if (dll != null) return;

    const dll_name = if (builtin.is_test) "shader_wrapper.dll" else "shader_wrapper.dll";
    const dll_path_w = std.unicode.utf8ToUtf16LeStringLiteral(dll_name);
    const handle = windows.LoadLibraryW(dll_path_w) catch return error.DllNotFound;

    dll = handle;

    const init_proc = windows.kernel32.GetProcAddress(handle, "shader_wrapper_init") orelse return error.SymbolNotFound;
    fn_init = @ptrCast(init_proc);

    const compile_proc = windows.kernel32.GetProcAddress(handle, "shader_wrapper_compile_hlsl") orelse return error.SymbolNotFound;
    fn_compile_hlsl = @ptrCast(compile_proc);

    const error_proc = windows.kernel32.GetProcAddress(handle, "shader_wrapper_get_error") orelse return error.SymbolNotFound;
    fn_get_error = @ptrCast(error_proc);

    const rc = fn_init.?();
    if (rc != 0) return error.GlslangInitFailed;

    log.info("shader_wrapper.dll loaded and initialized", .{});
}

/// Compile a GLSL shader (shadertoy-style) to HLSL via the MSVC-compiled wrapper DLL.
/// Returns a null-terminated HLSL string allocated with alloc_gpa.
pub fn compileToHlsl(alloc: std.mem.Allocator, source: [:0]const u8) ![:0]const u8 {
    try ensureLoaded();

    var out_len: c_int = 0;
    var buf: [4 * 1024 * 1024]u8 = undefined;

    const rc = fn_compile_hlsl.?(
        source.ptr,
        @intCast(source.len),
        &buf,
        @intCast(buf.len),
        &out_len,
    );

    if (rc != 0) {
        const err_msg = if (fn_get_error) |f| std.mem.sliceTo(f(), 0) else "(no error fn)";
        std.debug.print("[shader_wrapper] compile_hlsl failed: rc={}, msg={s}\n", .{ rc, err_msg });
        return error.GlslangFailed;
    }

    const len: usize = @intCast(out_len);
    return try alloc.dupeZ(u8, buf[0..len]);
}
