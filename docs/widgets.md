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

Every widget can also have its own font and colours instead of the layout theme's (*Widget theme* in the editor).
Leave them out, or empty, to use the theme's. Named colours in the widget's other settings, such as `accent`, then
mean the widget's own.

| Setting | Type | Default | Description |
|---|---|---|---|
| `fontFamily` | font | — | Font. Empty uses the theme's. |
| `fontWeight` | `theme` / `Thin` / `ExtraLight` / `Light` / `Normal` / `Medium` / `SemiBold` / `Bold` / `ExtraBold` / `Black` | `theme` | Weight |
| `foreground` | color | — | Text. Empty uses the theme's. |
| `secondary` | color | — | Secondary text. Empty uses the theme's. |
| `accent` | color | — | Accent. Empty uses the theme's. |
| `track` | color | — | Bar track. Empty uses the theme's. |
| `warning` | color | — | Warning. Empty uses the theme's. |
| `critical` | color | — | Critical. Empty uses the theme's. |

## Widgets

- **Readings:** [Text](#text-text) · [Graph](#graph-graph) · [Multi-line graph](#multi-line-graph-lines) · [Gauge](#gauge-gauge) · [Stat tile](#stat-tile-stat) · [CPU threads](#cpu-threads-cores) · [Icon and value](#icon-and-value-icon) · [Bars](#bars-bars) · [Rows](#rows-rows) · [Drives](#drives-drives) · [Top apps](#top-apps-processes)
- **Time and calendar:** [Clock](#clock-clock) · [Timer](#timer-timer) · [Agenda](#agenda-agenda) · [Countdowns](#countdowns-countdowns)
- **Media and controls:** [Now playing](#now-playing-media) · [Audio visualiser](#audio-visualiser-visualizer) · [Buttons](#buttons-buttons)
- **Weather, news and Claude:** [Weather](#weather-weather) · [News](#news-news) · [Claude usage](#claude-usage-claude)
- **Pictures and shapes:** [Image](#image-image) · [Shape](#shape-shape) · [Photo frame](#photo-frame-photos)

## Text (`text`)

A line of text with live values, e.g. "{gpu.load}" or "{cpu.temp:nounit} °C".

| Setting | Type | Default | Description |
|---|---|---|---|
| `text` | template | `{cpu.load}` | Text |
| `fontSize` | number (6–400) | `48` | Font size |
| `align` | `left` / `center` / `right` | `left` | Alignment |
| `uppercase` | true/false | `false` | Uppercase |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `hideUnavailable` | true/false | `false` | Hide without a reading. Hides the text while none of its metrics has a reading, e.g. a game's session before you've played. It shows faded while you edit the layout. |
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
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
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
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |

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
| `trackColor` | color | `track` | Track colour. The empty part of the dial. track is the theme's bar track. |
| `fontSize` | number (6–400) | `56` | Value size |
| `labelSize` | number (6–200) | `24` | Label size |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `thresholds` | list | — | Warning colours. Changes the dial and the value. An empty metric uses the gauge's metric. When several rules match, the last one wins, so list them mildest first. |

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
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `thresholds` | list | — | Warning colours. Changes the value and the trend line. An empty metric uses the tile's metric. When several rules match, the last one wins, so list them mildest first. |

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
| `trackColor` | color | `track` | Idle colour. An idle thread's bar track or heatmap cell. track is the theme's bar track. |
| `thresholds` | list | — | Warning colours. Changes a thread's colour. An empty metric uses that thread's load. When several rules match, the last one wins, so list them mildest first. |

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
| `icon` | `cpu` / `gpu` / `ram` / `disk` / `network` / `fps` / `play` / `pause` / `play-pause` / `next` / `previous` / `stop` / `volume-down` / `volume-up` / `mute` / `music` / `folder` / `globe` / `layout` / `keyboard` / `terminal` / `gamepad` / `sliders` / `chat` / `camera` / `home` / `star` / `lock` / `power` / `moon` / `link` / `clear-day` / `clear-night` / `partly-day` / `partly-night` / `cloud` / `fog` / `drizzle` / `rain` / `sleet` / `snow` / `storm` / `drop` / `wind` / `warning` / `mic` / `mic-off` | `cpu` | Icon |
| `text` | template | `{cpu.temp:nounit} °C` | Text |
| `fontSize` | number (6–400) | `64` | Font size |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
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

## Bars (`bars`)

Labelled bars, one per metric, each filled to the metric's share of its maximum, e.g. CPU and RAM load.

| Setting | Type | Default | Description |
|---|---|---|---|
| `bars` | list | 2 entries | Bars |
| `fontSize` | number (6–200) | `25` | Label size |
| `valueFontSize` | number (6–200) | — | Value size. Empty uses 85% of the label size. |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `barHeight` | number (1–100) | `8` | Bar height |
| `spacing` | number (0–200) | `12` | Bar spacing |
| `hideUnavailable` | true/false | `true` | Hide without a reading. Hides a bar whose metric has no reading, and the whole widget when none has one. They show faded while you edit the layout. |
| `color` | color | `accent` | Bar colour |
| `trackColor` | color | `track` | Track colour. The empty part of each bar. track is the theme's bar track. |
| `labelColor` | color | `foreground` | Label colour |
| `valueColor` | color | `secondary` | Value colour |
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

## Rows (`rows`)

A stack of label and value rows, e.g. Download / Upload / Ping.

| Setting | Type | Default | Description |
|---|---|---|---|
| `rows` | list | — | Rows |
| `fontSize` | number (6–200) | `28` | Label size |
| `valueFontSize` | number (6–200) | — | Value size. Empty uses 85% of the label size. |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `align` | `top` / `bottom` | `top` | Stack from |

Each entry in `rows`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `Download` | Label |
| `text` | template | `{net.down}` | Value |

## Drives (`drives`)

One row per drive with free space and a bar showing how full it is. Follows drives as they are plugged in or removed.

| Setting | Type | Default | Description |
|---|---|---|---|
| `drives` | text | `all` | Drives. "all", or letters separated by commas, e.g. "C,D". |
| `label` | template (per drive) | `{letter}:` | Row label. Each row's own drive: {letter}, {free}, {used}, {total}, {usedPct}, {label}, {read}, {write}, {active}, and with the sensor service {life}, {temp}, {status}, {model}, {hours}. In a metric id, * is the row's drive, e.g. {disk.*.read}. Other metrics work too. |
| `text` | template (per drive) | `{free} free / {total}` | Row value. Each row's own drive: {letter}, {free}, {used}, {total}, {usedPct}, {label}, {read}, {write}, {active}, and with the sensor service {life}, {temp}, {status}, {model}, {hours}. In a metric id, * is the row's drive, e.g. {disk.*.read}. Other metrics work too. |
| `fontSize` | number (6–200) | `25` | Font size |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `barHeight` | number (1–100) | `5` | Bar height |
| `spacing` | number (0–200) | `12` | Row spacing |
| `color` | color | `accent` | Bar colour |
| `trackColor` | color | `track` | Track colour. The empty part of each bar. track is the theme's bar track. |
| `labelColor` | color | `foreground` | Label colour |
| `valueColor` | color | `secondary` | Value colour |
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
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `spacing` | number (0–200) | `4` | Row spacing |
| `thresholds` | list | — | Warning colours. Changes the colour of each row's CPU figure. An empty metric uses that app's CPU. When several rules match, the last one wins, so list them mildest first. |

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
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
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
| `target` | text | `18:00` | Until. For until: a time of day (the next one comes round daily), a month and day with no year (comes round yearly, e.g. 12-25 or 10-31 18:00), or a date and time, e.g. 2026-12-25 or 2026-12-25 09:00. |
| `label` | text | — | Label. Shown above the time. Timers with the same label, mode and length share one clock, so pages and other layouts can show the same timer. |
| `doneText` | text | `Time's up` | When done. For countdown and until: shown and sent as a notification when it reaches zero. Empty sends none. |
| `notify` | true/false | `true` | Notify when done. A Windows notification when a countdown or until reaches zero, as long as the timer is on screen. |
| `showReset` | true/false | `true` | Reset button. Not shown for until. |
| `fontSize` | number (6–600) | `72` | Time size |
| `labelSize` | number (6–200) | `22` | Label size |
| `align` | `left` / `center` / `right` | `center` | Alignment |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `doneColor` | color | `critical` | Done colour. The time's colour once it has reached zero; it also blinks. |
| `pausedColor` | color | `secondary` | Paused colour. The time's colour while stopped or paused. |

## Agenda (`agenda`)

What's coming up in your calendars (set them in the editor, or on the Calendars page), by day, with what's on now in the accent colour.

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
| `shrinkToFit` | true/false | `true` | Shrink to fit. Rows that would be cut off at the bottom, say in a bigger font, are left out, and a first row too tall for the widget gets smaller to fit. |
| `spacing` | number (0–100) | `6` | Event spacing |
| `nowColor` | color | `accent` | What's on now. The colour of events that are happening now. |

## Countdowns (`countdowns`)

How long until the days you add, soonest first: birthdays, holidays, a release, a trip. On the day, it says Today in the accent colour.

| Setting | Type | Default | Description |
|---|---|---|---|
| `events` | list | 4 entries | Events. Each with a label and a date. One that comes round every year starts again the day after; one that happens once leaves the list. |
| `count` | number (1–50) | `5` | Rows. The most it lists, soonest first. |
| `style` | `days` / `time` | `days` | How long. days says 69 days, Tomorrow or Today; time gives the days, hours and minutes, e.g. 69d 02:13. |
| `heading` | text | — | Heading. Shown above the list, e.g. Coming up. Empty shows none. |
| `emptyText` | text | `Nothing coming up` | When there's nothing. Empty shows nothing. |
| `fontSize` | number (6–200) | `22` | Font size |
| `headingSize` | number (6–200) | — | Heading size. Empty uses 75% of the font size. |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Rows that would be cut off at the bottom, say in a bigger font, are left out, and a first row too tall for the widget gets smaller to fit. |
| `spacing` | number (0–100) | `6` | Row spacing |
| `todayColor` | color | `accent` | Today's colour. The colour of the events that are today. |

Each entry in `events`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `Christmas` | Label |
| `date` | text | `12-25` | Date. A month and day that comes round every year, e.g. 12-25 or 03-14 09:00, or a date that happens once, e.g. 2026-12-25 or 2026-12-25 18:00. |

## Now playing (`media`)

What's playing in Spotify, a browser, Media Player or any app in Windows' media controls: album art, title, artist, progress and buttons to skip or pause.

| Setting | Type | Default | Description |
|---|---|---|---|
| `title` | template | `{media.title}` | Title |
| `subtitle` | template | `{media.artist}` | Second line. E.g. the artist, or {media.artist} · {media.album}. Empty hides it. |
| `fontSize` | number (6–200) | `34` | Title size |
| `subtitleSize` | number (6–200) | — | Second line size. Empty uses 70% of the title size. |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `showArt` | true/false | `true` | Album art |
| `showProgress` | true/false | `true` | Progress bar. With the position and length under it. |
| `showControls` | true/false | `true` | Buttons. Previous, play/pause and next. They work like the keyboard's media keys. |
| `controlSize` | number (16–200) | `52` | Button size |
| `hideIdle` | true/false | `true` | Hide when nothing is playing. It shows faded while you edit the layout. |
| `textColor` | color | `foreground` | Title colour |
| `detailColor` | color | `secondary` | Second line colour. The second line, the times under the progress bar, and the music note when there's no album art. |
| `artColor` | color | `track` | Album art background. Behind the music note when there's no album art. track is the theme's bar track. |
| `progressColor` | color | `accent` | Progress colour |
| `trackColor` | color | `track` | Progress track colour. The rest of the progress bar. track is the theme's bar track. |
| `buttonColor` | color | `foreground` | Button colour |

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
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `iconSize` | number (0–400) | `40` | Icon size |
| `spacing` | number (0–200) | `10` | Spacing |
| `style` | `tile` / `plain` | `tile` | Style. tile gives each button a background. |
| `cornerRadius` | number (0–200) | `10` | Corner radius |
| `color` | color | `accent` | Icon colour |
| `tileColor` | color | `track` | Tile colour. Each button's background, with the tile style. track is the theme's bar track. |
| `labelColor` | color | `foreground` | Label colour |

Each entry in `buttons`:

| Setting | Type | Default | Description |
|---|---|---|---|
| `label` | text | `Play/pause` | Label. Empty shows just the icon. |
| `icon` | `none` / `cpu` / `gpu` / `ram` / `disk` / `network` / `fps` / `play` / `pause` / `play-pause` / `next` / `previous` / `stop` / `volume-down` / `volume-up` / `mute` / `music` / `folder` / `globe` / `layout` / `keyboard` / `terminal` / `gamepad` / `sliders` / `chat` / `camera` / `home` / `star` / `lock` / `power` / `moon` / `link` / `clear-day` / `clear-night` / `partly-day` / `partly-night` / `cloud` / `fog` / `drizzle` / `rain` / `sleet` / `snow` / `storm` / `drop` / `wind` / `warning` / `mic` / `mic-off` | `play-pause` | Icon |
| `action` | `play-pause` / `next` / `previous` / `stop` / `volume-up` / `volume-down` / `mute` / `mic-mute` / `open` / `keys` / `layout` | `play-pause` | Action. Media and volume actions work like the keyboard's media keys. open, keys and layout use the target below. mic-mute mutes or unmutes your microphone; with the mic icon, the button shows mic-off while it's muted. |
| `target` | text | — | Target. open: a program, file, folder or web address, e.g. calc.exe, C:\Games or https://… · keys: a shortcut, e.g. Ctrl+Shift+M or F13 · layout: the layout's name. |
| `arguments` | text | — | Arguments. open: command-line arguments for a program. |

## Weather (`weather`)

The weather now and the days or hours ahead, from Open-Meteo.com, and severe weather warnings if they're on. Set your location in the editor (select the widget) or on the Weather page, and add more places there to move between.

| Setting | Type | Default | Description |
|---|---|---|---|
| `showCurrent` | true/false | `true` | Weather now. The big icon and temperature for now. Off leaves just the forecast, e.g. for a widget of the hours ahead beside another of the days. |
| `forecast` | `days` / `hours` / `none` | `days` | Forecast |
| `count` | number (1–48) | `5` | Days or hours. Up to 16 days, or 48 hours. |
| `hourStep` | number (1–12) | `1` | Hours apart. For an hourly forecast, e.g. 3 for every three hours. |
| `forecastDetails` | some of `chance of rain` / `rainfall` / `wind` / `gusts` / `UV` in order, separated by commas | `chance of rain` | Forecast details. What each day or hour shows under its temperature, in order: the chance of rain (from Chance of rain from), rainfall (only when there's some), wind, gusts (in the wind's units) and UV. A day's are its total rain, its strongest wind and gusts, and its highest UV. |
| `rainFrom` | number (0–100) | `30` | Chance of rain from. With chance of rain in Forecast details, show it from this percentage; 0 always shows it. |
| `showDetails` | true/false | `true` | Details. The ones chosen under Details to show, beside the temperature. Off leaves them all out. |
| `details` | some of `conditions` / `high and low` / `feels like` / `wind` / `gusts` / `humidity` / `pressure` / `UV` / `chance of rain` / `rainfall` / `sunrise` / `sunset` / `daylight` / `dew point` / `visibility` / `cloud cover` / `air quality` / `pollen` in order, separated by commas | `conditions, high and low, feels like` | Details to show. Which details go beside the temperature, in order. Conditions, when they're first, have the larger line, as they always had; the rest go a few to a line, with a short label where the number alone could be anything, such as Feels 12°, Gusts 40 km/h or UV 3 Moderate. High and low, chance of rain, rainfall and daylight are today's, and pressure has an arrow for rising, falling or steady over the last three hours. Air quality and pollen need them turned on (the Weather page) and are your location's, so they're left out for other places. |
| `showLocation` | true/false | `false` | Location |
| `places` | names in order, separated by commas | — | Places. Which places to show, in order: your location and the more places on the Weather page, by their names up to the first comma, such as Leeds (or Paris (Texas) where two places would share one). None shows just your location. With more than one, the widget shows one at a time with its name, moving on every Seconds per place, and a tap moves it (and any other weather widget showing the same places) on straight away; the tablet view moves on by itself. A place other than your location always shows its name. Warnings are your location's, so they only show while it's on show. |
| `placeSeconds` | number (3–600) | `10` | Seconds per place. With more than one place, how long each one shows. |
| `showWarnings` | true/false | `true` | Warnings. With severe weather warnings on (the Weather page), the most serious one along the bottom while there are any: in the warning colour, or the critical colour for red, severe and extreme ones. Only with Weather now, so a forecast-only widget beside it doesn't show it again. Its size follows Forecast size, or Temperature size without a forecast. Tap it to read the warning (not in the tablet view). |
| `fontSize` | number (6–400) | `80` | Temperature size |
| `forecastSize` | number (6–200) | `22` | Forecast size |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `iconStyle` | `outline` / `colour` | `outline` | Icons. outline draws them in the icon colour below; colour in their own: a yellow sun, white clouds, blue rain, white lightning. |
| `iconColor` | color | `accent` | Icon colour. For outline icons. |
| `textColor` | color | `foreground` | Text colour. The temperatures, and the conditions when they have the larger line (first in Details to show). |
| `detailColor` | color | `secondary` | Detail colour. The smaller details beside the temperature, the forecast's lows, times and days, and what's under each day or hour. |

## News (`news`)

Headlines from your news feeds (set them in the editor, or on the News feeds page), newest first, as a list, one at a time, or one at a time from each feed. Tap a headline to open it in your browser.

| Setting | Type | Default | Description |
|---|---|---|---|
| `style` | `list` / `ticker` / `tickers` | `list` | Style. list shows the newest few; ticker shows one at a time, moving on every few seconds; tickers gives each feed a ticker of its own, a line each. |
| `count` | number (1–30) | `5` | Headlines. How many the list shows, or each ticker goes through. |
| `seconds` | number (2–120) | `8` | Ticker seconds. How long a ticker shows each headline. |
| `feeds` | `all`, or names in order, separated by commas | `all` | Feeds. All feeds, or just the ones chosen. With tickers, each gets a line of its own, in the order they're listed. |
| `source` | true/false | `true` | Show where from |
| `age` | true/false | `true` | Show how old |
| `tapToOpen` | true/false | `true` | Tap to open. Opens the article in your browser. Not in the tablet view. |
| `fontSize` | number (6–200) | `22` | Font size |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Rows that would be cut off at the bottom, say in a bigger font, are left out, and a first row too tall for the widget gets smaller to fit. |
| `spacing` | number (0–100) | `8` | Spacing |
| `lines` | number (1–4) | `1` | Lines per headline. Long headlines wrap onto this many lines, then end with … |

## Claude usage (`claude`)

Your Claude plan's session (5-hour) and weekly usage, as Claude Code reports them. Shown only while Claude Code is open and reporting. Set it up in the editor (select the widget) or on the Claude usage page.

| Setting | Type | Default | Description |
|---|---|---|---|
| `bars` | list | 2 entries | Bars |
| `fontSize` | number (6–200) | `25` | Label size |
| `valueFontSize` | number (6–200) | — | Value size. Empty uses 85% of the label size. |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `barHeight` | number (1–100) | `8` | Bar height |
| `spacing` | number (0–200) | `12` | Bar spacing |
| `hideUnavailable` | true/false | `true` | Hide without a reading. Hides a bar whose metric has no reading, and the whole widget when none has one. They show faded while you edit the layout. |
| `color` | color | `accent` | Bar colour |
| `trackColor` | color | `track` | Track colour. The empty part of each bar. track is the theme's bar track. |
| `labelColor` | color | `foreground` | Label colour |
| `valueColor` | color | `secondary` | Value colour |
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

## Photo frame (`photos`)

A slideshow of the pictures in a folder, like a digital photo frame; tap it for the next one. The folder is on this PC, so exported and shared layouts leave it out.

| Setting | Type | Default | Description |
|---|---|---|---|
| `folder` | folder | — | Folder. The folder of pictures to show: PNG, JPG, GIF, BMP and TIFF files. It stays on this PC: exported and shared layouts leave it out. |
| `subfolders` | true/false | `true` | Include subfolders. The pictures in the folders inside it too. |
| `seconds` | number (3–86400) | `30` | Seconds per photo. How long each photo shows. A tap moves on straight away. |
| `order` | `shuffle` / `name` / `date` | `shuffle` | Order. shuffle shows every photo, in a random order, before any comes round again; name goes from A to Z; date goes from oldest to newest, by when the files last changed. |
| `caption` | `none` / `name` / `date` | `none` | Caption. Along the bottom of the photo: its file name, or the date it was taken (or else the file's date). |
| `dateFormat` | text | `d MMMM yyyy` | Date format. For a date caption: a .NET date format, e.g. d MMMM yyyy (14 August 2026) or d MMM yyyy, HH:mm (14 Aug 2026, 20:31). |
| `fontSize` | number (6–200) | `18` | Caption size |
| `shrinkToFit` | true/false | `true` | Shrink to fit. Text too wide or too tall for the widget, say in a bigger font, gets smaller to fit instead of being cut off. |
| `fit` | `fill` / `fit` | `fill` | Fit. fill covers the box, cropping the edges; fit shows the whole photo. |
| `fade` | number (0–5) | `1` | Fade. How many seconds one photo takes to fade into the next; 0 changes straight away. |
| `cornerRadius` | number (0–1000) | `0` | Corner radius |
| `opacity` | number (0–1) | `1` | Opacity |
| `captionColor` | color | `foreground` | Caption colour. Over a shade of the theme's background, so it can be read over any photo. |

## Theme

The layout's `theme` object sets the font and colours every widget uses, and the canvas background.
A `backgroundImage` is drawn over the `background` colour and behind the widgets, and scales with the canvas.

Instead of its own values, a layout can use a saved theme by name: `"theme": "Ice"`. To change a few of the theme's
values for one layout, name it as `base` and add the ones to change: `"theme": { "base": "Ice", "accent": "#FF4081" }`.
Built-in themes: `8-Bit`, `Amber`, `Aurora`, `Bauhaus`, `Blue Screen`, `Blueprint`, `Catppuccin`, `Chalkboard`, `Christmas`, `Cockpit`, `Cyberpunk`, `Day of the Dead`, `Desktop 95`, `Diwali`, `Dracula`, `Dune`, `Easter`, `Eid`, `Espresso`, `Fireworks`, `Forest`, `Gatsby`, `Gingerbread`, `Glacier`, `Grimoire`, `Gruvbox`, `Halloween`, `Handheld`, `Hanukkah`, `High Contrast`, `Ice`, `Lunar New Year`, `Magma`, `Monokai`, `Night Watch`, `Noir`, `Nord`, `Notebook`, `Orange`, `Outrun`, `Rose Pine`, `Sakura`, `Shamrock`, `Solarized`, `Solarized Light`, `Stadium`, `Swiss`, `Tactical`, `Terminal`, `Tokyo Night`, `Toxic`, `Tropical`, `Typewriter`, `Valentine`, `Vaporwave`. Your own are in `themes\<name>.json` in the
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
| `backgroundImage` | imagefile | — | Image. A picture drawn behind the widgets, over the background colour. An animated GIF or a video (MP4, WMV) moves. |
| `backgroundFit` | `fill` / `fit` / `stretch` / `center` / `tile` | `fill` | Fit. fill covers the canvas (cropping), fit shows it all, stretch distorts to fit, center and tile keep its size. |
| `backgroundOpacity` | number (0–1) | `1` | Opacity. Below 1 the background colour shows through, e.g. 0.4 to dim a busy picture on black. |
| `accent2` | color | — | Accent fades to. A second accent colour: graphs, bars, gauges and icons fade from the accent to this, left to right. Empty is a plain accent. |
| `backgroundGradient` | color | — | Background fades to. The background fades from its colour at the top to this at the bottom. Empty is a plain background. |
| `glow` | number (0–1) | — | Glow. A soft neon glow around every widget's text and shapes: 0.3 is subtle, 1 is strong. Empty or 0 is none. Uses a little more CPU. |
| `glowColor` | color | — | Glow colour. Empty glows in the accent colour. |
