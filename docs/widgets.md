# Widget reference

<!-- Generated from the widget descriptors: ImpiousBonum.exe --widget-docs docs/widgets.md. Don't edit by hand. -->

Every widget in a layout file has `type`, `x`, `y`, `width` and `height` (in canvas units), plus the settings below.
Anything left out uses its default.

Templates are text with live values: `{metric.id}` or `{metric.id:spec}`, where spec can be a number format (`0.0`, `N0`),
a byte unit (`MB`, `GB`, …) or `nounit`. Colours are `foreground`, `secondary`, `accent`, `warning`, `critical`
or `#RRGGBB` / `#AARRGGBB`; the named ones come from the layout's theme.

Widgets with a `thresholds` list change colour when a value crosses a limit, e.g.
`"thresholds": [{ "above": 80, "color": "warning" }, { "above": 90, "color": "critical" }]`.
Each rule checks its `metric` (empty means the widget's own) against `above` and/or `below`. When several rules match,
the last one in the list wins, so list them mildest first. A metric with no reading keeps the normal colour.

## Text (`text`)

A line of text with live values, e.g. "{gpu.load}" or "{cpu.temp:nounit} °C".

| Setting | Type | Default | Description |
|---|---|---|---|
| `text` | template | `{cpu.load}` | Text |
| `fontSize` | number (6–400) | `48` | Font size |
| `align` | `left` / `center` / `right` | `left` | Alignment |
| `uppercase` | true/false | `false` | Uppercase |
| `color` | color | `foreground` | Colour |
| `thresholds` | list | — | Warning colours. Changes the text colour. An empty metric uses the first metric in the text. When several rules match, the last one wins, so list them mildest first. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## Graph (`graph`)

A label and live value above a scrolling history of one metric, drawn as a line or a filled area.

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `CPU` | Label |
| `metric` | metric | `cpu.load` | Metric |
| `text` | template | — | Value text. Shown on the right of the header. Empty shows the metric's value. |
| `style` | `line` / `area` | `line` | Style |
| `points` | number (2–240) | `120` | History (seconds) |
| `min` | number | `0` | Minimum |
| `max` | number | — | Maximum. Empty uses the metric's natural maximum (100 for percentages, the total for memory), else scales to fit. |
| `lineThickness` | number (0.5–20) | `2.5` | Line thickness |
| `fontSize` | number (6–200) | `26` | Font size |
| `thresholds` | list | — | Warning colours. Changes the line or area and the value. An empty metric uses the graph's metric. When several rules match, the last one wins, so list them mildest first. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## Icon and value (`icon`)

An accent-coloured line icon followed by a live value, e.g. a chip and the CPU temperature.

| Setting | Type | Default | Description |
|---|---|---|---|
| `icon` | `cpu` / `gpu` / `ram` / `disk` / `network` / `fps` | `cpu` | Icon |
| `text` | template | `{cpu.temp:nounit} °C` | Text |
| `fontSize` | number (6–400) | `64` | Font size |
| `iconSize` | number (8–400) | `60` | Icon size |
| `iconStroke` | number (0.5–6) | `2` | Icon line width. On the icon's 24×24 grid. |
| `gap` | number (0–400) | `50` | Gap after icon |
| `thresholds` | list | — | Warning colours. Changes the value's colour. An empty metric uses the first metric in the text. When several rules match, the last one wins, so list them mildest first. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## Clock (`clock`)

The time with the date underneath, in your Windows language and region.

| Setting | Type | Default | Description |
|---|---|---|---|
| `timeFormat` | text | `HH:mm` | Time format. .NET date/time format, e.g. HH:mm, h:mm tt, dddd d MMMM. |
| `dateFormat` | text | `dddd, d MMMM yyyy` | Date format. .NET date/time format, e.g. HH:mm, h:mm tt, dddd d MMMM. Empty hides the date. |
| `timeSize` | number (6–600) | `150` | Time size |
| `dateSize` | number (6–200) | `30` | Date size |
| `uppercaseDate` | true/false | `true` | Uppercase date |
| `align` | `left` / `center` / `right` | `center` | Alignment |

## Drives (`drives`)

One row per drive with free space and a bar showing how full it is. Follows drives as they are plugged in or removed.

| Setting | Type | Default | Description |
|---|---|---|---|
| `drives` | text | `all` | Drives. "all", or letters separated by commas, e.g. "C,D". |
| `label` | text | `{letter}:/` | Row label. {letter} is replaced with the drive letter. |
| `text` | text | `{free} free / {total}` | Row value. Placeholders: {free}, {used}, {total}, {label}. |
| `fontSize` | number (6–200) | `25` | Font size |
| `barHeight` | number (1–100) | `5` | Bar height |
| `spacing` | number (0–200) | `12` | Row spacing |
| `thresholds` | list | — | Warning colours. Changes each drive's bar. An empty metric uses that drive's used percentage (disk.C.usedPct for C:). When several rules match, the last one wins, so list them mildest first. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## Rows (`rows`)

A stack of label and value rows, e.g. Download / Upload / Ping.

| Setting | Type | Default | Description |
|---|---|---|---|
| `rows` | list | — | Rows |
| `fontSize` | number (6–200) | `28` | Label size |
| `valueFontSize` | number (6–200) | — | Value size. Empty uses 85% of the label size. |
| `align` | `top` / `bottom` | `top` | Stack from |

Each entry in `rows`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `Download` | Label |
| `text` | template | `{net.down}` | Value |

## Theme

The layout's `theme` object sets the font and colours every widget uses, and the canvas background.
A `backgroundImage` is drawn over the `background` colour and behind the widgets, and scales with the canvas.

Instead of its own values, a layout can use a saved theme by name: `"theme": "Ice"`. To change a few of the theme's
values for one layout, name it as `base` and add the ones to change: `"theme": { "base": "Ice", "accent": "#FF4081" }`.
Built-in themes: `Ice`, `Orange`, `Terminal`. Your own are in `themes\<name>.json` in the
settings folder, each holding the settings below; changing one restyles every layout that uses it.

| Setting | Type | Default | Description |
|---|---|---|---|
| `fontFamily` | font | `Segoe UI` | Font |
| `fontWeight` | `Thin` / `ExtraLight` / `Light` / `Normal` / `Medium` / `SemiBold` / `Bold` / `ExtraBold` / `Black` | `Light` | Weight |
| `fontFile` | fontfile | — | Font file. Use a .ttf/.otf file without installing it. Overrides the font above. |
| `foreground` | color | `#FFFFFF` | Text |
| `secondary` | color | `#D0FFFFFF` | Secondary text. Values in widget headers and rows. |
| `accent` | color | `#FF9800` | Accent. Graphs, bars and icons. |
| `background` | color | `#000000` | Background |
| `track` | color | `#1A1A1A` | Bar track. The unfilled part of bars. |
| `warning` | color | `#FFC107` | Warning. Used by warning colour rules that say "warning". |
| `critical` | color | `#F44336` | Critical. Used by warning colour rules that say "critical". |
| `backgroundImage` | imagefile | — | Image. A PNG or JPG drawn behind the widgets, over the background colour. |
| `backgroundFit` | `fill` / `fit` / `stretch` / `center` / `tile` | `fill` | Fit. fill covers the canvas (cropping), fit shows it all, stretch distorts to fit, center and tile keep its size. |
| `backgroundOpacity` | number (0–1) | `1` | Opacity. Below 1 the background colour shows through, e.g. 0.4 to dim a busy picture on black. |
