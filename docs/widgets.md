# Widget reference

<!-- Generated from the widget descriptors: ImpiousBonum.exe --widget-docs docs/widgets.md. Don't edit by hand. -->

Every widget in a layout file has `type`, `x`, `y`, `width` and `height` (in canvas units), plus the settings below.
Anything left out uses its default.

Templates are text with live values: `{metric.id}` or `{metric.id:spec}`, where spec can be a number format (`0.0`, `N0`),
a byte unit (`MB`, `GB`, …) or `nounit`. Colours are `foreground`, `secondary`, `accent`, `warning`, `critical`, `track`, `background`
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

## Multi-line graph (`lines`)

Several metrics' histories on one scale, each in its own colour, e.g. CPU and GPU load, or download and upload. A legend above shows each line's value.

| Setting | Type | Default | Description |
|---|---|---|---|
| `lines` | list | 2 entries | Lines |
| `legend` | true/false | `true` | Legend. Each line's colour, label and value above the graph. |
| `points` | number (2–240) | `120` | History (seconds) |
| `min` | number | `0` | Minimum |
| `max` | number | — | Maximum. Empty uses the metrics' natural maximum when they share one (100 for percentages), else scales to fit the highest line. |
| `lineThickness` | number (0.5–20) | `2` | Line thickness |
| `fontSize` | number (6–200) | `18` | Font size |

Each entry in `lines`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `CPU` | Label |
| `metric` | metric | `cpu.load` | Metric |
| `color` | color | `accent` | Colour |
| `text` | template | — | Value. Shown in the legend. Empty shows the metric's value. |

## Gauge (`gauge`)

A dial filled to a metric's share of its range, with the value in the middle, e.g. GPU load or CPU temperature.

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `CPU` | Label |
| `metric` | metric | `cpu.load` | Metric. Fills the dial. |
| `text` | template | — | Value text. Shown in the middle. Empty shows the metric's value. |
| `style` | `arc` / `half` / `ring` | `arc` | Shape. arc is a 270° dial with the label in the gap, half a semicircle, ring a full circle. |
| `min` | number | `0` | Minimum. An empty dial. |
| `max` | number | — | Maximum. A full dial. Empty uses the metric's natural maximum: 100 for percentages, the total for memory. |
| `thickness` | number (1–200) | `16` | Thickness |
| `color` | color | `accent` | Colour. The filled part of the dial. |
| `fontSize` | number (6–400) | `56` | Value size. Shrinks to fit inside the dial. |
| `labelSize` | number (6–200) | `24` | Label size |
| `thresholds` | list | — | Warning colours. Changes the dial and the value. An empty metric uses the gauge's metric. When several rules match, the last one wins, so list them mildest first. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## CPU threads (`cores`)

Every logical processor's load at once, as bars or a heatmap: shows whether work is spread out or stuck on a few threads.

| Setting | Type | Default | Description |
|---|---|---|---|
| `style` | `bars` / `heatmap` | `bars` | Style. bars fill up from the bottom; heatmap is a grid of cells that go from the track colour to the accent as they get busy. |
| `rows` | number (0–64) | `0` | Heatmap rows. 0 picks a grid that suits the box. |
| `gap` | number (0–50) | `3` | Gap |
| `cornerRadius` | number (0–50) | `2` | Corner radius |
| `color` | color | `accent` | Busy colour |
| `thresholds` | list | — | Warning colours. Changes a thread's colour. An empty metric uses that thread's load. When several rules match, the last one wins, so list them mildest first. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## Stat tile (`stat`)

A big value with a small trend line and its lowest and highest readings over the last minute or so.

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `CPU` | Label |
| `metric` | metric | `cpu.load` | Metric |
| `text` | template | — | Value text. Empty shows the metric's value. |
| `points` | number (2–240) | `60` | History (seconds) |
| `style` | `area` / `line` / `none` | `area` | Trend line |
| `showRange` | true/false | `true` | Lowest and highest |
| `fontSize` | number (6–400) | `56` | Value size |
| `labelSize` | number (6–200) | `20` | Label size |
| `thresholds` | list | — | Warning colours. Changes the value and the trend line. An empty metric uses the tile's metric. When several rules match, the last one wins, so list them mildest first. |

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
| `icon` | `cpu` / `gpu` / `ram` / `disk` / `network` / `fps` / `play` / `pause` / `play-pause` / `next` / `previous` / `stop` / `volume-down` / `volume-up` / `mute` / `music` / `folder` / `globe` / `layout` / `keyboard` / `terminal` / `gamepad` / `sliders` / `chat` / `camera` / `home` / `star` / `lock` / `power` / `moon` / `link` / `clear-day` / `clear-night` / `partly-day` / `partly-night` / `cloud` / `fog` / `drizzle` / `rain` / `sleet` / `snow` / `storm` / `drop` / `wind` | `cpu` | Icon |
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

