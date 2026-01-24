# DataTables Implementation Guide - XtremeIdiots Portal

## Overview

This guide documents the standardized approach for implementing responsive tables across the XtremeIdiots Portal. All tables use DataTables with responsive inline details to provide consistent mobile behavior.

## Table Implementation Patterns

### 1. DataTable-AJAX (Server-Side Processing)

**Use When:**
- Large datasets (1000+ records)
- Dynamic filtering/sorting required on server
- Real-time data updates needed

**Pattern:**

**View (Razor):**
```html
<div class="ibox">
    <div class="ibox-content">
        <div class="table-responsive">
            <table id="dataTable" class="table table-striped table-hover w-100" 
                   data-source="/Controller/GetDataAjax">
                <thead>
                    <tr>
                        <th scope="col">Column 1</th>
                        <th scope="col">Column 2</th>
                        <th scope="col">Column 3</th>
                    </tr>
                </thead>
            </table>
        </div>
    </div>
</div>

@section Scripts {
    <script src="~/js/my-table.js" asp-append-version="true"></script>
}
```

**JavaScript (my-table.js):**
```javascript
$(document).ready(function () {
    const tableEl = $('#dataTable');
    const dataUrl = tableEl.data('source');

    const table = tableEl.DataTable({
        processing: true,
        serverSide: true,
        searchDelay: 800,
        stateSave: true,
        responsive: { 
            details: { 
                type: 'inline',  // Show collapsed columns inline
                target: 'tr'     // Click/tap entire row to expand
            } 
        },
        autoWidth: false,
        order: [[0, 'desc']],
        columnDefs: [
            { targets: 0, responsivePriority: 1 }, // Most important - always visible
            { targets: 1, responsivePriority: 2 }, // High priority
            { targets: 2, responsivePriority: 3 }, // Medium priority
            { targets: 3, responsivePriority: 4 }, // Lower priority
            { targets: 4, responsivePriority: 5 }  // Least important - collapses first
        ],
        ajax: {
            url: dataUrl,
            dataSrc: 'data',
            contentType: 'application/json',
            type: 'POST',
            data: function (d) { return JSON.stringify(d); },
            beforeSend: function (xhr) {
                const token = document.querySelector('input[name="__RequestVerificationToken"]');
                if (token) xhr.setRequestHeader('RequestVerificationToken', token.value);
            }
        },
        columns: [
            { data: 'column1', name: 'column1', sortable: true },
            { data: 'column2', name: 'column2', sortable: true },
            { data: 'column3', name: 'column3', sortable: false }
        ]
    });
});
```

**Examples:**
- `Players/Index.cshtml` + `players-index.js`
- `AdminActions/Global.cshtml` + `admin-actions-global.js`
- `Demos/Index.cshtml` + `demos-index.js`

---

### 2. DataTable-Static (Client-Side Processing)

**Use When:**
- Small to medium datasets (<500 records)
- Data rendered by server (Razor foreach loop)
- No real-time updates needed
- Simple filtering/sorting sufficient

**Pattern:**

**View (Razor):**
```html
<div class="ibox">
    <div class="ibox-content">
        <div class="table-responsive">
            <table id="myStaticTable" class="table table-striped table-hover w-100">
                <thead>
                    <tr>
                        <th>Column 1</th>
                        <th>Column 2</th>
                        <th>Column 3</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var item in Model.Items)
                    {
                        <tr>
                            <td>@item.Property1</td>
                            <td>@item.Property2</td>
                            <td>@item.Property3</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>

@section Scripts {
    <script src="~/js/my-static-table.js" asp-append-version="true"></script>
}
```

**JavaScript (my-static-table.js):**
```javascript
$(document).ready(function () {
    const myTable = $('#myStaticTable');
    
    // Only initialize if table exists and has data
    if (myTable.length && myTable.find('tbody tr').length > 0) {
        myTable.DataTable({
            responsive: {
                details: {
                    type: 'inline',
                    target: 'tr'
                }
            },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            order: [[1, 'desc']], // Sort by column 2 descending
            columnDefs: [
                { targets: 0, responsivePriority: 1 }, // Most important
                { targets: 1, responsivePriority: 2 }, // High priority
                { targets: 2, responsivePriority: 3 }  // Lower priority
            ],
            language: {
                search: '<i class="fa fa-search" aria-hidden="true"></i>',
                lengthMenu: 'Show _MENU_ entries',
                info: 'Showing _START_ to _END_ of _TOTAL_ entries',
                emptyTable: 'No records found'
            }
        });
    }
});
```

