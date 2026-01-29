//! The Inspector is a development tool to debug the terminal. This is
//! useful for terminal application developers as well as people potentially
//! debugging issues in Ghostty itself.
const Inspector = @This();

const std = @import("std");
const assert = @import("../quirks.zig").inlineAssert;
const Allocator = std.mem.Allocator;
const builtin = @import("builtin");
const cimgui = @import("dcimgui");
const Surface = @import("../Surface.zig");
const font = @import("../font/main.zig");
const terminal = @import("../terminal/main.zig");
const inspector = @import("main.zig");
const widgets = @import("widgets.zig");

/// The window names. These are used with docking so we need to have access.
const window_cell = "Cell";
const window_termio = "Terminal IO";
const window_imgui_demo = "Dear ImGui Demo";

/// The surface that we're inspecting.
surface: *Surface,

/// This is used to track whether we're rendering for the first time. This
/// is used to set up the initial window positions.
first_render: bool = true,

/// Mouse state that we track in addition to normal mouse states that
/// Ghostty always knows about.
mouse: widgets.surface.Mouse = .{},

/// A selected cell.
cell: CellInspect = .{ .idle = {} },

// ImGui state
gui: widgets.surface.Inspector,

const CellInspect = union(enum) {
    /// Idle, no cell inspection is requested
    idle: void,

    /// Requested, a cell is being picked.
    requested: void,

    /// The cell has been picked and set to this. This is a copy so that
    /// if the cell contents change we still have the original cell.
    selected: Selected,

    const Selected = struct {
        alloc: Allocator,
        row: usize,
        col: usize,
        cell: inspector.Cell,
    };

    pub fn deinit(self: *CellInspect) void {
        switch (self.*) {
            .idle, .requested => {},
            .selected => |*v| v.cell.deinit(v.alloc),
        }
    }

    pub fn request(self: *CellInspect) void {
        switch (self.*) {
            .idle => self.* = .requested,
            .selected => |*v| {
                v.cell.deinit(v.alloc);
                self.* = .requested;
            },
            .requested => {},
        }
    }

    pub fn select(
        self: *CellInspect,
        alloc: Allocator,
        pin: terminal.Pin,
        x: usize,
        y: usize,
    ) !void {
        assert(self.* == .requested);
        const cell = try inspector.Cell.init(alloc, pin);
        errdefer cell.deinit(alloc);
        self.* = .{ .selected = .{
            .alloc = alloc,
            .row = y,
            .col = x,
            .cell = cell,
        } };
    }
};

/// Setup the ImGui state. This requires an ImGui context to be set.
pub fn setup() void {
    const io: *cimgui.c.ImGuiIO = cimgui.c.ImGui_GetIO();

    // Enable docking, which we use heavily for the UI.
    io.ConfigFlags |= cimgui.c.ImGuiConfigFlags_DockingEnable;

    // Our colorspace is sRGB.
    io.ConfigFlags |= cimgui.c.ImGuiConfigFlags_IsSRGB;

    // Disable the ini file to save layout
    io.IniFilename = null;
    io.LogFilename = null;

    // Use our own embedded font
    {
        // TODO: This will have to be recalculated for different screen DPIs.
        // This is currently hardcoded to a 2x content scale.
        const font_size = 16 * 2;

        var font_config: cimgui.c.ImFontConfig = undefined;
        cimgui.ext.ImFontConfig_ImFontConfig(&font_config);
        font_config.FontDataOwnedByAtlas = false;
        _ = cimgui.c.ImFontAtlas_AddFontFromMemoryTTF(
            io.Fonts,
            @ptrCast(@constCast(font.embedded.regular.ptr)),
            @intCast(font.embedded.regular.len),
            font_size,
            &font_config,
            null,
        );
    }
}

pub fn init(surface: *Surface) !Inspector {
    var gui: widgets.surface.Inspector = try .init(surface.alloc, surface);
    errdefer gui.deinit(surface.alloc);

    return .{
        .surface = surface,
        .gui = gui,
    };
}

pub fn deinit(self: *Inspector) void {
    self.gui.deinit(self.surface.alloc);
    self.cell.deinit();
}