The time with the date underneath, in your Windows language and region, and the time in other places if you add them.

| Setting | Type | Default | Description |
|---|---|---|---|
| `timeFormat` | text | `HH:mm` | Time format. .NET date/time format, e.g. HH:mm, h:mm tt, dddd d MMMM. |
| `dateFormat` | text | `dddd, d MMMM yyyy` | Date format. .NET date/time format, e.g. HH:mm, h:mm tt, dddd d MMMM. Empty hides the date. |
| `timeSize` | number (6–600) | `150` | Time size |
| `dateSize` | number (6–200) | `30` | Date size |
| `uppercaseDate` | true/false | `true` | Uppercase date |
| `align` | `left` / `center` / `right` | `center` | Alignment |
| `timeZone` | text | — | Time zone. Empty is this PC's. A place's time zone, e.g. Europe/London, America/Los_Angeles, Asia/Tokyo, or a Windows name such as GMT Standard Time. |
| `zones` | list | — | Other places. Smaller clocks under the date, each with its own time zone: a world clock. |
| `zoneFormat` | text | `HH:mm` | Other places' format. .NET date/time format, e.g. HH:mm, h:mm tt, dddd d MMMM. |
| `zoneSize` | number (6–200) | `26` | Other places' size |
| `zoneLayout` | `columns` / `rows` | `columns` | Other places' layout. columns puts them side by side, label above time; rows stacks them, label left and time right. |

Each entry in `zones`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `New York` | Label |
| `zone` | text | `America/New_York` | Time zone. A place's time zone, e.g. Europe/London, America/Los_Angeles, Asia/Tokyo, or a Windows name such as GMT Standard Time. |

## Timer (`timer`)

A stopwatch, a countdown, or the time left until a time or date. Tap the time to start and pause; the button underneath resets it.

| Setting | Type | Default | Description |
|---|---|---|---|
| `mode` | `countdown` / `stopwatch` / `until` | `countdown` | Mode. countdown counts down from the length below; stopwatch counts up; until counts down to the time or date below and runs by itself. |
| `duration` | text | `25:00` | Length. For countdown: minutes:seconds or hours:minutes:seconds, e.g. 5:00 or 1:30:00. A plain number is minutes. |
| `target` | text | `18:00` | Until. For until: a time of day (the next one comes round daily) or a date and time, e.g. 2026-12-25 or 2026-12-25 09:00. |
| `label` | text | — | Label. Shown above the time. Timers with the same label, mode and length share one clock, so pages and other layouts can show the same timer. |
| `doneText` | text | `Time's up` | When done. For countdown and until: shown and sent as a notification when it reaches zero. Empty sends none. |
| `notify` | true/false | `true` | Notify when done. A Windows notification when a countdown or until reaches zero, as long as the timer is on screen. |
| `showReset` | true/false | `true` | Reset button. Not shown for until. |
| `fontSize` | number (6–600) | `72` | Time size |
| `labelSize` | number (6–200) | `22` | Label size |
| `align` | `left` / `center` / `right` | `center` | Alignment |
| `doneColor` | color | `critical` | Done colour. The time's colour once it has reached zero; it also blinks. |
| `pausedColor` | color | `secondary` | Paused colour. The time's colour while stopped or paused. |

## Drives (`drives`)

One row per drive with free space and a bar showing how full it is. Follows drives as they are plugged in or removed.

| Setting | Type | Default | Description |
|---|---|---|---|
| `drives` | text | `all` | Drives. "all", or letters separated by commas, e.g. "C,D". |
| `label` | template (per drive) | `{letter}:/` | Row label. Each row's own drive: {letter}, {free}, {used}, {total}, {usedPct}, {label}, {read}, {write}, {active}, and with the sensor service {life}, {temp}, {status}, {model}, {hours}. In a metric id, * is the row's drive, e.g. {disk.*.read}. Other metrics work too. |
| `text` | template (per drive) | `{free} free / {total}` | Row value. Each row's own drive: {letter}, {free}, {used}, {total}, {usedPct}, {label}, {read}, {write}, {active}, and with the sensor service {life}, {temp}, {status}, {model}, {hours}. In a metric id, * is the row's drive, e.g. {disk.*.read}. Other metrics work too. |
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

## Top apps (`processes`)

The apps using the most CPU or memory, like a small Task Manager. An app's processes count together, so all of a browser's tabs are one row.

