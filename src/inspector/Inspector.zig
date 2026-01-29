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

/// The VT stream
vt_events: inspector.termio.VTEventRing,
vt_stream: inspector.termio.Stream,

/// The currently selected event sequence number for keyboard navigation
selected_event_seq: ?u32 = null,

/// Flag indicating whether we need to scroll to the selected item
need_scroll_to_selected: bool = false,

/// Flag indicating whether the selection was made by keyboard
is_keyboard_selection: bool = false,

// ImGui state
gui: widgets.surface.Inspector,

/// Enum representing keyboard navigation actions
const KeyAction = enum {
    down,
    none,
    up,
};

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
    var vt_events = try inspector.termio.VTEventRing.init(surface.alloc, 2);
    errdefer vt_events.deinit(surface.alloc);

    var vt_handler = inspector.termio.VTHandler.init(surface);
    errdefer vt_handler.deinit();

    var gui: widgets.surface.Inspector = try .init(surface.alloc);
    errdefer gui.deinit(surface.alloc);

    return .{
        .surface = surface,
        .gui = gui,
        .vt_events = vt_events,
        .vt_stream = .initAlloc(surface.alloc, vt_handler),
    };
}

pub fn deinit(self: *Inspector) void {
    self.gui.deinit(self.surface.alloc);
    self.cell.deinit();

    {
        var it = self.vt_events.iterator(.forward);
        while (it.next()) |v| v.deinit(self.surface.alloc);
        self.vt_events.deinit(self.surface.alloc);

        self.vt_stream.deinit();
    }
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
    try self.vt_stream.nextSlice(data);
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

/// Helper function to check keyboard state and determine navigation action.
fn getKeyAction(self: *Inspector) KeyAction {
    _ = self;
    const keys = .{
        .{ .key = cimgui.c.ImGuiKey_J, .action = KeyAction.down },
        .{ .key = cimgui.c.ImGuiKey_DownArrow, .action = KeyAction.down },
        .{ .key = cimgui.c.ImGuiKey_K, .action = KeyAction.up },
        .{ .key = cimgui.c.ImGuiKey_UpArrow, .action = KeyAction.up },
    };

    inline for (keys) |k| {
        if (cimgui.c.ImGui_IsKeyPressed(k.key)) {
            return k.action;
        }
    }
    return .none;
}

fn renderTermioWindow(self: *Inspector) void {
    // Start our window. If we're collapsed we do nothing.
    defer cimgui.c.ImGui_End();
    if (!cimgui.c.ImGui_Begin(
        window_termio,
        null,
        cimgui.c.ImGuiWindowFlags_NoFocusOnAppearing,
    )) return;

    const popup_filter = "Filter";

    list: {
        const pause_play: [:0]const u8 = if (self.vt_stream.handler.active)
            "Pause##pause_play"
        else
            "Resume##pause_play";
        if (cimgui.c.ImGui_Button(pause_play.ptr)) {
            self.vt_stream.handler.active = !self.vt_stream.handler.active;
        }

        cimgui.c.ImGui_SameLineEx(0, cimgui.c.ImGui_GetStyle().*.ItemInnerSpacing.x);
        if (cimgui.c.ImGui_Button("Filter")) {
            cimgui.c.ImGui_OpenPopup(
                popup_filter,
                cimgui.c.ImGuiPopupFlags_None,
            );
        }

        if (!self.vt_events.empty()) {
            cimgui.c.ImGui_SameLineEx(0, cimgui.c.ImGui_GetStyle().*.ItemInnerSpacing.x);
            if (cimgui.c.ImGui_Button("Clear")) {
                var it = self.vt_events.iterator(.forward);
                while (it.next()) |v| v.deinit(self.surface.alloc);
                self.vt_events.clear();

                // We also reset the sequence number.
                self.vt_stream.handler.current_seq = 1;
            }
        }

        cimgui.c.ImGui_Separator();

        if (self.vt_events.empty()) {
            cimgui.c.ImGui_Text("Waiting for events...");
            break :list;
        }

        _ = cimgui.c.ImGui_BeginTable(
            "table_vt_events",
            3,
            cimgui.c.ImGuiTableFlags_RowBg |
                cimgui.c.ImGuiTableFlags_Borders,
        );
        defer cimgui.c.ImGui_EndTable();

        cimgui.c.ImGui_TableSetupColumn(
            "Seq",
            cimgui.c.ImGuiTableColumnFlags_WidthFixed,
        );
        cimgui.c.ImGui_TableSetupColumn(
            "Kind",
            cimgui.c.ImGuiTableColumnFlags_WidthFixed,
        );
        cimgui.c.ImGui_TableSetupColumn(
            "Description",
            cimgui.c.ImGuiTableColumnFlags_WidthStretch,
        );

        // Handle keyboard navigation when window is focused
        if (cimgui.c.ImGui_IsWindowFocused(cimgui.c.ImGuiFocusedFlags_RootAndChildWindows)) {
            const key_pressed = self.getKeyAction();

            switch (key_pressed) {
                .none => {},
                .up, .down => {
                    // If no event is selected, select the first/last event based on direction
                    if (self.selected_event_seq == null) {
                        if (!self.vt_events.empty()) {
                            var it = self.vt_events.iterator(if (key_pressed == .up) .forward else .reverse);
                            if (it.next()) |ev| {
                                self.selected_event_seq = @as(u32, @intCast(ev.seq));
                            }
                        }
                    } else {
                        // Find next/previous event based on current selection
                        var it = self.vt_events.iterator(.reverse);
                        switch (key_pressed) {
                            .down => {
                                var found = false;
                                while (it.next()) |ev| {
                                    if (found) {
                                        self.selected_event_seq = @as(u32, @intCast(ev.seq));
                                        break;
                                    }
                                    if (ev.seq == self.selected_event_seq.?) {
                                        found = true;
                                    }
                                }
                            },
                            .up => {
                                var prev_ev: ?*const inspector.termio.VTEvent = null;
                                while (it.next()) |ev| {
                                    if (ev.seq == self.selected_event_seq.?) {
                                        if (prev_ev) |prev| {
                                            self.selected_event_seq = @as(u32, @intCast(prev.seq));
                                            break;
                                        }
                                    }
                                    prev_ev = ev;
                                }
                            },
                            .none => unreachable,
                        }
                    }

                    // Mark that we need to scroll to the newly selected item
                    self.need_scroll_to_selected = true;
                    self.is_keyboard_selection = true;
                },
            }
        }

        var it = self.vt_events.iterator(.reverse);
        while (it.next()) |ev| {
            // Need to push an ID so that our selectable is unique.
            cimgui.c.ImGui_PushIDPtr(ev);
            defer cimgui.c.ImGui_PopID();

            cimgui.c.ImGui_TableNextRow();
            _ = cimgui.c.ImGui_TableNextColumn();

            // Store the previous selection state to detect changes
            const was_selected = ev.imgui_selected;

            // Update selection state based on keyboard navigation
            if (self.selected_event_seq) |seq| {
                ev.imgui_selected = (@as(u32, @intCast(ev.seq)) == seq);
            }

            // Handle selectable widget
            if (cimgui.c.ImGui_SelectableBoolPtr(
                "##select",
                &ev.imgui_selected,
                cimgui.c.ImGuiSelectableFlags_SpanAllColumns,
            )) {
                // If selection state changed, update keyboard navigation state
                if (ev.imgui_selected != was_selected) {
                    self.selected_event_seq = if (ev.imgui_selected)
                        @as(u32, @intCast(ev.seq))
                    else
                        null;
                    self.is_keyboard_selection = false;
                }
            }

            cimgui.c.ImGui_SameLine();
            cimgui.c.ImGui_Text("%d", ev.seq);
            _ = cimgui.c.ImGui_TableNextColumn();
            cimgui.c.ImGui_Text("%s", @tagName(ev.kind).ptr);
            _ = cimgui.c.ImGui_TableNextColumn();
            cimgui.c.ImGui_Text("%s", ev.str.ptr);

            // If the event is selected, we render info about it. For now
            // we put this in the last column because that's the widest and
            // imgui has no way to make a column span.
            if (ev.imgui_selected) {
                {
                    widgets.screen.cursorTable(&ev.cursor);
                    widgets.screen.cursorStyle(
                        &ev.cursor,
                        &self.surface.renderer_state.terminal.colors.palette.current,
                    );

                    _ = cimgui.c.ImGui_BeginTable(
                        "details",
                        2,
                        cimgui.c.ImGuiTableFlags_None,
                    );
                    defer cimgui.c.ImGui_EndTable();
                    {
                        cimgui.c.ImGui_TableNextRow();
                        {
                            _ = cimgui.c.ImGui_TableSetColumnIndex(0);
                            cimgui.c.ImGui_Text("Scroll Region");
                        }
                        {
                            _ = cimgui.c.ImGui_TableSetColumnIndex(1);
                            cimgui.c.ImGui_Text(
                                "T=%d B=%d L=%d R=%d",
                                ev.scrolling_region.top,
                                ev.scrolling_region.bottom,
                                ev.scrolling_region.left,
                                ev.scrolling_region.right,
                            );
                        }
                    }

                    var md_it = ev.metadata.iterator();
                    while (md_it.next()) |entry| {
                        var buf: [256]u8 = undefined;
                        const key = std.fmt.bufPrintZ(&buf, "{s}", .{entry.key_ptr.*}) catch
                            "<internal error>";
                        cimgui.c.ImGui_TableNextRow();
                        _ = cimgui.c.ImGui_TableNextColumn();
                        cimgui.c.ImGui_Text("%s", key.ptr);
                        _ = cimgui.c.ImGui_TableNextColumn();
                        cimgui.c.ImGui_Text("%s", entry.value_ptr.ptr);
                    }
                }

                // If this is the selected event and scrolling is needed, scroll to it
                if (self.need_scroll_to_selected and self.is_keyboard_selection) {
                    cimgui.c.ImGui_SetScrollHereY(0.5);
                    self.need_scroll_to_selected = false;
                }
            }
        }
    } // table

    if (cimgui.c.ImGui_BeginPopupModal(
        popup_filter,
        null,
        cimgui.c.ImGuiWindowFlags_AlwaysAutoResize,
    )) {
        defer cimgui.c.ImGui_EndPopup();

        cimgui.c.ImGui_Text("Changed filter settings will only affect future events.");

        cimgui.c.ImGui_Separator();

        {
            _ = cimgui.c.ImGui_BeginTable(
                "table_filter_kind",
                3,
                cimgui.c.ImGuiTableFlags_None,
            );
            defer cimgui.c.ImGui_EndTable();

            inline for (@typeInfo(terminal.Parser.Action.Tag).@"enum".fields) |field| {
                const tag = @field(terminal.Parser.Action.Tag, field.name);
                if (tag == .apc_put or tag == .dcs_put) continue;

                _ = cimgui.c.ImGui_TableNextColumn();
                var value = !self.vt_stream.handler.filter_exclude.contains(tag);
                if (cimgui.c.ImGui_Checkbox(@tagName(tag).ptr, &value)) {
                    if (value) {
                        self.vt_stream.handler.filter_exclude.remove(tag);
                    } else {
                        self.vt_stream.handler.filter_exclude.insert(tag);
                    }
                }
            }
        } // Filter kind table

        cimgui.c.ImGui_Separator();

        cimgui.c.ImGui_Text(
            "Filter by string. Empty displays all, \"abc\" finds lines\n" ++
                "containing \"abc\", \"abc,xyz\" finds lines containing \"abc\"\n" ++
                "or \"xyz\", \"-abc\" excludes lines containing \"abc\".",
        );
        _ = cimgui.c.ImGuiTextFilter_Draw(
            &self.vt_stream.handler.filter_text,
            "##filter_text",
            0,
        );

        cimgui.c.ImGui_Separator();
        if (cimgui.c.ImGui_Button("Close")) {
            cimgui.c.ImGui_CloseCurrentPopup();
        }
    } // filter popup
}
