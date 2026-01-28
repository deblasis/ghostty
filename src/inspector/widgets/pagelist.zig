const std = @import("std");
const cimgui = @import("dcimgui");
const terminal = @import("../../terminal/main.zig");
const widgets = @import("../widgets.zig");
const units = @import("../units.zig");
const page_inspector = @import("../page.zig");

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
                "Pages are linked in scrollback order. Each page holds a grid of rows/cells " ++
                    "plus metadata tables for styles, graphemes, strings, and hyperlinks.",
            );

            const active_pin = pages.getTopLeft(.active);
            const viewport_pin = pages.getTopLeft(.viewport);

            var row_offset: usize = 0;
            var index: usize = 0;
            var node = pages.pages.first;
            while (node) |page_node| : (node = page_node.next) {
                const page = &page_node.data;
                const row_start = row_offset;
                const row_end = row_offset + page.size.rows - 1;
                const stats = pageStats(page);

                row_offset += page.size.rows;

                cimgui.c.ImGui_PushIDInt(@intCast(index));
                defer cimgui.c.ImGui_PopID();

                const header_state = pageHeaderRow(
                    index,
                    page,
                    page_node,
                    row_start,
                    row_end,
                    active_pin,
                    viewport_pin,
                    stats,
                );

                if (header_state.open) {
                    pageMetaTable(page_node, row_start, row_end, active_pin, viewport_pin, stats);
                    cimgui.c.ImGui_Separator();
                    page_inspector.render(page);
                    cimgui.c.ImGui_Separator();
                    contentStatsTable(stats);
                    cimgui.c.ImGui_TreePop();
                }

                index += 1;
            }
        }
    }
};

const PageStats = struct {
    rows_with_text: usize = 0,
    cells_with_text: usize = 0,
    dirty_rows: usize = 0,
    wrap_rows: usize = 0,
    wrap_cont_rows: usize = 0,
    styled_rows: usize = 0,
    grapheme_rows: usize = 0,
    hyperlink_rows: usize = 0,
    first_text_row: ?usize = null,
    last_text_row: ?usize = null,
    hyperlink_cells: usize = 0,
    styled_cells: usize = 0,
    grapheme_cells: usize = 0,
};

fn pageStats(page: *const terminal.Page) PageStats {
    var stats: PageStats = .{};
    const rows = page.rows.ptr(page.memory)[0..page.size.rows];
    for (rows, 0..) |*row, row_index| {
        if (row.dirty) stats.dirty_rows += 1;
        if (row.wrap) stats.wrap_rows += 1;
        if (row.wrap_continuation) stats.wrap_cont_rows += 1;
        if (row.styled) stats.styled_rows += 1;
        if (row.grapheme) stats.grapheme_rows += 1;
        if (row.hyperlink) stats.hyperlink_rows += 1;

        const cells = page.getCells(row);
        var row_cells_with_text: usize = 0;
        for (cells) |cell| {
            if (cell.hasText()) row_cells_with_text += 1;
            if (cell.hasStyling()) stats.styled_cells += 1;
            if (cell.hasGrapheme()) stats.grapheme_cells += 1;
            if (cell.hyperlink) stats.hyperlink_cells += 1;
        }

        if (row_cells_with_text > 0) {
            stats.rows_with_text += 1;
            stats.cells_with_text += row_cells_with_text;
            if (stats.first_text_row == null) stats.first_text_row = row_index;
            stats.last_text_row = row_index;
        }
    }

    return stats;
}

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

fn pageMetaTable(
    node: *const PageList.List.Node,
    row_start: usize,
    row_end: usize,
    active_pin: terminal.Pin,
    viewport_pin: terminal.Pin,
    stats: PageStats,
) void {
    if (!cimgui.c.ImGui_BeginTable(
        "page_meta",
        2,
        cimgui.c.ImGuiTableFlags_BordersInnerV |
            cimgui.c.ImGuiTableFlags_RowBg |
            cimgui.c.ImGuiTableFlags_SizingFixedFit,
    )) return;
    defer cimgui.c.ImGui_EndTable();

    const page = &node.data;

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Row Range");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d..%d", row_start, row_end);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Serial");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", node.serial);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Dirty");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%s", if (page.isDirty()) "true".ptr else "false".ptr);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Active Top");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%s", if (node == active_pin.node) "true".ptr else "false".ptr);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Viewport Top");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%s", if (node == viewport_pin.node) "true".ptr else "false".ptr);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Links");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text(
        "%d map / %d set",
        page.hyperlink_map.map(page.memory).count(),
        page.hyperlink_set.count(),
    );
    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Text Coverage");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d/%d rows", stats.rows_with_text, page.size.rows);
}