| Setting | Type | Default | Description |
|---|---|---|---|
| `sort` | `cpu` / `memory` | `cpu` | Sort by |
| `count` | number (1–30) | `5` | Rows |
| `columns` | `both` / `cpu` / `memory` | `both` | Show. Which numbers each row shows after the app's name. |
| `fontSize` | number (6–200) | `20` | Font size |
| `spacing` | number (0–200) | `4` | Row spacing |
| `thresholds` | list | — | Warning colours. Changes the colour of each row's CPU figure. An empty metric uses that app's CPU. When several rules match, the last one wins, so list them mildest first. |

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

## Bars (`bars`)

Labelled bars, one per metric, each filled to the metric's share of its maximum, e.g. CPU and RAM load.

| Setting | Type | Default | Description |
|---|---|---|---|
| `bars` | list | 2 entries | Bars |
| `fontSize` | number (6–200) | `25` | Label size |
| `valueFontSize` | number (6–200) | — | Value size. Empty uses 85% of the label size. |
| `barHeight` | number (1–100) | `8` | Bar height |
| `spacing` | number (0–200) | `12` | Bar spacing |
| `hideUnavailable` | true/false | `true` | Hide without a reading. Hides a bar whose metric has no reading, and the whole widget when none has one. They show faded while you edit the layout. |
| `thresholds` | list | — | Warning colours. Changes each bar. An empty metric uses that bar's metric. When several rules match, the last one wins, so list them mildest first. |

Each entry in `bars`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `CPU` | Label |
| `metric` | metric | `cpu.load` | Metric. Fills the bar. |
| `text` | template | — | Value. Shown on the right. Empty shows the metric's value. |
| `max` | number | — | Maximum. A full bar. Empty uses the metric's natural maximum: 100 for percentages, the total for memory. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## Now playing (`media`)

What's playing in Spotify, a browser, Media Player or any app in Windows' media controls: album art, title, artist, progress and buttons to skip or pause.

| Setting | Type | Default | Description |
|---|---|---|---|
| `title` | template | `{media.title}` | Title |
| `subtitle` | template | `{media.artist}` | Second line. E.g. the artist, or {media.artist} · {media.album}. Empty hides it. |
| `fontSize` | number (6–200) | `34` | Title size |
| `subtitleSize` | number (6–200) | — | Second line size. Empty uses 70% of the title size. |
| `showArt` | true/false | `true` | Album art |
| `showProgress` | true/false | `true` | Progress bar. With the position and length under it. |
| `showControls` | true/false | `true` | Buttons. Previous, play/pause and next. They work like the keyboard's media keys. |
| `controlSize` | number (16–200) | `52` | Button size |
| `hideIdle` | true/false | `true` | Hide when nothing is playing. It shows faded while you edit the layout. |

## Audio visualiser (`visualizer`)

Bars that move with whatever the PC is playing, from low notes on the left to high on the right. Draws many times a second, so it uses a little more CPU than other widgets.

| Setting | Type | Default | Description |
|---|---|---|---|
| `bands` | number (4–128) | `32` | Bars |
| `style` | `bars` / `mirror` | `bars` | Style. bars rise from the bottom; mirror grows up and down from the middle. |
| `fps` | number (5–60) | `25` | Frames a second. Smoother as it goes up, and a little more CPU. 25 looks fluid. |
| `gap` | number (0–50) | `3` | Gap |
| `cornerRadius` | number (0–50) | `2` | Corner radius |
| `fall` | number (0.01–1) | `0.06` | Fall speed. How far a bar can drop each frame, as a share of the height. Lower falls more gently. |
| `peaks` | true/false | `true` | Peak markers. A line at each bar's recent highest, drifting down. |
| `color` | color | `accent` | Colour |
| `peakColor` | color | `foreground` | Peak colour |

## Buttons (`buttons`)

Buttons to tap: play/pause, skip and volume, open a program, file or web address, send a keyboard shortcut to the app you're using, or switch layout.

| Setting | Type | Default | Description |
|---|---|---|---|
| `buttons` | list | 4 entries | Buttons |
| `columns` | number (0–50) | `0` | Columns. 0 puts every button in one row. |
| `fontSize` | number (6–200) | `20` | Label size |
| `iconSize` | number (0–400) | `40` | Icon size |
| `spacing` | number (0–200) | `10` | Spacing |
| `style` | `tile` / `plain` | `tile` | Style. tile gives each button a background. |
| `cornerRadius` | number (0–200) | `10` | Corner radius |
| `color` | color | `accent` | Icon colour |

