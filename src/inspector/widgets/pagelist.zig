const std = @import("std");
const cimgui = @import("dcimgui");
const terminal = @import("../../terminal/main.zig");
const widgets = @import("../widgets.zig");
const units = @import("../units.zig");

const PageList = terminal.PageList;

/// PageList inspector widget.
pub const Inspector = struct {
    pub const empty: Inspector = .{};

    pub fn draw(_: *const Inspector, pages: *PageList) void {
        cimgui.c.ImGui_TextWrapped(
            "PageList manages the backing pages that hold scrollback and the active " ++
                "terminal grid. Each page is a contiguous memory buffer with its " ++
                "own rows, cells, style set, grapheme map, and hyperlink storage.",
        );

        if (cimgui.c.ImGui_CollapsingHeader(
            "Overview",
            cimgui.c.ImGuiTreeNodeFlags_DefaultOpen,
        )) {
            summaryTable(pages);
        }

        if (cimgui.c.ImGui_CollapsingHeader(
            "Scrollbar & Regions",
            cimgui.c.ImGuiTreeNodeFlags_DefaultOpen,
        )) {
            cimgui.c.ImGui_SeparatorText("Scrollbar");
            scrollbarInfo(pages);
            cimgui.c.ImGui_SeparatorText("Regions");
            regionsTable(pages);
        }

        if (cimgui.c.ImGui_CollapsingHeader(
            "Tracked Pins",
            cimgui.c.ImGuiTreeNodeFlags_DefaultOpen,
        )) {
            trackedPinsTable(pages);
        }

        if (cimgui.c.ImGui_CollapsingHeader(
            "Pages",
            cimgui.c.ImGuiTreeNodeFlags_DefaultOpen,
        )) {
            widgets.helpMarker(
                "Pages are shown most-recent first. Each page holds a grid of rows/cells " ++
                    "plus metadata tables for styles, graphemes, strings, and hyperlinks.",
            );

            const active_pin = pages.getTopLeft(.active);
            const viewport_pin = pages.getTopLeft(.viewport);

            var row_offset = pages.total_rows;
            var index: usize = pages.totalPages();
            var node = pages.pages.last;
            while (node) |page_node| : (node = page_node.prev) {
                const page = &page_node.data;
                row_offset -= page.size.rows;
                index -= 1;

                // We use our location as the ID so that even if reallocations
                // happen we remain open if we're open already.
                cimgui.c.ImGui_PushIDInt(@intCast(index));
                defer cimgui.c.ImGui_PopID();

                // Open up the tree node.
                if (!widgets.page.treeNode(.{
                    .page = page,
                    .index = index,
                    .row_range = .{ row_offset, row_offset + page.size.rows - 1 },
                    .active = node == active_pin.node,
                    .viewport = node == viewport_pin.node,
                })) continue;
                defer cimgui.c.ImGui_TreePop();
                widgets.page.inspector(page);
            }
        }
    }
};

fn summaryTable(pages: *const PageList) void {
    if (!cimgui.c.ImGui_BeginTable(
        "pagelist_summary",
        3,
        cimgui.c.ImGuiTableFlags_BordersInnerV |
            cimgui.c.ImGuiTableFlags_RowBg |
            cimgui.c.ImGuiTableFlags_SizingFixedFit,
    )) return;
    defer cimgui.c.ImGui_EndTable();

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Active Grid");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Active viewport size in columns x rows.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%dc x %dr", pages.cols, pages.rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Pages");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Total number of pages in the linked list.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%d", pages.totalPages());

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Total Rows");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Total rows represented by scrollback + active area.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%d", pages.total_rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Page Bytes");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Total bytes allocated for active pages.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text(
        "%d KiB",
        units.toKibiBytes(pages.page_size),
    );

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Max Size");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker(
        \\Maximum bytes before pages must be evicated. The total
        \\used bytes may be higher due to minimum individual page
        \\sizes but the next allocation that would exceed this limit
        \\will evict pages from the front of the list to free up space.
    );
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text(
        "%d KiB",
        units.toKibiBytes(pages.maxSize()),
    );

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Viewport");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Current viewport anchoring mode.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%s", @tagName(pages.viewport).ptr);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Tracked Pins");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Number of pins tracked for automatic updates.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%d", pages.countTrackedPins());
}