fn contentStatsTable(stats: PageStats) void {
    if (!cimgui.c.ImGui_BeginTable(
        "page_content_stats",
        2,
        cimgui.c.ImGuiTableFlags_BordersInnerV |
            cimgui.c.ImGuiTableFlags_RowBg |
            cimgui.c.ImGuiTableFlags_SizingFixedFit,
    )) return;
    defer cimgui.c.ImGui_EndTable();

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Rows w/ Text");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.rows_with_text);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Cells w/ Text");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.cells_with_text);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Text Row Range");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    if (stats.first_text_row) |first| {
        cimgui.c.ImGui_Text("%d..%d", first, stats.last_text_row.?);
    } else {
        cimgui.c.ImGui_TextDisabled("(none)");
    }

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Dirty Rows");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.dirty_rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Wrap Rows");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.wrap_rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Wrap Continuations");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.wrap_cont_rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Styled Rows");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.styled_rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Styled Cells");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.styled_cells);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Grapheme Rows");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.grapheme_rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Grapheme Cells");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.grapheme_cells);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Hyperlink Rows");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.hyperlink_rows);

    cimgui.c.ImGui_TableNextRow();
    _ = cimgui.c.ImGui_TableSetColumnIndex(0);
    cimgui.c.ImGui_Text("Hyperlink Cells");
    _ = cimgui.c.ImGui_TableSetColumnIndex(1);
    cimgui.c.ImGui_Text("%d", stats.hyperlink_cells);
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

const PageHeaderState = struct {
    open: bool,
};

fn pageHeaderRow(
    index: usize,
    page: *const terminal.Page,
    page_node: *const PageList.List.Node,
    row_start: usize,
    row_end: usize,
    active_pin: terminal.Pin,
    viewport_pin: terminal.Pin,
    stats: PageStats,
) PageHeaderState {
    var label_buf: [160]u8 = undefined;
    const label = std.fmt.bufPrintZ(
        &label_buf,
        "Page {d}",
        .{index},
    ) catch "Page";

    const flags = cimgui.c.ImGuiTreeNodeFlags_AllowOverlap |
        cimgui.c.ImGuiTreeNodeFlags_SpanFullWidth |
        cimgui.c.ImGuiTreeNodeFlags_FramePadding;
    const open = cimgui.c.ImGui_TreeNodeEx(label.ptr, flags);

    const header_min = cimgui.c.ImGui_GetItemRectMin();
    const header_max = cimgui.c.ImGui_GetItemRectMax();
    const header_height = header_max.y - header_min.y;
    const text_line = cimgui.c.ImGui_GetTextLineHeight();
    const y_center = header_min.y + (header_height - text_line) * 0.5;

    cimgui.c.ImGui_SetCursorScreenPos(.{ .x = header_min.x + 170, .y = y_center });
    cimgui.c.ImGui_TextDisabled("%dc x %dr", page.size.cols, page.size.rows);

    cimgui.c.ImGui_SameLine();
    cimgui.c.ImGui_Text("rows %d..%d", row_start, row_end);

    if (page_node == active_pin.node) {
        cimgui.c.ImGui_SameLine();
        cimgui.c.ImGui_TextColored(.{ .x = 0.4, .y = 0.9, .z = 0.4, .w = 1.0 }, "active");
    }
    if (page_node == viewport_pin.node) {
        cimgui.c.ImGui_SameLine();
        cimgui.c.ImGui_TextColored(.{ .x = 0.4, .y = 0.8, .z = 1.0, .w = 1.0 }, "viewport");
    }
    if (page.isDirty()) {
        cimgui.c.ImGui_SameLine();
        cimgui.c.ImGui_TextColored(.{ .x = 1.0, .y = 0.4, .z = 0.4, .w = 1.0 }, "dirty");
    }

    const coverage = if (page.size.rows > 0)
        @as(f32, @floatFromInt(stats.rows_with_text)) /
            @as(f32, @floatFromInt(page.size.rows))
    else
        0.0;

    const bar_width: f32 = 140;
    const bar_height: f32 = 0;
    cimgui.c.ImGui_SetCursorScreenPos(.{ .x = header_max.x - bar_width - 10, .y = y_center });
    cimgui.c.ImGui_ProgressBar(coverage, .{ .x = bar_width, .y = bar_height }, null);
    if (cimgui.c.ImGui_IsItemHovered(cimgui.c.ImGuiHoveredFlags_DelayShort)) {
        cimgui.c.ImGui_SetTooltip("Text coverage: %d/%d rows", stats.rows_with_text, page.size.rows);
    }

    return .{ .open = open };
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