**Examples:**
- `Players/Details.cshtml` (Aliases, IPs, Protected Names) + `player-details-tables.js`
- `ProtectedNames/Index.cshtml` + `protected-names-table.js`

---

### 3. Shared Table Partials

**Use When:**
- Same table structure appears in multiple views
- Only context (data source, title) differs
- Want single source of truth for markup

**Pattern:**

**Partial View (_MySharedTable.cshtml):**
```csharp
@model IEnumerable<MyDto>

@{
    var tableId = ViewData["TableId"] as string ?? "dataTable";
    var title = ViewData["TableTitle"] as string ?? "Data";
}

<div class="table-responsive">
    <table id="@tableId" class="table table-striped table-hover w-100">
        <thead>
            <tr>
                <th>Column 1</th>
                <th>Column 2</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var item in Model)
            {
                <tr>
                    <td>@item.Property1</td>
                    <td>@item.Property2</td>
                </tr>
            }
        </tbody>
    </table>
</div>
```

**Usage in Parent View:**
```csharp
@{
    ViewData["TableId"] = "myTable";
    ViewData["TableTitle"] = "My Data";
}
<partial name="_MySharedTable" model="Model.Items" />

@section Scripts {
    <script src="~/js/my-table.js" asp-append-version="true"></script>
}
```

**Examples:**
- `_ChatLogTable.cshtml` - Used by ServerAdmin/ChatLogIndex, Players/Details
- `_ProtectedNamesTable.cshtml` - Used by ProtectedNames/Index, Players/ProtectedNames
- `_ProtectedNameUsageTable.cshtml` - Used by ProtectedNames/Report, Players/ProtectedNameReport

---

## Column Priority System

### Priority Levels (1-6)

| Priority | Usage | Visibility |
|----------|-------|------------|
| **1** | Primary/key columns (Name, ID, Timestamp) | Always visible when possible |
| **2** | Critical data (Actions, Status, Type) | Visible on mobile landscape+ |
| **3** | Important data (Player, Message, Count) | Hidden on small mobile |
| **4** | Secondary data (Added, First Seen, Server) | Hidden on mobile |
| **5** | Tertiary data (Game Type, Created By) | Hidden on tablet and below |
| **6** | Metadata (Modified, Link) | Hidden on desktop-small and below |

### Priority Guidelines