fn scrollbarInfo(pages: *PageList) void {
    const scrollbar = pages.scrollbar();

    // If we have a scrollbar, show it.
    if (scrollbar.total > 0) {
        var delta_row: isize = 0;
        scrollbarWidget(&scrollbar, &delta_row);
        if (delta_row != 0) {
            pages.scroll(.{ .delta_row = delta_row });
        }
    }

    if (!cimgui.c.ImGui_BeginTable(
        "scrollbar_info",
        3,
        cimgui.c.ImGuiTableFlags_BordersInnerV |
            cimgui.c.ImGuiTableFlags_RowBg |
            cimgui.c.ImGuiTableFlags_SizingFixedFit,
    )) return;
    defer cimgui.c.ImGui_EndTable();

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Total");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Total number of scrollable rows including scrollback and active area.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%d", scrollbar.total);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Offset");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Current scroll position as row offset from the top of scrollback.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%d", scrollbar.offset);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Length");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker("Number of rows visible in the viewport.");
    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    cimgui.c.ImGui_Text("%d", scrollbar.len);
}

fn regionsTable(pages: *PageList) void {
    if (!cimgui.c.ImGui_BeginTable(
        "pagelist_regions",
        4,
        cimgui.c.ImGuiTableFlags_BordersInnerV |
            cimgui.c.ImGuiTableFlags_RowBg |
            cimgui.c.ImGuiTableFlags_SizingFixedFit,
    )) return;
    defer cimgui.c.ImGui_EndTable();

    cimgui.c.ImGui_TableSetupColumn("Region", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableSetupColumn("", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableSetupColumn("Top-Left", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableSetupColumn("Bottom-Right", cimgui.c.ImGuiTableColumnFlags_WidthStretch);
    cimgui.c.ImGui_TableHeadersRow();

    inline for (comptime std.meta.tags(terminal.point.Tag)) |tag| {
        regionRow(pages, tag);
    }
}

fn regionRow(pages: *const PageList, comptime tag: terminal.point.Tag) void {
    const tl_pin = pages.getTopLeft(tag);
    const br_pin = pages.getBottomRight(tag);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("%s", @tagName(tag).ptr);

    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    widgets.helpMarker(comptime regionHelpText(tag));

    _ = cimgui.c.ImGui_TableSetColumnIndex(2);
    if (pages.pointFromPin(tag, tl_pin)) |pt| {
        const coord = pt.coord();
        cimgui.c.ImGui_Text("(%d, %d)", coord.x, coord.y);
    } else {
        cimgui.c.ImGui_TextDisabled("(n/a)");
    }

    _ = cimgui.c.ImGui_TableSetColumnIndex(3);
    if (br_pin) |br| {
        if (pages.pointFromPin(tag, br)) |pt| {
            const coord = pt.coord();
            cimgui.c.ImGui_Text("(%d, %d)", coord.x, coord.y);
        } else {
            cimgui.c.ImGui_TextDisabled("(n/a)");
        }
    } else {
        cimgui.c.ImGui_TextDisabled("(empty)");
    }
}

fn regionHelpText(comptime tag: terminal.point.Tag) [:0]const u8 {
    return switch (tag) {
        .active => "The active area where a running program can jump the cursor " ++
            "and make changes. This is the 'editable' part of the screen. " ++
            "Bottom-right includes the full height of the screen, including " ++
            "rows that may not be written yet.",
        .viewport => "The visible viewport. If the user has scrolled, top-left changes. " ++
            "Bottom-right is the last written row from the top-left.",
        .screen => "Top-left is the furthest back in scrollback history. Bottom-right " ++
            "is the last written row. Unlike 'active', this only contains " ++
            "written rows.",
        .history => "Same top-left as 'screen' but bottom-right is the line just before " ++
            "the top of 'active'. Contains only the scrollback history.",
    };
}

fn trackedPinsTable(pages: *const PageList) void {
    if (!cimgui.c.ImGui_BeginTable(
        "tracked_pins",
        5,
        cimgui.c.ImGuiTableFlags_Borders |
            cimgui.c.ImGuiTableFlags_RowBg |
            cimgui.c.ImGuiTableFlags_SizingFixedFit,
    )) return;
    defer cimgui.c.ImGui_EndTable();

    cimgui.c.ImGui_TableSetupColumn("Index", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableSetupColumn("Pin", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableSetupColumn("Context", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableSetupColumn("Dirty", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableSetupColumn("State", cimgui.c.ImGuiTableColumnFlags_WidthFixed);
    cimgui.c.ImGui_TableHeadersRow();

    const active_pin = pages.getTopLeft(.active);
    const viewport_pin = pages.getTopLeft(.viewport);

    for (pages.trackedPins(), 0..) |tracked, idx| {
        const pin = tracked.*;
        cimgui.c.ImGui_TableNextRow();
        _ = cimgui.c.ImGui_TableSetColumnIndex(0);
        cimgui.c.ImGui_Text("%d", idx);

        _ = cimgui.c.ImGui_TableSetColumnIndex(1);
        if (pin.garbage) {
            cimgui.c.ImGui_TextColored(.{ .x = 1.0, .y = 0.5, .z = 0.3, .w = 1.0 }, "(%d, %d)", pin.x, pin.y);
        } else {
            cimgui.c.ImGui_Text("(%d, %d)", pin.x, pin.y);
        }

        _ = cimgui.c.ImGui_TableSetColumnIndex(2);
        if (pages.pointFromPin(.screen, pin)) |pt| {
            const coord = pt.coord();
            cimgui.c.ImGui_Text(
                "screen (%d, %d)",
                coord.x,
                coord.y,
            );
        } else {
            cimgui.c.ImGui_TextDisabled("screen (out of range)");
        }

        _ = cimgui.c.ImGui_TableSetColumnIndex(3);
        const dirty = pin.isDirty();
        if (dirty) {
            cimgui.c.ImGui_TextColored(.{ .x = 1.0, .y = 0.4, .z = 0.4, .w = 1.0 }, "dirty");
        } else {
            cimgui.c.ImGui_TextDisabled("clean");
        }

        _ = cimgui.c.ImGui_TableSetColumnIndex(4);
        if (pin.eql(active_pin)) {
            cimgui.c.ImGui_TextColored(.{ .x = 0.4, .y = 0.9, .z = 0.4, .w = 1.0 }, "active top");
        } else if (pin.eql(viewport_pin)) {
            cimgui.c.ImGui_TextColored(.{ .x = 0.4, .y = 0.8, .z = 1.0, .w = 1.0 }, "viewport top");
        } else if (pin.garbage) {
            cimgui.c.ImGui_TextColored(.{ .x = 1.0, .y = 0.5, .z = 0.3, .w = 1.0 }, "garbage");
        } else if (tracked == pages.viewport_pin) {
            cimgui.c.ImGui_Text("viewport pin");
        } else {
            cimgui.c.ImGui_TextDisabled("tracked");
        }
    }
}

fn scrollbarWidget(
    scrollbar: *const PageList.Scrollbar,
    delta_row: *isize,
) void {
    delta_row.* = 0;

    const avail_width = cimgui.c.ImGui_GetContentRegionAvail().x;
    const bar_height: f32 = cimgui.c.ImGui_GetFrameHeight();
    const cursor_pos = cimgui.c.ImGui_GetCursorScreenPos();

    const total_f: f32 = @floatFromInt(scrollbar.total);
    const offset_f: f32 = @floatFromInt(scrollbar.offset);
    const len_f: f32 = @floatFromInt(scrollbar.len);

    const grab_start = (offset_f / total_f) * avail_width;
    const grab_width = @max((len_f / total_f) * avail_width, 4.0);

    const draw_list = cimgui.c.ImGui_GetWindowDrawList();
    const bg_color = cimgui.c.ImGui_GetColorU32(cimgui.c.ImGuiCol_ScrollbarBg);
    const grab_color = cimgui.c.ImGui_GetColorU32(cimgui.c.ImGuiCol_ScrollbarGrab);

    const bg_min: cimgui.c.ImVec2 = cursor_pos;
    const bg_max: cimgui.c.ImVec2 = .{ .x = cursor_pos.x + avail_width, .y = cursor_pos.y + bar_height };
    cimgui.c.ImDrawList_AddRectFilledEx(
        draw_list,
        bg_min,
        bg_max,
        bg_color,
        0,
        0,
    );

    const grab_min: cimgui.c.ImVec2 = .{
        .x = cursor_pos.x + grab_start,
        .y = cursor_pos.y,
    };
    const grab_max: cimgui.c.ImVec2 = .{
        .x = cursor_pos.x + grab_start + grab_width,
        .y = cursor_pos.y + bar_height,
    };
    cimgui.c.ImDrawList_AddRectFilledEx(
        draw_list,
        grab_min,
        grab_max,
        grab_color,
        0,
        0,
    );
    _ = cimgui.c.ImGui_InvisibleButton(
        "scrollbar_drag",
        .{ .x = avail_width, .y = bar_height },
        0,
    );
    if (cimgui.c.ImGui_IsItemActive()) {
        const drag_delta = cimgui.c.ImGui_GetMouseDragDelta(
            cimgui.c.ImGuiMouseButton_Left,
            0.0,
        );
        if (drag_delta.x != 0) {
            const row_delta = (drag_delta.x / avail_width) * total_f;
            delta_row.* = @intFromFloat(row_delta);
            cimgui.c.ImGui_ResetMouseDragDelta();
        }
    }

    if (cimgui.c.ImGui_IsItemHovered(cimgui.c.ImGuiHoveredFlags_DelayShort)) {
        cimgui.c.ImGui_SetTooltip(
            "offset=%d len=%d total=%d",
            scrollbar.offset,
            scrollbar.len,
            scrollbar.total,
        );
    }
}