Each entry in `buttons`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `Play/pause` | Label. Empty shows just the icon. |
| `icon` | `none` / `cpu` / `gpu` / `ram` / `disk` / `network` / `fps` / `play` / `pause` / `play-pause` / `next` / `previous` / `stop` / `volume-down` / `volume-up` / `mute` / `music` / `folder` / `globe` / `layout` / `keyboard` / `terminal` / `gamepad` / `sliders` / `chat` / `camera` / `home` / `star` / `lock` / `power` / `moon` / `link` / `clear-day` / `clear-night` / `partly-day` / `partly-night` / `cloud` / `fog` / `drizzle` / `rain` / `sleet` / `snow` / `storm` / `drop` / `wind` | `play-pause` | Icon |
| `action` | `play-pause` / `next` / `previous` / `stop` / `volume-up` / `volume-down` / `mute` / `open` / `keys` / `layout` | `play-pause` | Action. Media and volume actions work like the keyboard's media keys. open, keys and layout use the target below. |
| `target` | text | — | Target. open: a program, file, folder or web address, e.g. calc.exe, C:\Games or https://… · keys: a shortcut, e.g. Ctrl+Shift+M or F13 · layout: the layout's name. |
| `arguments` | text | — | Arguments. open: command-line arguments for a program. |

## Weather (`weather`)

The weather now and the days or hours ahead, from Open-Meteo.com. Set your location from the tray: Weather → Set location.

| Setting | Type | Default | Description |
|---|---|---|---|
| `forecast` | `days` / `hours` / `none` | `days` | Forecast |
| `count` | number (1–14) | `5` | Days or hours |
| `hourStep` | number (1–12) | `1` | Hours apart. For an hourly forecast, e.g. 3 for every three hours. |
| `showDetails` | true/false | `true` | Details. Conditions, today's high and low, and feels like, beside the temperature. |
| `showLocation` | true/false | `false` | Location |
| `fontSize` | number (6–400) | `80` | Temperature size |
| `forecastSize` | number (6–200) | `22` | Forecast size |
| `iconColor` | color | `accent` | Icon colour |

## Agenda (`agenda`)

What's coming up in your calendars (tray → Calendar…), by day, with what's on now in the accent colour.

| Setting | Type | Default | Description |
|---|---|---|---|
| `days` | number (1–15) | `3` | Days. How many days to show, starting today. |
| `count` | number (1–50) | `8` | Most events |
| `calendars` | text | `all` | Calendars. "all", or calendar names separated by commas, e.g. "Work, Family". |
| `location` | true/false | `true` | Show where |
| `dayHeaders` | true/false | `true` | Day headings. Today, Tomorrow, then the date, above each day's events. |
| `emptyText` | text | `Nothing on` | When there's nothing. Empty shows nothing. |
| `timeFormat` | text | `HH:mm` | Time format. .NET time format, e.g. HH:mm or h:mm tt. |
| `fontSize` | number (6–200) | `20` | Font size |
| `headerSize` | number (6–200) | `15` | Heading size |
| `spacing` | number (0–100) | `6` | Event spacing |
| `nowColor` | color | `accent` | What's on now. The colour of events that are happening now. |

## News (`news`)

Headlines from your news feeds (tray → News feeds…), newest first, as a list or one at a time. Tap a headline to open it in your browser.

| Setting | Type | Default | Description |
|---|---|---|---|
| `style` | `list` / `ticker` | `list` | Style. list shows the newest few; ticker shows one at a time, moving on every few seconds. |
| `count` | number (1–30) | `5` | Headlines. How many the list shows, or the ticker goes through. |
| `seconds` | number (2–120) | `8` | Ticker seconds. How long the ticker shows each headline. |
| `feeds` | text | `all` | Feeds. "all", or feed names separated by commas, e.g. "BBC, The Verge". |
| `source` | true/false | `true` | Show where from |
| `age` | true/false | `true` | Show how old |
| `tapToOpen` | true/false | `true` | Tap to open. Opens the article in your browser. Not in the tablet view. |
| `fontSize` | number (6–200) | `22` | Font size |
| `spacing` | number (0–100) | `8` | Spacing |
| `lines` | number (1–4) | `1` | Lines per headline. Long headlines wrap onto this many lines, then end with … |

## Image (`image`)

A picture from a file: a logo, artwork or a photo. It's packed into exported layouts so it travels with them.