/// Record a keyboard event.
pub fn recordKeyEvent(self: *Inspector, ev: inspector.key.Event) !void {
    const max_capacity = 50;

    const events: *widgets.key.EventRing = &self.gui.key_stream.events;
    events.append(ev) catch |err| switch (err) {
        error.OutOfMemory => if (events.capacity() < max_capacity) {
            // We're out of memory, but we can allocate to our capacity.
            const new_capacity = @min(events.capacity() * 2, max_capacity);
            try events.resize(self.surface.alloc, new_capacity);
            try events.append(ev);
        } else {
            var it = events.iterator(.forward);
            if (it.next()) |old_ev| old_ev.deinit(self.surface.alloc);
            events.deleteOldest(1);
            try events.append(ev);
        },

        else => return err,
    };
}

/// Record data read from the pty.
pub fn recordPtyRead(self: *Inspector, data: []const u8) !void {
    try self.gui.vt_stream.parser_stream.nextSlice(data);
}

/// Render the frame.
pub fn render(self: *Inspector) void {
    self.gui.draw(
        self.surface,
        self.mouse,
    );
    if (true) return;

    const dock_id = cimgui.c.ImGui_DockSpaceOverViewport();

    // Render all of our data. We hold the mutex for this duration. This is
    // expensive but this is an initial implementation until it doesn't work
    // anymore.
    {
        self.surface.renderer_state.mutex.lock();
        defer self.surface.renderer_state.mutex.unlock();
        const t = self.surface.renderer_state.terminal;
        self.windows.terminal.render(t);
        self.windows.surface.render(.{
            .surface = self.surface,
            .mouse = self.mouse,
        });
        self.renderTermioWindow();
        self.renderCellWindow();
    }

    // In debug we show the ImGui demo window so we can easily view available
    // widgets and such.
    if (builtin.mode == .Debug) {
        var show: bool = true;
        cimgui.c.ImGui_ShowDemoWindow(&show);
    }

    // On first render we set up the layout. We can actually do this at
    // the end of the frame, allowing the individual rendering to also
    // observe the first render flag.
    if (self.first_render) {
        self.first_render = false;
        self.setupLayout(dock_id);
    }
}

fn setupLayout(self: *Inspector, dock_id_main: cimgui.c.ImGuiID) void {
    _ = self;

    // Our initial focus
    cimgui.c.ImGui_SetWindowFocusStr(inspector.terminal.Window.name);

    // Setup our initial layout - all windows in a single dock as tabs.
    // Surface is docked first so it appears as the first tab.
    cimgui.ImGui_DockBuilderDockWindow(inspector.surface.Window.name, dock_id_main);
    cimgui.ImGui_DockBuilderDockWindow(inspector.terminal.Window.name, dock_id_main);
    cimgui.ImGui_DockBuilderDockWindow(window_termio, dock_id_main);
    cimgui.ImGui_DockBuilderDockWindow(window_cell, dock_id_main);
    cimgui.ImGui_DockBuilderDockWindow(window_imgui_demo, dock_id_main);
    cimgui.ImGui_DockBuilderFinish(dock_id_main);
}

fn renderCellWindow(self: *Inspector) void {
    // Start our window. If we're collapsed we do nothing.
    defer cimgui.c.ImGui_End();
    if (!cimgui.c.ImGui_Begin(
        window_cell,
        null,
        cimgui.c.ImGuiWindowFlags_NoFocusOnAppearing,
    )) return;

    // Our popup for the picker
    const popup_picker = "Cell Picker";

    if (cimgui.c.ImGui_Button("Picker")) {
        // Request a cell
        self.cell.request();

        cimgui.c.ImGui_OpenPopup(
            popup_picker,
            cimgui.c.ImGuiPopupFlags_None,
        );
    }

    if (cimgui.c.ImGui_BeginPopupModal(
        popup_picker,
        null,
        cimgui.c.ImGuiWindowFlags_AlwaysAutoResize,
    )) popup: {
        defer cimgui.c.ImGui_EndPopup();

        // Once we select a cell, close this popup.
        if (self.cell == .selected) {
            cimgui.c.ImGui_CloseCurrentPopup();
            break :popup;
        }

        cimgui.c.ImGui_Text(
            "Click on a cell in the terminal to inspect it.\n" ++
                "The click will be intercepted by the picker, \n" ++
                "so it won't be sent to the terminal.",
        );
        cimgui.c.ImGui_Separator();

        if (cimgui.c.ImGui_Button("Cancel")) {
            cimgui.c.ImGui_CloseCurrentPopup();
        }
    } // cell pick popup

    cimgui.c.ImGui_Separator();

    if (self.cell != .selected) {
        cimgui.c.ImGui_Text("No cell selected.");
        return;
    }

    const selected = self.cell.selected;
    selected.cell.renderTable(
        self.surface.renderer_state.terminal,
        selected.col,
        selected.row,
    );
}