**Always Priority 1:**
- Primary identifier (Username, Name, ID)
- Timestamp (when it's the primary sort)

**Always Priority 2:**
- Actions column (Edit, Delete, View buttons)
- Most recent timestamp (Last Seen, Last Used)
- Critical status (Type, Status)

**Priority 3-4:**
- Descriptive text (Message, Reason)
- Secondary identifiers (GUID, Player)
- Dates (Created, Added, First Seen)

**Priority 5-6:**
- Game Type (when other columns show game context)
- Creator/modifier metadata
- Less frequently accessed data

---

## File Structure

```
XtremeIdiots.Portal.Web/
├── Views/
│   ├── {Controller}/
│   │   ├── Index.cshtml           # Main list view with DataTable
│   │   ├── Details.cshtml         # Detail view, may have related tables
│   │   └── {Action}.cshtml        # Other views
│   ├── Shared/
│   │   ├── _ChatLogTable.cshtml         # Shared: ChatLog table
│   │   ├── _ProtectedNamesTable.cshtml  # Shared: Protected names list
│   │   └── _ProtectedNameUsageTable.cshtml # Shared: Usage report
│   └── _Layout.cshtml             # Global layout with responsive-tables.css
├── wwwroot/
│   ├── css/
│   │   ├── responsive-tables.css  # Global table mobile optimizations
│   │   ├── admin-actions.css      # Admin-specific table styling
│   │   └── {page}-index.css       # Page-specific overrides
│   └── js/
│       ├── {page}-index.js        # DataTable-AJAX initialization
│       ├── {page}-details-tables.js # DataTable-Static initialization
│       └── {component}-table.js    # Shared partial table initialization
```

### Naming Conventions

**JavaScript Files:**
- `{page}-index.js` - For AJAX DataTables on index/list pages
- `{page}-details-tables.js` - For static DataTables on detail pages
- `{component}-table.js` - For shared partial table initialization

**CSS Files:**
- `responsive-tables.css` - Global table responsive styles
- `{page}-index.css` - Page-specific table styling

**Partial Views:**
- `_PascalCaseTable.cshtml` - Shared table components

**Table IDs:**
- `#dataTable` - Default for single table per page
- `#{specific}Table` - When multiple tables (e.g., `#aliasesTable`, `#ipAddressesTable`)

---

## CSS Architecture

### Global Styles (responsive-tables.css)

Applied to ALL tables automatically:

```css
/* Mobile-first spacing (max-width: 767.98px) */
- Wrapper padding: 10px 8px
- ibox padding: 0.5rem
- Table cell padding: 0.35rem vertical, 0.5rem horizontal
- Header font: 0.85rem
- Compact filters and pagination controls

/* DataTables inline details styling */
- Primary color expand indicators
- Clean child row layout
- Clear visual hierarchy

/* Tablet (768px-991px) */
- Medium spacing adjustments

/* Desktop (992px+) */
- Standard spacing
- Full feature display
```

### Page-Specific Overrides

Create when page needs custom styling:

```css
/* players-index.css */
#dataTable tbody td {
    padding-top: .4rem;
    padding-bottom: .35rem;
}

@media (max-width: 767.98px) {
    #dataTable tbody td {
        padding-top: .3rem;
        padding-bottom: .3rem;
    }
}
```

---

## Implementation Checklist

### For New AJAX DataTables

- [ ] Create `{page}-index.js` with serverSide: true
- [ ] Add `responsive: { details: { type: 'inline', target: 'tr' } }`
- [ ] Define column priorities for ALL columns (1-6)
- [ ] Set appropriate default sort order
- [ ] Include anti-forgery token in beforeSend
- [ ] Add `table-responsive` wrapper in view
- [ ] Use standard table classes: `table table-striped table-hover w-100`
- [ ] Include script reference in `@section Scripts`

### For New Static DataTables

- [ ] Ensure table has unique ID
- [ ] Create `{page}-tables.js` with client-side config
- [ ] Add `responsive: { details: { type: 'inline', target: 'tr' } }`
- [ ] Define column priorities for ALL columns
- [ ] Set pageLength appropriate for data volume (10-25)
- [ ] Check for empty table before initializing
- [ ] Add `table-responsive` wrapper in view
- [ ] Use standard table classes: `table table-striped table-hover w-100`
- [ ] Include script reference in `@section Scripts`

### For Shared Table Partials

- [ ] Create partial in `Views/Shared/_ComponentTable.cshtml`
- [ ] Accept model as `IEnumerable<Dto>`
- [ ] Use ViewData for configuration (tableId, title, etc.)
- [ ] Create corresponding `{component}-table.js`
- [ ] Document usage in partial header comments
- [ ] Update parent views to use partial
- [ ] Ensure script is included in parent views

---

## Current Implementation Status

### ✅ Fully Implemented (DataTable-AJAX)

| View | Script | Responsive | Priorities | Status |
|------|--------|-----------|-----------|--------|
| Players/Index | players-index.js | ✅ | ✅ (5 cols) | Complete |
| AdminActions/Global | admin-actions-global.js | ✅ | ✅ (6 cols) | Complete |
| AdminActions/Unclaimed | admin-actions-unclaimed.js | ✅ | ✅ (6 cols) | Complete |
| AdminActions/MyActions | admin-actions-my.js | ✅ | ✅ (4 cols) | Complete |
| Demos/Index | demos-index.js | ✅ | ✅ (10 cols) | Complete |
| Maps/Index | maps-index.js | ✅ | ✅ (5 cols) | Complete |
| User/Index | user-index.js | ✅ | ✅ (5 cols) | Complete |
| ServerAdmin/ChatLogIndex | chatlog-index.js | ✅ | ✅ (6-7 cols) | Complete |
| ServerAdmin/ViewRcon | (inline JS) | ✅ | ✅ (6 cols) | Complete |

### ✅ Fully Implemented (DataTable-Static)

| View | Script | Responsive | Priorities | Status |
|------|--------|-----------|-----------|--------|
| Players/Details (3 tables) | player-details-tables.js | ✅ | ✅ (4 cols each) | Complete |
| ProtectedNames/Index | protected-names-table.js | ✅ | ✅ (5 cols) | Complete |
| Players/ProtectedNames | protected-names-table.js | ✅ | ✅ (5 cols) | Complete |
| ProtectedNames/Report | protected-name-usage-report.js | ✅ | ✅ (4 cols) | Complete |
| Players/ProtectedNameReport | protected-name-usage-report.js | ✅ | ✅ (4 cols) | Complete |

### ✅ Shared Partials

| Partial | Model Type | Used By | Script | Status |
|---------|-----------|---------|--------|--------|
| _ChatLogTable | N/A (AJAX) | ChatLogIndex, Players/Details | chatlog-index.js | Complete |
| _ProtectedNamesTable | IEnumerable&lt;ProtectedNameDto&gt; | ProtectedNames/Index, Players/ProtectedNames | protected-names-table.js | Complete |
| _ProtectedNameUsageTable | IEnumerable&lt;ProtectedNameUsageDto&gt; | ProtectedNames/Report, Players/ProtectedNameReport | protected-name-usage-report.js | Complete |

### ⚠️ Static HTML Tables (Should Convert or Document)

| View | Table Content | Recommendation |
|------|--------------|----------------|
| Servers/Index | Server list with status | Convert to DataTable-Static |
| Servers/ServerInfo | Players list, Map rotation | Convert players to DataTable-Static |
| GameServers/Index | Server management list | Convert to DataTable-AJAX |
| GameServers/Details | Ban file monitors | Convert to DataTable-Static |
| Credentials/Index | Server credentials | Keep static (security sensitive) |
| MapManager/Manage | Map packs, rotation, remote | Convert to DataTable-Static |
| Profile/Manage | Profile info, claims | Keep static (small, readonly) |
| BanFileMonitors/Index | Monitor list | Convert to DataTable-AJAX |
| Tags/Index | Tag list | Convert to DataTable-Static |
| IPAddresses/Details | Players using IP | Convert to DataTable-Static |
| User/ManageProfile | Permissions table | Keep static (readonly) |

---

## Configuration Best Practices

### Responsive Configuration

**Always use inline details:**
```javascript
responsive: {
    details: {
        type: 'inline',    // Not 'column' - shows in same row
        target: 'tr'       // Entire row is clickable
    }
}
```

**Why inline?**
- More intuitive on mobile (tap row to expand)
- No extra column for expand icon
- Cleaner visual presentation
- Consistent with Players/Index reference

### Column Priority Assignment

**Start with most important:**
```javascript
columnDefs: [
    { targets: 0, responsivePriority: 1 }, // Primary identifier
    { targets: 4, responsivePriority: 2 }, // Actions or latest timestamp
    { targets: 1, responsivePriority: 3 }, // Important context
    { targets: 2, responsivePriority: 4 }, // Secondary data
    { targets: 3, responsivePriority: 5 }  // Metadata
]
```

**Considerations:**
- Actions column: Priority 2 (need to stay accessible)
- Timestamps: Latest = Priority 2, others = Priority 4-5
- Game Type: Priority 5-6 (visible in Name column icon)
- Creator metadata: Priority 5-6 (less frequently needed)

### State Persistence

**For AJAX tables:**
```javascript
stateSave: true,
stateSaveParams: function (settings, data) {
    data._structureVersion = 1; // Bump when changing columns
    data.customFilter = $('#filter').val();
},
stateLoadParams: function (settings, data) {
    if (data._structureVersion !== 1) return false; // Invalidate old state
    $('#filter').val(data.customFilter);
}
```

**Why structure versioning?**
- Prevents errors when columns change
- Invalidates incompatible saved state
- User gets fresh table instead of broken state

---

## Mobile Optimization Techniques

### 1. Spacing Reduction (responsive-tables.css)

Applied globally on mobile breakpoint (`max-width: 767.98px`):
- Wrapper: 10px vertical, 8px horizontal (50% reduction)
- Table cells: 0.35rem vertical (30% reduction)
- Headers: 0.85rem font size (15% reduction)
- Result: **15-20% more visible content**

### 2. Inline Details Styling

Custom styling for expanded rows:
```css
table.dataTable.dtr-inline.collapsed > tbody > tr > td:first-child:before {
    background-color: var(--xi-primary);
    border: 2px solid white;
}

table.dataTable > tbody > tr.child ul.dtr-details > li {
    border-bottom: 1px solid #efefef;
    padding: 0.5rem 0;
}
```

### 3. Touch Targets

Maintain minimum 44px touch target size:
- Buttons: `btn-sm` class still meets minimum
- Expand indicators: Sized appropriately for touch
- Row height: Adequate padding for tap accuracy

### 4. Font Scaling

Progressive enhancement:
- Desktop: Standard sizes (1rem, 0.875rem)
- Tablet: Slightly reduced
- Mobile: Optimized for readability (0.85rem headers, 0.875rem content)

---

## Common Patterns

### Filter Bar Integration

Reusable filter bar with DataTables:
```html
<div class="admin-actions-filters mb-2" id="myFilters">
    <div class="filter-group">
        <label for="filterOption" class="form-label">Filter</label>
        <select id="filterOption" class="form-select">
            <option value="">All</option>
        </select>
    </div>
    <div class="filter-group">
        <label class="form-label" for="resetFilters">Reset</label>
        <button type="button" id="resetFilters" class="btn btn-outline-secondary btn-sm">
            Clear Filters
        </button>
    </div>
</div>
```

Handled in JavaScript:
```javascript
$('#filterOption').on('change', function() {
    table.ajax.reload();
});

$('#resetFilters').on('click', function() {
    $('#filterOption').val('');
    table.search('').draw();
});
```

### Empty State Handling

**For static tables:**
```javascript
if (myTable.length && myTable.find('tbody tr').length > 0) {
    myTable.DataTable({ /* config */ });
}
```

**For AJAX tables:**
Handled automatically by DataTables with `language.emptyTable` setting.

### Tab Navigation

When DataTables are in tabs, recalculate on tab show:
```javascript
$('a[data-bs-toggle="tab"]').on('shown.bs.tab', function (e) {
    if (e.target.id === 'myTab-tab') {
        var table = $('#myTable').DataTable();
        table.columns.adjust();
        if (table.responsive) table.responsive.recalc();
    }
});
```

---

## Testing Checklist

### Development Testing

- [ ] Test on Chrome DevTools device emulation
- [ ] Verify column collapse at different widths
- [ ] Test tap/click to expand on mobile viewport
- [ ] Check pagination works correctly
- [ ] Verify search/filter functionality
- [ ] Test with empty data (no records)
- [ ] Validate sorting on all sortable columns

### Viewport Sizes

- [ ] Mobile Portrait (320px-480px)
- [ ] Mobile Landscape (481px-767px)
- [ ] Tablet (768px-991px)
- [ ] Desktop (992px+)

### Responsive Behavior

- [ ] Only priority 1-2 columns visible on small mobile
- [ ] Priority 3 columns appear on mobile landscape
- [ ] Priority 4-5 columns appear on tablet
- [ ] All columns visible on desktop
- [ ] Expand icon appears when columns are hidden
- [ ] Clicking row expands inline details
- [ ] Inline details show all hidden column data

---

## Troubleshooting

### Issue: Columns not collapsing

**Check:**
1. `responsive: { details: { type: 'inline', target: 'tr' } }` is present
2. `autoWidth: false` is set
3. Column priorities are defined for ALL columns
4. DataTables Responsive CSS is loaded: `datatables-responsive/responsive.dataTables.min.css`

### Issue: Table not initializing

**Check:**
1. Table has unique ID
2. ID matches JavaScript selector
3. Table has `<thead>` and `<tbody>` structure
4. For static tables: Check tbody has rows before init
5. JavaScript file is included in `@section Scripts`

### Issue: Inline details not showing

**Check:**
1. Using `type: 'inline'` not `type: 'column'`
2. DataTables Responsive extension is loaded
3. Columns have priorities set
4. Browser console for JavaScript errors

### Issue: Horizontal scrolling still present

**Check:**
1. `table-responsive` wrapper div is present
2. Table has `w-100` class
3. `autoWidth: false` in DataTables config
4. No fixed pixel widths on columns for mobile breakpoints

---

## Examples from Codebase

### Reference Implementation
**Players/Index.cshtml + players-index.js**
- Perfect example of AJAX DataTables
- Full responsive config
- Column priorities optimized
- Filter bar integration
- State persistence

### Shared Partial Example
**_ChatLogTable.cshtml + chatlog-index.js**
- Reusable across multiple contexts
- ViewData for configuration
- Used by ServerAdmin and Players views

### Static DataTable Example
**Players/Details.cshtml + player-details-tables.js**
- 3 tables initialized from server-rendered data
- Responsive inline details
- Pagination for better UX
- Individual table IDs

---

## Migration Guide

### Converting Static HTML Table to DataTable-Static

**Before:**
```html
<table class="table">
    <thead><tr><th>Name</th><th>Value</th></tr></thead>
    <tbody>
        @foreach (var item in Model.Items)
        {
            <tr>
                <td>@item.Name</td>
                <td>@item.Value</td>
            </tr>
        }
    </tbody>
</table>
```

**After:**
```html
<div class="table-responsive">
    <table id="myTable" class="table table-striped table-hover w-100">
        <thead><tr><th>Name</th><th>Value</th></tr></thead>
        <tbody>
            @foreach (var item in Model.Items)
            {
                <tr>
                    <td>@item.Name</td>
                    <td>@item.Value</td>
                </tr>
            }
        </tbody>
    </table>
</div>

@section Scripts {
    <script src="~/js/my-table.js" asp-append-version="true"></script>
}
```

**my-table.js:**
```javascript
$(document).ready(function () {
    const table = $('#myTable');
    if (table.length && table.find('tbody tr').length > 0) {
        table.DataTable({
            responsive: { details: { type: 'inline', target: 'tr' } },
            autoWidth: false,
            paging: true,
            pageLength: 25,
            columnDefs: [
                { targets: 0, responsivePriority: 1 },
                { targets: 1, responsivePriority: 2 }
            ]
        });
    }
});
```

---

## Summary

### Key Principles

1. **Consistent Behavior:** All tables work the same way
2. **Mobile-First:** Optimized spacing and touch targets
3. **Progressive Enhancement:** More features on larger screens
4. **Single Source of Truth:** Shared partials for reusable patterns
5. **Column Priorities:** Intelligent collapsing based on importance

### File Organization

- **Views/{Controller}/**: Page-specific views with tables
- **Views/Shared/_*Table.cshtml**: Reusable table partials
- **wwwroot/js/{page}-*.js**: DataTable initialization scripts
- **wwwroot/css/responsive-tables.css**: Global table styles

### Standards

- ✅ Bootstrap 5 table classes
- ✅ DataTables responsive extension
- ✅ Column priority system (1-6)
- ✅ Inline expandable details
- ✅ Mobile-optimized spacing
- ✅ Shared partials for reusable patterns

---

## Quick Reference

**Import in view:**
```html
<div class="table-responsive">
    <table id="myTable" class="table table-striped table-hover w-100">
```

**Initialize in JavaScript:**
```javascript
$('#myTable').DataTable({
    responsive: { details: { type: 'inline', target: 'tr' } },
    columnDefs: [
        { targets: 0, responsivePriority: 1 },
        // ... all columns
    ]
});
```

**Use shared partial:**
```csharp
@{ ViewData["ConfigKey"] = value; }
<partial name="_ComponentTable" model="Model.Data" />
```

---

**Document Version:** 1.0  
**Last Updated:** 2026-01-24  
**Maintained By:** Portal Development Team
