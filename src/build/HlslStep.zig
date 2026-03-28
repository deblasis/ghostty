/// A zig build step that compiles a set of ".hlsl" files into
/// ".cso" (compiled shader object) files using fxc.exe from the
/// Windows SDK. Returns null on non-Windows targets.
const HlslStep = @This();

const std = @import("std");
const Step = std.Build.Step;
const RunStep = std.Build.Step.Run;
const LazyPath = std.Build.LazyPath;

pub const ShaderEntry = struct {
    /// The HLSL source file.
    source: LazyPath,
    /// Shader profile (e.g. "vs_5_0", "ps_5_0").
    profile: []const u8,
    /// Entry point function name (e.g. "VSMain", "PSMain").
    entry_point: []const u8,
    /// Output name (e.g. "cell_vs" -> "cell_vs.cso").
    output_name: []const u8,
};

pub const Options = struct {
    target: std.Build.ResolvedTarget,
    shaders: []const ShaderEntry,
};

step: *Step,
outputs: []const LazyPath,

pub fn create(b: *std.Build, opts: Options) ?*HlslStep {
    if (opts.target.result.os.tag != .windows) return null;

    const self = b.allocator.create(HlslStep) catch @panic("OOM");

    // Find fxc.exe from the Windows SDK.
    const fxc_path = findFxc(b, opts.target.result.cpu.arch) orelse {
        std.log.warn("fxc.exe not found; HLSL shaders will not be compiled", .{});
        return null;
    };

    var outputs = b.allocator.alloc(LazyPath, opts.shaders.len) catch @panic("OOM");
    var last_step: ?*Step = null;

    for (opts.shaders, 0..) |shader, i| {
        const run = RunStep.create(
            b,
            b.fmt("hlsl {s}", .{shader.output_name}),
        );
        run.addArgs(&.{
            fxc_path,
            "/T",
            shader.profile,
            "/E",
            shader.entry_point,
            "/Fo",
        });
        outputs[i] = run.addOutputFileArg(
            b.fmt("{s}.cso", .{shader.output_name}),
        );
        run.addFileArg(shader.source);

        if (last_step) |prev| {
            run.step.dependOn(prev);
        }
        last_step = &run.step;
    }

    self.* = .{
        .step = last_step.?,
        .outputs = outputs,
    };

    return self;
}

fn findFxc(b: *std.Build, arch: std.Target.Cpu.Arch) ?[]const u8 {
    const arch_str: []const u8 = switch (arch) {
        .x86_64 => "x64",
        .x86 => "x86",
        .aarch64 => "arm64",
        else => "x64",
    };

    const sdk = std.zig.WindowsSdk.find(b.allocator, arch) catch return null;
    const w10 = sdk.windows10sdk orelse return null;

    return std.fmt.allocPrint(
        b.allocator,
        "{s}\\bin\\{s}\\{s}\\fxc.exe",
        .{ w10.path, w10.version, arch_str },
    ) catch null;
}