| Setting | Type | Default | Description |
|---|---|---|---|
| `file` | imagefile | — | Picture. A PNG, JPG, GIF, BMP or TIFF file. |
| `fit` | `fill` / `fit` / `stretch` / `center` / `tile` | `fit` | Fit. fill covers the box (cropping), fit shows it all, stretch distorts to fit, center and tile keep its size. |
| `opacity` | number (0–1) | `1` | Opacity |
| `cornerRadius` | number (0–1000) | `0` | Corner radius |
| `recolor` | true/false | `false` | Recolour. Draws the picture's shape in the colour below, e.g. a white logo in the theme's accent. |
| `color` | color | `accent` | Colour |

## Shape (`shape`)

A rounded panel, a circle or a line, to group widgets or divide them. It's added behind the other widgets.

| Setting | Type | Default | Description |
|---|---|---|---|
| `shape` | `panel` / `circle` / `line` | `panel` | Shape. A line runs along the middle of the box, across its longer side. |
| `color` | color | `track` | Colour. The fill of a panel or circle, or the colour of a line. |
| `cornerRadius` | number (0–1000) | `16` | Corner radius. For a panel. |
| `borderColor` | color | `accent` | Outline colour |
| `borderThickness` | number (0–100) | `0` | Outline. 0 has no outline. |
| `thickness` | number (0.5–100) | `2` | Line thickness. For a line. |
| `opacity` | number (0–1) | `1` | Opacity |

## Claude usage (`claude`)

Your Claude plan's session (5-hour) and weekly usage, as Claude Code reports them. Shown only while Claude Code is open and reporting. Set it up from the tray: Claude usage → Connect Claude Code.

| Setting | Type | Default | Description |
|---|---|---|---|
| `bars` | list | 2 entries | Bars |
| `fontSize` | number (6–200) | `25` | Label size |
| `valueFontSize` | number (6–200) | — | Value size. Empty uses 85% of the label size. |
| `barHeight` | number (1–100) | `8` | Bar height |
| `spacing` | number (0–200) | `12` | Bar spacing |
| `hideUnavailable` | true/false | `true` | Hide without a reading. Hides a bar whose metric has no reading, and the whole widget when none has one. They show faded while you edit the layout. |
| `thresholds` | list | — | Warning colours. Changes each bar. An empty metric uses that bar's metric. When several rules match, the last one wins, so list them mildest first. |

Each entry in `bars`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `CPU` | Label |
| `metric` | metric | `cpu.load` | Metric. Fills the bar. |
| `text` | template | — | Value. Shown on the right. Empty shows the metric's value. |
| `max` | number | — | Maximum. A full bar. Empty uses the metric's natural maximum: 100 for percentages, the total for memory. |

Each entry in `thresholds`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `metric` | metric | — | Metric. Empty uses the widget's own metric. |
| `above` | number | — | Above. Matches when the value is above this. |
| `below` | number | — | Below. Matches when the value is below this. |
| `color` | color | `critical` | Colour |

## Theme

The layout's `theme` object sets the font and colours every widget uses, and the canvas background.
A `backgroundImage` is drawn over the `background` colour and behind the widgets, and scales with the canvas.

Instead of its own values, a layout can use a saved theme by name: `"theme": "Ice"`. To change a few of the theme's
values for one layout, name it as `base` and add the ones to change: `"theme": { "base": "Ice", "accent": "#FF4081" }`.
Built-in themes: `8-Bit`, `Amber`, `Aurora`, `Bauhaus`, `Blue Screen`, `Blueprint`, `Catppuccin`, `Chalkboard`, `Cockpit`, `Cyberpunk`, `Desktop 95`, `Dracula`, `Dune`, `Espresso`, `Forest`, `Gatsby`, `Glacier`, `Grimoire`, `Gruvbox`, `Handheld`, `High Contrast`, `Ice`, `Magma`, `Monokai`, `Night Watch`, `Noir`, `Nord`, `Notebook`, `Orange`, `Outrun`, `Rose Pine`, `Sakura`, `Solarized`, `Solarized Light`, `Stadium`, `Swiss`, `Tactical`, `Terminal`, `Tokyo Night`, `Toxic`, `Tropical`, `Typewriter`, `Vaporwave`. Your own are in `themes\<name>.json` in the
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
| `accent2` | color | — | Accent fades to. A second accent colour: graphs, bars, gauges and icons fade from the accent to this, left to right. Empty is a plain accent. |
| `backgroundGradient` | color | — | Background fades to. The background fades from its colour at the top to this at the bottom. Empty is a plain background. |
| `glow` | number (0–1) | — | Glow. A soft neon glow around every widget's text and shapes: 0.3 is subtle, 1 is strong. Empty or 0 is none. Uses a little more CPU. |
| `glowColor` | color | — | Glow colour. Empty glows in the accent colour. |
