# Impious Bonum user guide

Everything Impious Bonum does and how to set it up. New here? Start with [Getting started](#getting-started). Every widget's settings, defaults and ranges are in the [widget reference](widgets.md). The pictures are drawn by the app itself, from made-up readings, so they show what it really draws without showing anyone's PC.

## Contents

- [Getting started](#getting-started): [installing](#installing), [the window and the tray](#the-window-and-the-tray-menu), [windowed mode](#windowed-mode), [backing up your settings](#backing-up-your-settings)
- [Layouts and the editor](#layouts-and-the-editor): [the default layout](#the-default-layout), [the editor](#layout-editor), [editing by hand](#editing-layouts-by-hand), [warning symbols](#warning-symbols), [sharing](#sharing-layouts), [the gallery](#layout-gallery), [pages](#pages)
- [What it shows](#what-it-shows): [metrics](#metrics), [the sensor service](#sensor-service-temperatures-fans-power-fps), [energy use](#energy-use), [weather](#weather), [calendar](#calendar), [news](#news), [custom data](#custom-data), [status checks](#status-checks), [Claude usage](#claude-usage), [microphone and camera](#microphone-and-camera)
- [Widgets in depth](#widgets-in-depth): [top apps](#top-apps), [multi-line graphs](#multi-line-graphs), [world clocks](#world-clocks), [timers](#timers), [countdowns](#countdowns), [now playing and buttons](#now-playing-and-buttons), [audio visualiser](#audio-visualiser), [images and shapes](#images-and-shapes), [photo frame](#photo-frame)
- [Automatic behaviour](#automatic-behaviour): [alerts](#alerts), [hotkeys](#hotkeys), [game layouts](#game-layouts), [session recaps](#session-recaps), [schedule](#schedule), [holiday themes](#holiday-themes), [dimming](#dimming), [logging](#logging)
- [More screens](#more-screens): [more dashboards](#more-dashboards), [tablet view](#tablet-view), [readings for other apps](#readings-for-other-apps), [USB screens](#usb-screens)
- [Command line](#command-line)

## Getting started

### Installing

Download [`ImpiousBonum-win-Setup.exe`](https://github.com/ChaoDjinn/ImpiousBonum/releases/latest/download/ImpiousBonum-win-Setup.exe) from the [latest release](https://github.com/ChaoDjinn/ImpiousBonum/releases/latest) and run it.

- It installs for your user only (no admin prompt) into `%LocalAppData%\ImpiousBonum`, adds Start menu and desktop shortcuts, and installs the .NET 10 Desktop Runtime first if you don't have it.
- On first run it fills your smallest secondary monitor. With only one screen it starts as a window on it instead (see [Windowed mode](#windowed-mode)), so it doesn't cover everything else. Open Impious Bonum (from the tray icon, or the Start menu) to move it to another screen, edit the layout, or turn on *Start with Windows* (on the *General* page).
- The app checks GitHub for updates about a minute after it starts and every six hours after that; *About and updates* → *Check for updates* checks right away and shows the download as it goes. New versions download in the background (usually as a small delta), and once one is ready *Restart to update* is on that page and in the tray, and a dot beside *About and updates* in the sidebar says so. The tray menu's first line shows the installed version.
- After an update, if the sensor service is from an older version a notification says so; *Sensors* → *Update…* brings it up to date.
- Uninstall from Windows Settings → Apps. It removes start-with-Windows and offers to remove the sensor service (one UAC prompt). Your layout and settings in `%AppData%\ImpiousBonum` are kept.
- The installer isn't code-signed yet, so Windows SmartScreen may warn the first time: choose *More info → Run anyway*.

Settings and layouts live in `%AppData%\ImpiousBonum`:

- `layouts\<name>.json`: your saved layouts (canvas size, theme and widgets), one file each. The dashboard shows one at a time; saved changes to it apply immediately. Upgrading from a version with a single `layout.json` moves it to `layouts\Default.json`, hand edits and all.
- `themes\<name>.json`: your saved themes (font, colours, gradients, glow and background), which any layout can use by name.
- `settings.json`: the weather location and units (`weatherLocation`, `weatherUnits`), [more places](#more-places) for the weather (`weatherPlaces`), [air quality and pollen](#air-quality-and-pollen) (`airQuality`, `airQualityScale`), [severe weather warnings](#severe-weather-warnings) (`weatherWarnings`, `weatherWarningsNotify`, `metOfficeArea`), [energy use](#energy-use) (`energyPrice`, `energyCurrency`, `energyOtherWatts`), [grid carbon intensity](#grid-carbon-intensity) (`gridCarbon`, `gridCarbonArea`), [pages](#pages) (`swipePages`, `rotatePagesSeconds`, `pages`), [alerts](#alerts) (`alerts`), [custom data](#custom-data) (`customData`), [status checks](#status-checks) (`statusChecks`), the [schedule](#schedule) (`schedule`, `scheduleEnabled`), `scheduleEnabled`, `holidayThemes`), [dimming when away](#dimming) (`awayDimMinutes`, `awayBrightness`), which monitor, how the window behaves (`fill`, `area`, `locked`, `clickThrough`, `alwaysOnTop`, `hideOverFullscreen`), which layout (`activeLayout`), ping host, the tablet view (`tabletView`, `tabletPort`, `tabletToken`, `tabletButtons`, `tabletReadings`), a [USB screen](#usb-screens) (`usbScreen`), [hotkeys](#hotkeys) (`hotkeys`), [calendars](#calendar) (`calendars`), [news feeds](#news) (`newsFeeds`), [logging](#logging) (`logging`), [game layouts](#game-layouts) (`gameLayouts`, `gameLayoutsEnabled`), [session recaps](#session-recaps) (`sessionRecaps`), the [gallery](#layout-gallery)'s address (`galleryAddress`), `hardwareRendering` (off by default to save memory).
- `claude-usage.json`: the plan usage Claude Code last reported (see [Claude usage](#claude-usage)).
- `energy.json`: the energy your PC has used today and this month so far, so a restart carries on from it (see [Energy use](#energy-use)).
- `dashboard.log`: startup, update checks and any errors. If something goes wrong, this is the file to attach to an issue (*General* → *Settings folder*, or *About and updates* → *Report a problem*).

To keep a copy of your settings, layouts and themes, or move them to another PC, see [Backing up your settings](#backing-up-your-settings).

### The window and the tray menu

Everything is in Impious Bonum's window: open it from the tray (*Open Impious Bonum*, or double-click the icon), or start Impious Bonum again from the Start menu while it's running. A sidebar on the left has a page for each part of the app, as in Windows' Settings, and changes take effect as you make them. The layout editor is the exception: it has *Save*, and asks before another page takes its place while it has unsaved changes. The window follows Windows' light or dark mode.

| Page | |
|---|---|
| *Layouts* | The layout editor, on the screen you pick (see [Layout editor](#layout-editor)) |
| *Gallery* | Ready-made layouts for screens like yours, to put straight on one (see [Layout gallery](#layout-gallery)) |
| *Screens* | Your monitors as Windows arranges them: which shows the main dashboard and how its window behaves, the dashboards on the others, and dimming when you're away (see [Windowed mode](#windowed-mode), [More dashboards](#more-dashboards) and [Dimming](#dimming)) |
| *Tablet and USB screen* | The dashboard in a browser on a tablet or phone, and on a small USB screen (see [Tablet view](#tablet-view) and [USB screens](#usb-screens)) |
| *Pages and schedule* | Swiping and turning between layouts, and what shows when (see [Pages](#pages) and [Schedule](#schedule)) |
| *Game layouts* | Games linked to a layout or theme, and session recaps (see [Game layouts](#game-layouts) and [Session recaps](#session-recaps)) |
| *Sensors* | Whether the sensor service is running, installing, updating or removing it, and which app the FPS reading follows (see [the sensor service](#sensor-service-temperatures-fans-power-fps)) |
| *Energy* | Your PC's power now and the energy it's used today and this month, the price and currency for what that costs, the allowance for the parts nothing measures, and Great Britain's grid carbon intensity (see [Energy use](#energy-use)) |
| *Weather* | The location, more places for the weather widget to move between, the units, air quality and pollen, severe weather warnings, and turning the weather off (see [Weather](#weather)) |
| *Calendars*, *News feeds* | The links the agenda and news widgets read (see [Calendar](#calendar) and [News](#news)) |
| *Claude usage* | Whether Claude Code is reporting, and connecting it (see [Claude usage](#claude-usage)) |
| *Custom data* | Your own metrics from web addresses and commands (see [Custom data](#custom-data)) |
| *Status checks* | Whether your own servers and services are up (see [Status checks](#status-checks)) |
| *Alerts*, *Hotkeys*, *Logging* | See [Alerts](#alerts), [Hotkeys](#hotkeys) and [Logging](#logging) |
| *General* | *Start with Windows*, drawing with the graphics card, the settings folder, backing it up and restoring it (see [Backing up your settings](#backing-up-your-settings)), and `dashboard.log` |
| *About and updates* | The version, checking for and installing updates, what's new, this guide and the widget reference for your version, *Report a problem*, and *Details* to copy into a problem report |

The tray menu keeps to the basics: *Open Impious Bonum*, *Edit layout…* (the window on the main dashboard's layout), *Layout* to switch the main dashboard's layout quickly, *Restart to update to x.y.z* when an update is waiting, and *Exit*. Its first line shows the installed version, and it's dark or light with the taskbar (Settings → Personalisation → Colours → *Choose your mode*).

Notifications that can do something say so: clicking one about an error opens the settings folder with `dashboard.log` selected, clicking *Impious Bonum x.y.z is ready* restarts into the update, and clicking a [weather warning](#severe-weather-warnings) opens it on the Met Office's or the National Weather Service's site.

### Windowed mode

Without a spare screen, *Screens* → pick the monitor → *Size* → *A window* turns the dashboard into a window on it (on a PC with only one screen it starts this way). It starts unlocked with a dashed outline: drag it to move it (onto another monitor too) and drag an edge or corner to resize it. It keeps the layout's shape, so the layout always fills it. Then turn on *Lock position*. Its size, position and monitor are remembered, and it's moved back on screen if the resolution changes.

- *Click-through when locked* lets clicks go to the window underneath. Right-click can't open the editor then; use the tray or the window.
- *Always on top* (under *All dashboards*) keeps it above other windows. *Hide over fullscreen apps* (on by default) hides it while a game or video fills its monitor, since a window over a borderless game can cost frame pacing, and it can't show over exclusive fullscreen anyway.
- *Bring to front* raises it when it's behind other windows, and *Reset size and position* recentres it.
- The canvas scales to the window, so a small window shrinks everything evenly. For a window that isn't a strip, give the layout a matching size in the editor (*Canvas*); *Match dashboard screen* uses the window's size.

*Size* → *Fill the screen* goes back to covering the whole monitor.

### Backing up your settings

*General* → *Back up and restore* → *Back up…* saves your whole setup as one `.ibbackup` file: `settings.json` (calendars, alerts, hotkeys, the schedule, custom data, which screens the dashboards are on and the rest), every saved layout with the fonts and pictures in its folder under `layouts`, and your saved themes. Keep it somewhere safe, or take it to another PC.

**A backup has private things in it**: your calendar links, the tablet link, and the web addresses and commands you've set up, for alerts and custom data among others (with any tokens in custom data's headers). Keep it somewhere only you can open, and don't share it the way you'd share a layout: to share a layout, export it instead (see [Sharing layouts](#sharing-layouts)).

*Restore…* shows what's in a backup: when it was made, by which version of Impious Bonum, and how many layouts and saved themes it has. Once you say yes, Impious Bonum first saves your setup as it is now in a folder beside the settings folder (`%AppData%\ImpiousBonum backups`, as *Before restoring 2026-10-16 14.05.ibbackup*), so you can restore that to go back. Then it closes, puts the backup's settings, layouts and themes in place of yours, and starts again. Layouts and themes that aren't in the backup go, though they're in the one saved before restoring.

- A backup from a newer version of Impious Bonum says so first: this version would drop the settings it doesn't know about. Update first (*About and updates*) to keep everything.
- Left out: `dashboard.log`, the `logs` folder and `sessions.csv`, which are records rather than settings (restoring them would lose what's been recorded since), and `claude-usage.json`, which Claude Code keeps up to date. *Start with Windows* and the sensor service belong to Windows rather than the settings folder, so restoring leaves them as they are.
- Fonts and pictures a layout uses from elsewhere, such as your Pictures folder, aren't in a backup. To take a layout with them to another PC, export it.
- On another PC, a layout's fonts and pictures from its folder under `layouts` work as before. The dashboards look for the screens they were on, so choose screens for them on *Screens*.
- Only restore your own backups: a backup sets the programs and web addresses that custom data, alerts and buttons use. Restoring checks the file as importing a layout does: it refuses one with `..` or absolute paths in it, caps sizes (100 MB a file, 1 GB in all), and takes only settings, layouts with their fonts and pictures, and themes.

## Layouts and the editor

### The default layout

Out of the box it fills a 1920×480 screen like this (any size works: the canvas scales to fit, and you can change the design size):

<p align="center"><img src="images/default-layout.svg" alt="Map of the default layout: CPU, GPU and memory graphs, CPU and GPU temperatures and the frame rate, a clock, drives, and network download, upload and ping"></p>

Every box is a widget you can move, resize, restyle or replace. Twenty-three widget types are available: `text`, `graph` (line or area history with a header), `lines` ([Multi-line graphs](#multi-line-graphs)), `stat` (a big value with a trend line and its lowest and highest), `gauge` (a dial: 270° arc, semicircle or ring), `cores` (every CPU thread's load as bars or a heatmap), `icon` (accent icon + value), `clock` (with other time zones too: [World clocks](#world-clocks)), `timer` ([Timers](#timers)), `drives`, `processes` (the apps using the most CPU or memory, like a small Task Manager), `rows`, `bars` (labelled progress bars), `media` and `buttons` ([Now playing and buttons](#now-playing-and-buttons)), `visualizer` ([Audio visualiser](#audio-visualiser)), `weather` ([Weather](#weather)), `agenda` ([Calendar](#calendar)), `news` ([News](#news)), `status` ([Status checks](#status-checks)), `image` and `shape` ([Images and shapes](#images-and-shapes)) and `claude` ([Claude usage](#claude-usage)). Every setting, its default and its range is in the [widget reference](widgets.md).

<table>
  <tr>
    <td width="50%"><img src="images/widgets/gauges.png" alt="Three gauges: a 270° dial of the CPU temperature at 65, a half dial of GPU load at 90.4 % and a ring of memory use at 55 %"><br>Gauges: a dial, a half dial and a ring</td>
    <td width="50%"><img src="images/widgets/cores.png" alt="Sixteen CPU threads as a four-by-four heatmap, brighter where busier, beside bars for CPU, GPU, RAM and GPU memory"><br>CPU threads as a heatmap, and bars</td>
  </tr>
</table>

### Layout editor

The editor is the *Layouts* page of the window, which opens on your main screen: tray → *Edit layout…*, or right-click / press and hold on the dashboard. Right-click or press and hold on one of [more dashboards](#more-dashboards) to edit that one's layout instead. At the top, *Screen* picks which screen's dashboard you're editing and *Layout* which saved layout it shows:

- **Preview:** click a widget to select it, drag to move, drag the handles to resize. Moves snap to a grid (toolbar); hold Alt to place freely. Arrow keys nudge, Shift+arrows by 10.
- **Snapping to other widgets:** while you drag or resize a widget, its edges and centre snap to other widgets' edges and centres, and to the canvas's edges and middle, once they're within a few pixels. A thin line shows what it has lined up with. Resizing snaps only the edges you're dragging. Hold Alt to place freely.
- **Several widgets at once:** Ctrl+click or Shift+click widgets in the preview or the *Widgets* list to add them to the selection (or take one out again), drag a box around them on empty canvas (with Ctrl or Shift held to add to what's selected), or press Ctrl+A for all of them. Drag any of them to move them all; arrows nudge them all; *Duplicate* (Ctrl+D) and *Delete* (Del) work on all of them. Click one of them to go back to just that one, or press Esc for none. The properties say how many are selected; select one to change its settings.
- **Align:** with two or more selected, *Align ▾* on the toolbar (or *Align* in the list's right-click menu) lines them up by their left edges, centres, right edges, tops, middles or bottoms, against the space they take up together. *Distribute horizontally* and *Distribute vertically* put equal gaps between three or more, keeping them within the space they take up now. *Same width* and *Same height* make them as wide or as tall as the first one you selected. Each is one undo.
- **Metrics:** template and metric fields have a `{…}` button that opens a searchable list of every metric (about 280 on a typical PC, including every raw sensor) with live values. Pick one and choose how to show it (e.g. `18.7 GB`, `19,138 MB` or just `18.7`) and it's inserted at the cursor. Under each field a live preview shows what it renders now and flags unknown metric ids.
- **Properties:** every setting of the selected widget, with the right control for each (numbers, colours with a picker, fonts, drop-downs, lists, boxes to tick such as a news widget's feeds) and a reset-to-default button. Click empty canvas or press Esc for canvas size and theme. To reorder a list (a rows widget's rows, a clock's other places, buttons, warning colours, the feeds ticked…), drag an entry by the handle at its left, or move it a place at a time with Alt+Up and Alt+Down from its handle or one of its text or number boxes (in a drop-down, they open its list instead).
- **Fonts:** the font list shows each font's name in that font, so you can see what you're picking. Symbol and icon fonts, such as *Wingdings* and *Segoe MDL2 Assets*, show their name as usual with a few of their symbols beside it. While the list is open, the font under the mouse (or the one you move to with the arrow keys) shows on the widget straight away: click it or press Enter to keep it, or press Esc or click elsewhere to go back to the font you had. With the list closed, Up and Down change to the next font straight away, and one undo goes back to where you started. You can still type a font's name. A widget's own *Font* has *Theme font* at the top of the list, to follow the layout's theme again.
- **Setting a widget up:** the weather, agenda, news and Claude usage widgets need something set up outside the layout. Select one and *Set up*, at the top of its properties, says what's set up now and has a button for it: *Set location…*, *Calendars…*, *News feeds…* or *Connect Claude Code…*. They do the same as the window's own pages.
- **Widgets:** add, duplicate (Ctrl+D) and delete. The *Widgets* list is the drawing order, and lower in the list draws on top: drag a widget up or down it to change the order, or press Ctrl+] to bring the selected widget forward and Ctrl+[ to send it back. Right-click a widget in the list for *Bring forward*, *Send back*, *Duplicate*, *Delete* and *Align*.
- **On the dashboard itself:** while the editor is open, the dashboard shows outlines and finger-sized handles. Tap a widget to select it, drag to move, drag a handle to resize, or drag across empty space to select the widgets inside; the editor follows along (and vice versa), with the same snapping and undo.
- **Themes:** with the canvas selected, *Theme* picks a saved theme for the layout, each shown with a sample of its colours. *Orange* (the default look) and 55 others are built in: clean ones like *High Contrast*, *Swiss* and *Glacier*, *Clear Sight* for [colour blindness](#warning-symbols), retro ones like *Amber*, *8-Bit*, *Desktop 95* and *Blue Screen*, moods like *Outrun*, *Cyberpunk*, *Grimoire*, *Aurora* and *Gatsby*, a *Night Watch* theme in dim reds for a screen you keep on at night, well-known editor palettes such as *Dracula*, *Nord*, *Gruvbox*, *Solarized* and *Catppuccin*, and ones for the holidays through the year: *Lunar New Year*, *Valentine*, *Shamrock* (St Patrick's Day), *Easter*, *Eid*, *Diwali*, *Halloween*, *Day of the Dead*, *Fireworks* (Bonfire Night or New Year's Eve), *Hanukkah*, *Christmas* and *Gingerbread*, which [Holiday themes](#holiday-themes) can show on their days. Colours and fonts you change below it apply to this layout only (↺ goes back to the theme's value). *Save theme as…* keeps the current look as a new theme for other layouts, *Update theme* writes your changes into a theme of your own (restyling every layout that uses it), and *Detach* copies the theme's values into the layout so later changes to the theme don't affect it. Built-in themes can't be changed; *Save theme as…* makes your own copy.
- **Moving backgrounds:** the theme's background *Image* can be an animated GIF or a video (MP4, M4V or WMV), which plays muted and on a loop behind the widgets, with the same fit and opacity settings as a picture. GIF frames are prepared once at the size the canvas shows them and kept under 48 MB in all (a long GIF is made smaller, then thinned), and fast GIFs are held to 20 frames a second. A moving background stops while the dashboard is hidden. The tablet view and USB screens show a still frame. Moving backgrounds cost more CPU than a still picture, a video most of all, since the dashboard draws without the graphics card unless you turn hardware rendering on. Exported layouts carry a video up to 50 MB.
- **A widget's own look:** every widget has *Widget theme* settings (font, weight and the theme's colours) that override the layout's theme for that widget alone; leave them empty to follow the theme. Named colours in its other settings, like `accent`, then mean the widget's own. Many widgets also have colours of their own, such as a bar's track or *Now playing*'s progress bar, so changing the theme's bar track doesn't change everything that happens to use it.
- **Text that fits:** a big font size, or a font that's wider or taller than the theme's, can make text too big for its widget. Widgets with text have *Shrink to fit*, on by default: text that would be cut off gets smaller, evenly, until it fits, and a label and its value on one line get smaller together rather than run into each other. Text that fits keeps exactly the size you set; turn it off to keep your sizes whatever happens. Names that end with … when they're too long, as in *Top apps*, still do, down to their first few letters. *Agenda* and *News* end long lines with … too, and rather than shrink the whole list, they leave out the rows at the bottom that would be cut off; a first row too tall for the widget on its own gets smaller.
- **Gradients and glow:** under *Gradients and glow* in the theme's settings, *Accent fades to* makes graphs, bars, gauges and icons fade from the accent to a second colour; *Background fades to* fades the background from top to bottom; and *Glow* (0 to 1) puts a soft neon halo around every widget's text and shapes, in the accent colour or its own. *Outrun*, *Vaporwave*, *Aurora*, *Magma*, *Cyberpunk* and *Toxic* use them. The glow costs a little CPU, since the dashboard draws without the graphics card by default.
- **Saved layouts:** the editor edits the layout the dashboard is showing. The *Layout* drop-down switches to another (asking first if you have unsaved changes), and *Layouts ▾* has *Save as…* (your changes as a new layout), *Rename…*, *Duplicate…*, *Get one from the gallery…*, *Export…*, *Import…*, *Open layout file* and *Delete* (not the last one). Keep a "Gaming" and a "Desktop" layout, for example, and flip between them from the tray's *Layout* menu.
- **More than one screen:** with [more dashboards](#more-dashboards), *Screen* moves between them. Pick a screen to edit its dashboard's layout; *Layout* and *Layouts ▾* then work on that screen's dashboard, and the others keep theirs.
- The real dashboard shows your changes live while you edit. **Save** (Ctrl+S) writes the layout's file; leaving the page or closing the window without saving (it asks first) puts the dashboard back as it was. Undo/redo with Ctrl+Z / Ctrl+Y.

<p align="center"><img src="images/editor.png" alt="The Impious Bonum window on its Layouts page: the sidebar of pages, the screen and layout pickers, the widget list, the layout's preview with the GPU graph selected, and the graph's settings"></p>

### Editing layouts by hand

Each layout is a JSON file in `%AppData%\ImpiousBonum\layouts` (*Layouts ▾* → *Open layout file* in the editor opens the one being edited). Saving the shown layout's file applies it straight away; changes to other layouts show when you switch to them. To add a layout by hand, drop a new `.json` file in the folder and pick it from tray → *Layout*.

The canvas has a design size (default 1920×480) and scales to fit the window. Each widget has `type`, `x`, `y`, `width`, `height` plus its own settings. Text uses templates:

```json
{ "type": "text", "x": 58, "y": 296, "width": 427, "height": 76, "fontSize": 64, "text": "{gpu.vram.used:N0 MB}" }
```

`{id}` formats automatically (`18.7 GB`, `11.8 %`, `79.7 KB/s`). After a colon you can add a number format (`0.0`, `N0`), force a byte unit (`MB`, `GB`, …) or write `nounit`. Readings that aren't available show as `—`.

To make a value change colour when it runs hot, give its `text`, `icon`, `graph` or `drives` widget warning colours (in the editor, or as `thresholds` in the layout file): `[{ "above": 80, "color": "warning" }, { "above": 90, "color": "critical" }]`. `warning` and `critical` are theme colours, and `below` works too, e.g. for a low frame rate. With the theme's `warningSymbols` on, a value in `warning` also has a ▲ beside it and one in `critical` a ◆ (see [Warning symbols](#warning-symbols)).

If a layout has a typo or an out-of-range value, the tray shows a notification naming the file and saying what and where, e.g. *unknown setting 'fontsize' (did you mean 'fontSize'?)*.

A layout's `theme` can be its own values, a saved theme's name (`"theme": "Ice"`), or a saved theme with a few values changed for this layout: `"theme": { "base": "Ice", "accent": "#FF4081" }`. Saved themes are files in `%AppData%\ImpiousBonum\themes` holding the same settings; saving one restyles every layout that uses it straight away. An unknown theme name is reported like any other typo, and the layout falls back to the default look.

To use a font you don't want to install, point the theme at the file: `"fontFile": "C:\\Fonts\\SomeFont-Light.otf"`.

To put a picture behind the widgets, add `"backgroundImage": "C:\\Pictures\\wallpaper.jpg"` to the theme. `backgroundFit` is `fill` (the default: covers the canvas, cropping the edges), `fit`, `stretch`, `center` or `tile`, and `backgroundOpacity` below 1 lets the `background` colour show through, which dims a busy picture on black. The image is decoded at the canvas size, so a 4K wallpaper doesn't cost extra memory. If the file is missing the dashboard just shows the colour. The full list of theme settings is at the end of the [widget reference](widgets.md#theme).

### Warning symbols

Warning colours change a value's colour as it runs hot, but with colour blindness warning and critical can look alike, or like the value's usual colour. Tick *Warning symbols* in the theme's *Colours* (select the canvas in the editor), or add `"warningSymbols": true` to a theme, and a value in the theme's warning colour gets a ▲ beside it too, and one in its critical colour a ◆. The shape says which it is, whatever the colour looks like.

- Text, *Icon and value* and stat tiles have it after the value; graphs, bars, drives and *Top apps* have it just before the value on the right; a gauge has it after the value in the middle of the dial.
- It takes no room while the value is normal. With *Shrink to fit* on, text that only just fits gets a little smaller to make room for it.
- *CPU threads* keeps to colour alone: a symbol in every thread would be too busy to read.
- Only the theme's `warning` and `critical` colours get one. A warning colour of your own, such as `#FF00FF` or `accent`, doesn't, since only you know what it means.
- It's off unless you turn it on, so layouts look as they always have.

The built-in *Clear Sight* theme has it on, with colours picked to stay apart with each common kind of colour blindness (red-blind, green-blind and blue-blind): a sky blue accent, gold for warning and a deep pink-red for critical, the gold much lighter than the red so they stay apart on a dim screen too. To keep another theme's look with the symbols, tick *Warning symbols* for the layout, or *Save theme as…* a copy with it ticked.

### Sharing layouts

*Layouts ▾ → Export…* in the editor saves the layout you're editing as one `.ibl` file, with its font file, background image and image widgets' pictures inside, so it works on another PC. A layout using one of your saved themes takes the theme's values with it; built-in themes stay as names. *Import…* (in the editor's *Layouts ▾*) adds it to your saved layouts, asking before replacing one with the same name. The font and image go in `layouts\<name>\` and are removed when no layout uses them any more. Metrics the other PC doesn't have, such as a particular sensor, show `—` as usual. A [photo frame](#photo-frame)'s folder isn't exported: it stays on your PC, and whoever imports the layout chooses their own.

A layout from someone else could have a harmless-looking button that runs any program, or one that sends a web request to something on your network, so if it has buttons that open something or [send web requests](#web-request-buttons), the import lists what they'd open and where they'd send, and asks whether to keep them or clear them. Exporting leaves out web buttons' headers, which are usually a password or token.

An `.ibl` file is a zip of `layout.json`, `manifest.json` and an `assets` folder. Imports only take the layout and the font (`.ttf`, `.otf`) and images it names, reject a file with `..` or absolute paths in it, and cap sizes (1 MB for the layout, 50 MB per file).

### Layout gallery

The *Gallery* page lists ready-made layouts, each with its size, how many widgets it has and a description. Under
*For*, pick one of your screens to see only the layouts whose shape suits it, closest in size first. Under *Showing*,
choose *A few big things* (up to 6 widgets) or *Lots at once*: a TV across the room or a small panel suits a few big
things, while a monitor at arm's length can show lots. Pick a layout to see it drawn live with your own readings.
*Show on* puts it on the screen you picked (*Add a dashboard on* gives a screen without one a dashboard), then *Edit
it* opens it in the editor; with *Any screen*, *Add to my layouts* only adds it. It's added the same way as importing
a file: you're asked before a layout with the same name is replaced, and before keeping buttons that open programs
or send web requests. Pictures and fonts inside a shared layout show once it's added. In the editor, *Layouts ▾* →
*Get one from the gallery…* opens it for the screen you're editing.

So far it has twenty-one, made along with the app, for screens from a 3.5" USB screen (480 × 320), a 7" panel
(1024 × 600) and a 1280 × 800 tablet through 1920 × 480 and 1920 × 720 bars to 1080p, 1440p and 4K monitors, a TV
across the room, a monitor turned on its side and a tall strip inside a PC case. Some are for one thing: music with an
audio visualiser, the weather in detail, the news, your network, streaming, or counting down to Christmas or
Halloween. Yours could be next.

The gallery is the [`gallery`](../gallery) folder of this repository. To share a layout, export it (*Layouts ▾ →
Export…* in the editor), add the `.ibl` file and an entry in [`gallery/index.json`](../gallery/index.json), and open a
pull request. A plain layout `.json` works too if it has no pictures or fonts. The tests check every gallery layout
opens and is valid, and that its widget count is right. To use a gallery of your own instead, set `"galleryAddress"`
in `settings.json` to a web folder, or a folder on this PC, with an `index.json` in it.

### Pages

A small strip fits more when it has pages. *Pages and schedule* → *Pages*:

- **Swipe between pages:** swipe left across the dashboard for the next layout, right for the previous one (a mouse drag works too). A row of dots shows which page you're on. A press that turns into a swipe doesn't count as a tap on a button.
- **Turn pages by themselves:** move to the next page every 15 seconds to 5 minutes. Rotation waits a minute after you touch the dashboard, and doesn't run while you edit the layout or while a [game's layout](#game-layouts) is showing.
- **The pages:** tick the layouts that are pages. With none ticked, every layout is a page.

Turning a page is the same as picking that layout from the tray, so it's remembered. Swiping is off while the window can be dragged (windowed and unlocked) and while you edit the layout. Stored as `swipePages`, `rotatePagesSeconds` and `pages` in `settings.json`.

## What it shows

### Metrics

| Metric ids | Source |
|---|---|
| `cpu.load`, `cpu.threads` | `GetSystemTimes` |
| `cpu.clock` (effective MHz, like Task Manager's *Speed*), `cpu.core.<N>.load` (each logical processor, from 0) | Processor performance counters |
| `mem.used`, `mem.total`, `mem.available`, `mem.load` | `GlobalMemoryStatusEx` |
| `mem.commit.used`, `mem.commit.total`, `mem.commit.load` (committed memory against RAM + page file) | `GlobalMemoryStatusEx` |
| `gpu.name`, `gpu.load`, `gpu.vram.used`, `gpu.vram.total`, `gpu.vram.free`, `gpu.vram.usedPct`, `gpu.shared.used` | GPU performance counters + DXGI (same numbers as Task Manager) |
| `gpu.load.3d`, `gpu.load.decode`, `gpu.load.encode`, `gpu.load.compute` | GPU engine counters, the busiest engine of each kind |
| `disk.<L>.free/used/total/usedPct/label` | `DriveInfo`, follows drives as they come and go |
| `disk.<L>.read/write/active`, `disk.read`, `disk.write` | Disk performance counters: speed per drive and in total, and % of time active |
| `disk.<L>.life`, `disk.<L>.temp`, `disk.<L>.status`, `disk.<L>.model`, `disk.<L>.hours` | Sensor service: the drive's health (life remaining %, like CrystalDiskInfo), temperature, status (Good / Caution / Bad), model and power-on hours, from its SMART data |
| `net.down`, `net.up`, `net.down.total`, `net.up.total`, `net.ip`, `net.adapter` | Adapters with a default gateway (totals count since the adapter connected) |
| `net.<adapter>.down`, `net.<adapter>.up`, `net.<adapter>.speed` (link speed, e.g. *1 Gbps*), `net.<adapter>.ip` | One adapter, from when it's first seen connected, e.g. `net.ethernet.down` or `net.wi-fi.speed`: its name in lower case with a hyphen for spaces and punctuation. They're listed in the metric picker under *Network: Ethernet* and so on, and stay (empty) while it's disconnected |
| `wifi.signal` (%), `wifi.rssi` (dBm), `wifi.ssid` (the network's name) | The Wi-Fi connection, every 5 seconds, on PCs with Wi-Fi. Read without location access, so Windows never asks for it or shows its location icon |
| `net.ping` | ICMP to `1.1.1.1` (configurable) |
| `sys.uptime`, `sys.name`, `sys.battery`, `sys.battery.status`, `sys.battery.time` (time left on battery, e.g. *2h 14m*) | Windows; the battery ones only on PCs with a battery, which includes a UPS plugged in by USB that Windows shows as one |
| `top.cpu.<N>`, `top.cpu.<N>.name`, `top.memory.<N>`, `top.memory.<N>.name` (N = 1 to 5) | The apps using the most CPU and memory, every 2 seconds, from one `NtQuerySystemInformation` call like Task Manager's. An app's processes count together (all of Chrome's are one); CPU is a share of the whole processor, memory is the private working set (Task Manager's *Memory* column) |
| `weather.temp`, `weather.feels`, `weather.high`, `weather.low`, `weather.condition`, `weather.humidity`, `weather.wind`, `weather.wind.direction`, `weather.rain`, `weather.sunrise`, `weather.sunset`, `weather.tomorrow.high`, `weather.tomorrow.low`, `weather.tomorrow.condition`, `weather.location` | [Open-Meteo](https://open-meteo.com) forecast for the place set on the *Weather* page, every 15 minutes |
| `weather.gusts`, `weather.wind.degrees`, `weather.pressure`, `weather.pressure.trend` (*Rising*, *Falling* or *Steady* over the last 3 hours), `weather.dewpoint`, `weather.visibility`, `weather.clouds`, `weather.precipitation` (the last 15 minutes), `weather.precipitation.today`, `weather.rain.hour`, `weather.tomorrow.rain`, `weather.uv`, `weather.uv.max` (today's highest), `weather.uv.level` (*Low* to *Extreme*), `weather.daylight` | The same forecast (see [Weather](#weather)) |
| `weather.sun.elevation` | How high the sun is, in degrees, worked out on your PC from the place and the time, every 30 seconds |
| `weather.rain.soon` (e.g. *Rain from 14:30*), `weather.rain.next` (minutes until it rains, 0 while it is) | The same forecast's next two hours, a quarter of an hour at a time (see [Rain soon](#rain-soon)) |
| `weather.aqi`, `weather.aqi.level`, `weather.aqi.european`, `weather.aqi.us`, `weather.pm25`, `weather.pm10`, `weather.ozone`, `weather.no2` | Open-Meteo's air quality (from CAMS) for the same place, every hour, once *Weather* → *Air quality and pollen* is on (see [Air quality and pollen](#air-quality-and-pollen)) |
| `weather.pollen` (e.g. *Grass high*), `weather.pollen.grass`, `weather.pollen.tree`, `weather.pollen.weed`, and `weather.pollen.grass.level`, `weather.pollen.tree.level`, `weather.pollen.weed.level` (*None* to *Very high*) | The same, for places in Europe in season |
| `weather.warnings` (how many), `weather.warning` (the most serious one's headline), `weather.warning.level` (e.g. *Yellow* or *Severe*) | Warnings of severe weather for the same place, from the Met Office in the UK or the National Weather Service in the US, every 10 minutes, once *Weather* → *Severe weather warnings* is on (see [Severe weather warnings](#severe-weather-warnings)) |
| `calendar.next.title`, `calendar.next.time`, `calendar.next.location`, `calendar.next.minutes`, `calendar.now.title`, `calendar.today`, `calendar.status` | Your calendars' iCal links, set on the *Calendars* page, every 15 minutes (see [Calendar](#calendar)) |
| `calendar.<name>.next.title`, `calendar.<name>.next.time`, `calendar.<name>.next.location`, `calendar.<name>.next.minutes`, `calendar.<name>.now.title`, `calendar.<name>.today` | The same for one calendar, e.g. `calendar.work.next.title` for a calendar called *Work* |
| `session.game`, `session.minutes`, `session.playing`, `session.<x>.avg/min/max` for `fps`, `low1` (1% low FPS), `cpu`, `gpu` (loads), `cputemp`, `gputemp` | The game session going on now, or the last one, for a game linked under *Game layouts* (see [Session recaps](#session-recaps)) |
| `news.title`, `news.source`, `news.age`, `news.status` | The newest headline from your news feeds, set on the *News feeds* page, every 15 minutes (see [News](#news)) |
| `custom.<source>.<field>`, `custom.<source>.status` | Your own: web addresses and commands set up on the *Custom data* page (see [Custom data](#custom-data)) |
| `media.title`, `media.artist`, `media.album`, `media.app`, `media.status`, `media.position`, `media.duration`, `media.progress` | What's playing, from Windows' media sessions (the media controls in the volume flyout): Spotify, browsers, Media Player and so on. No reading while nothing is playing |
| `mic.muted` (1 when muted, 0 when not), `mic.name`, `mic.inuse`, `mic.apps`, `camera.inuse`, `camera.apps` | The microphone Windows uses for calls, and the apps using it or the camera, every 2 seconds (see [Microphone and camera](#microphone-and-camera)). The microphone's appear on PCs with a microphone, the camera's once an app has used a camera |
| `cpu.temp`, `cpu.power`, `gpu.temp`, `gpu.hotspot`, `gpu.power`, `gpu.fan` | Sensor service (below) |
| `fps`, `fps.app` | Sensor service counts frames per app (like PresentMon/HWiNFO): DirectX, Vulkan and OpenGL, without hooking into the game; *Sensors* → *FPS from* picks the foreground app or the top app on a chosen monitor |
| `fps.frametime`, `fps.frametime.max`, `fps.low1`, `fps.low01` | Sensor service, for the same app: average frame time, the slowest frame in the last second, and the 1% and 0.1% lows over the last 30 seconds (see [Frame times](#frame-times)) |
| `hw/...` (every sensor LibreHardwareMonitor finds: clocks, voltages, fans, per-core loads, …) | Sensor service. Run `ImpiousBonum.Sensors.exe list` to see the ids on your machine |
| `sensors.status` | Text describing the sensor service connection |
| `power.total`, `energy.today`, `energy.month`, `energy.cost.today`, `energy.cost.month` | The whole PC's power (the sensor service's CPU and graphics card power, and an allowance for the rest), the energy it's used today and this month, and what that costs at your price (see [Energy use](#energy-use)) |
| `grid.carbon`, `grid.carbon.index`, `grid.carbon.best`, `grid.carbon.region` | Great Britain's grid carbon intensity from NESO, every 30 minutes, once *Energy* → *Grid carbon intensity* is on (see [Grid carbon intensity](#grid-carbon-intensity)) |
| `claude.session`, `claude.week`, `claude.session.resets`, `claude.week.resets`, `claude.status` | Your Claude plan's usage limits, from Claude Code's status line (see [Claude usage](#claude-usage)) |

### Sensor service (temperatures, fans, power, FPS)

Reading CPU temperatures needs admin rights and a kernel driver, so it lives in a separate process: `sensors\ImpiousBonum.Sensors.exe`, built on [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). The dashboard itself stays unelevated and receives readings over a read-only named pipe.

```mermaid
flowchart LR
    lhm["LibreHardwareMonitor<br/>+ PawnIO driver"] --> sensors
    etw["Frame events: DirectX,<br/>Vulkan, OpenGL (ETW)"] --> sensors
    subgraph svc["Windows service (elevated)"]
        sensors["Sensor service<br/>ImpiousBonum.Sensors.exe"]
    end
    subgraph you["Your session (no admin)"]
        dash["Dashboard<br/>ImpiousBonum.exe"]
    end
    win["Windows APIs<br/>CPU, memory, GPU,<br/>drives, network"] --> dash
    sensors -- "read-only<br/>named pipe" --> dash
```

- **Install:** *Sensors* → *Install…* (one UAC prompt). The host is copied to `Program Files\Impious Bonum\Sensors` and registered as an auto-start service, so there are no prompts at logon.
- **CPU temperatures** also need the signed [PawnIO](https://pawnio.eu) driver (`winget install namazso.PawnIO`). HWiNFO and FanControl install it too. GPU sensors work without it.
- The service only reads hardware while a dashboard is connected. Its log is in `%ProgramData%\ImpiousBonum\sensors.log`.
- For development: `ImpiousBonum.Sensors.exe run` serves from a console, and `list` prints every sensor. Both work unelevated with fewer sensors. `install` and `uninstall` (as admin) do what the *Sensors* page's buttons do.

#### Frame times

Average FPS hides stutter: a game can average 120 FPS and still hitch. The sensor service also times every frame of the app the FPS follows:

- `fps.frametime`: the average frame time, e.g. `8.3 ms`.
- `fps.frametime.max`: the slowest frame in the last second. Put it in a `graph` widget for a frame-time graph: a flat line is smooth, each spike is a stutter you felt.
- `fps.low1` and `fps.low01`: the 1% and 0.1% lows, the frame rate of the slowest 1% (or 0.1%) of frames over the last 30 seconds. A 1% low close to the average means smooth play. They appear once there are enough frames (100, and 1,000). Pauses longer than a second, like alt-tabbing or a loading screen, don't count.

<p align="center"><img src="images/widgets/frame-times.png" width="600" alt="Stat tiles of the frame rate at 145 and the 1% and 0.1% lows, each with a trend line and its range, and a graph of the average and slowest frame times"></p>

A sensor service from before 0.8 doesn't send these; *Sensors* → *Update…* brings it up to date.

### Energy use

What your PC is using and what it costs, as readings for any widget. The *Energy* page shows them too, and is where you set your price.

- **Power now:** `power.total`, in watts: the CPU's package power and the graphics card's, from the [sensor service](#sensor-service-temperatures-fans-power-fps), plus an allowance for the parts nothing measures (the motherboard, memory, drives and fans). The allowance is 50 W, which suits most desktops; *Energy* → *Everything else* changes it. Monitors, speakers and anything else plugged in aren't counted, and nor is what the power supply itself loses (often a tenth or so): raise the allowance if you'd like that counted.
- **Energy used:** `energy.today`, in kWh since midnight, and `energy.month`, since the 1st, added up from `power.total` every second.
- **What it costs:** `energy.cost.today` and `energy.cost.month`, at the price you type under *Energy* → *Price per kWh*: `0.25`, say, or `24.5p` in pence or cents. They show your region's currency symbol where your region puts it, such as *£0.48*; *Currency symbol* changes it. Without a price they have no reading. The cost is the energy so far at the price now, so a new price applies to the whole day and month. Standing charges aren't included.

Without the sensor service there's no power reading, so `power.total` shows a dash and nothing is counted; the *Energy* page says why. The CPU's power needs the PawnIO driver, as CPU temperatures do: without it, only the graphics card's and the allowance are counted, and the page says so. Some built-in graphics don't report their power, and then it's the CPU's and the allowance.

Energy is only counted while Impious Bonum is running: time it isn't, or the PC is asleep, isn't counted. Today's and this month's totals are kept in `energy.json` in the settings folder, saved every minute, so a restart carries on from them. Today's starts again at midnight, and the month's on the 1st.

For example, `{power.total} · {energy.today} today ({energy.cost.today})` in a text widget reads *433 W · 1.92 kWh today (£0.48)*, and a `graph` of `power.total` shows the power climb when a game starts.

#### Grid carbon intensity

Turn on *Energy* → *Grid carbon intensity* to see how much CO₂ Great Britain's electricity is giving off: the more wind and sun, the less. It's only for Great Britain (England, Scotland and Wales), as Northern Ireland's grid is separate, and it's off until you turn it on.

- `grid.carbon`: the carbon intensity now, in grams of CO₂ for each kWh, e.g. *142 gCO₂/kWh*.
- `grid.carbon.index`: its level: *Very low*, *Low*, *Moderate*, *High* or *Very high*.
- `grid.carbon.best`: when it'll be lowest in the next 24 hours, e.g. *Lowest around 03:30*, or *Lowest now*: a good time for a long game update, or the washing machine.
- `grid.carbon.region`: whose figures they are: *Great Britain*, or your region's name.

The figures are for Great Britain as a whole unless you give *Your area*: the first half of your postcode, such as `LS1`, for your region's (Yorkshire, say). Type the whole postcode if you like; only the first half is kept.

Every 30 minutes, the next 24 hours' forecast comes from the [Carbon Intensity API](https://carbonintensity.org.uk), run by NESO, Great Britain's National Energy System Operator (formerly National Grid ESO). Nothing about you is sent but the first half of your postcode, if you give it, and nothing at all while it's off. Between fetches the readings move on through the forecast, half an hour at a time. If it can't be reached it tries again five minutes later, and the last figures stay for up to three hours. Carbon intensity forecasts by NESO, from carbonintensity.org.uk, under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

### Weather

The *Weather* widget shows an icon, the temperature and conditions now (with today's high and low and what it feels like, or other details you choose), and a row of the days or hours ahead, every three hours if you like: up to 16 days, or 48 hours. Every `weather.*` metric works in other widgets too: `{weather.temp} · {weather.condition}` in a text widget, or a gauge of `weather.temp`.

<p align="center"><img src="images/widgets/weather.png" width="600" alt="The weather widget with colour icons: 14° and partly cloudy now with the high, low and feels-like, four days ahead and a yellow wind warning under them, and below it a second weather widget of every three hours ahead with the chance of rain"></p>

1. *Weather* → *Set location…*, or in the editor select the widget and *Set location…* under *Set up* (adding the widget offers it too). Search for your town, area or postcode, or type latitude and longitude, e.g. `51.5, -0.12`.
2. Add the widget: *Add widget* → *Weather* in the editor.

For a suburb or village, or one of several places with the same name, add its town, county or country after a comma: `West Derby, Liverpool` finds the one in Liverpool rather than the one in South Africa. The start of a name is enough (`West Derby, Liv`), and `UK` and `USA` work as countries. Each place in the list says where it is, and hovering over it shows its coordinates.

It's adjustable in the editor: turn off *Weather now* to leave just the forecast (say, one widget of the hours ahead beside another of the days), choose *colour* icons for a yellow sun, white clouds, grey rain clouds with blue rain and white lightning instead of outlines in one colour, and give the widget its own text and detail colours rather than the theme's. A day or hour shows its chance of rain from 30%; *Chance of rain from* changes that, and 0 always shows it.

Choose what the widget shows beside the temperature under *Details to show* in the editor, ticking them and dragging them into order by their handles:

- **Conditions**, such as *Partly cloudy*, and today's **high and low** (*H 16° · L 9°*).
- **Feels like** (*Feels 13°*), **humidity** and **dew point**.
- **Wind** speed and direction (*12 km/h SW*) and **gusts** (*Gusts 40 km/h*).
- **Pressure**, with an arrow for whether it's rising (↑), falling (↓) or steady (→) over the last three hours (*1012 hPa ↓*).
- **UV**, with its level (*UV 3 Moderate*).
- Today's **chance of rain** (*Rain 60%*) and **rainfall** (*Rainfall 2.4 mm*).
- **Sunrise**, **sunset** and today's **daylight** (*Daylight 10 h 49 min*).
- **Visibility** and **cloud cover**.
- **Air quality**, by its level (*Air quality Fair*), and **pollen** (*Pollen Grass high*), once [air quality and pollen](#air-quality-and-pollen) are on; until then they're left out.

With the conditions first they have the larger line, as they always have, and the rest go a few to a line under them, with a short label where the number alone could be anything. A widget starts with the conditions, high and low and feels like, which is what it has always shown, and turning off *Details* leaves them all out. Under each day or hour, *Forecast details* chooses between its chance of rain (from *Chance of rain from*), rainfall (only when there's some, so dry days stay clear), wind, gusts and UV: for a day, its total rain, its strongest wind and gusts and its highest UV. Each has a row of its own, so they line up across the days or hours even where one is missing. Units are the forecast's, as for the readings below, and with *Shrink to fit* on (as it is unless you turn it off) a busy widget gets smaller as a whole to fit. On a [place](#more-places) other than your location, the details are that place's own, without air quality and pollen, which are only fetched for your location. They all come from the forecast already fetched, so choosing more sends nothing more.

The same readings, and a few more, work in any widget, for text, gauges and the rest:

- **Wind:** gusts (`weather.gusts`) as well as the speed, and the direction in degrees (`weather.wind.degrees`, 0 for a wind from the north) as well as `SW`.
- **UV:** the index now (`weather.uv`), today's highest (`weather.uv.max`), and the level now in the WHO's words (`weather.uv.level`): *Low* (up to 2), *Moderate* (3 to 5), *High* (6 and 7), *Very high* (8 to 10) or *Extreme* (11 and up).
- **Pressure:** at sea level (`weather.pressure`), and whether it's *Rising*, *Falling* or *Steady* (`weather.pressure.trend`): a change of 1 hPa or more over the last three hours counts. A fall often comes before rain and wind, and some people get headaches with it.
- **The air:** dew point (`weather.dewpoint`), visibility (`weather.visibility`) and cloud cover (`weather.clouds`), with humidity and *feels like* as before.
- **Rain:** how much fell in the last 15 minutes (`weather.precipitation`) and is forecast today (`weather.precipitation.today`), and the chance of rain this hour (`weather.rain.hour`) and tomorrow (`weather.tomorrow.rain`), as well as today's (`weather.rain`).
- **The sun:** today's daylight (`weather.daylight`, e.g. *10 h 49 min*) and how high the sun is (`weather.sun.elevation`, in degrees, below 0 after sunset). The sun's height is worked out on your PC from the place and the time, so it keeps moving between forecasts without fetching anything.

For example, `UV {weather.uv} ({weather.uv.level}) · {weather.pressure} {weather.pressure.trend}` in a text widget reads *UV 3 (Moderate) · 1012 hPa Falling*. They all come in the one request for the forecast, so there are no extra requests.

The location is set once for the PC, so every layout shows the same place (and the widget can move between it and [more places](#more-places)). Units follow your Windows region (°C, km/h, mm, km and hPa, or °F, mph, inches, miles and inches of mercury); *Weather* → *Units* changes them.

Forecasts come from [Open-Meteo](https://open-meteo.com), which is free for non-commercial use and needs no account. When you search, what you type goes to Open-Meteo, and to [OpenStreetMap](https://www.openstreetmap.org)'s place search only when Open-Meteo doesn't find it (it knows more suburbs, parks and postcodes). After that only the place's coordinates are used: they go to Open-Meteo every 15 minutes for the forecast (and every hour for [air quality and pollen](#air-quality-and-pollen), if you turn them on), and with [severe weather warnings](#severe-weather-warnings) on, a US place's go to the National Weather Service every 10 minutes. Nothing else is sent, and nothing at all until you search or set a location. *Turn off weather* forgets the place (and any [more places](#more-places)) and stops it. If the internet is down the last forecast stays for up to three hours. Weather data by Open-Meteo.com, under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Place search by OpenStreetMap ([© OpenStreetMap contributors](https://www.openstreetmap.org/copyright)).

#### Rain soon

The *Rain soon* widget shows the rain in the next two hours at your location: a bar for each quarter of an hour, taller the heavier the rain, with the time every half hour under them. A line above says what's coming: *Dry for the next 2 hours*, *Rain from 14:30*, *Rain stopping around 15:15*, or *Light rain for the next 2 hours* (*Moderate* or *Heavy* when it is). It says snow instead when the forecast for that hour is snow. Add it with *Add widget* → *Rain soon* in the editor; like the *Weather* widget, it needs a location.

<p align="center"><img src="images/widgets/rain.png" width="600" alt="The weather now, 14° and partly cloudy, and under it Rain from 22:30 with a bar for each quarter of an hour: none until half past ten, then a shower that's heaviest around eleven and tails off by a quarter to twelve"></p>

- **The bars:** a quarter-hour that's dry has no bar, so two dry hours are just the line along the bottom. A drizzle still gets a small bar, so you can see it; light rain (up to 2.5 mm an hour) reaches up to half way, and heavy rain (10 mm an hour or more) the top. They're in the accent colour unless you choose another, and turning off *Summary* leaves just the bars and the times.
- **Readings for any widget:** `weather.rain.soon` is the line above the bars, and `weather.rain.next` is how many minutes until it rains: 0 while it's raining, and no reading while it stays dry for the next two hours. An [alert](#alerts) on `weather.rain.next` below 30 is a reminder to bring the washing in.
- **Where it's most precise:** Open-Meteo's forecasts a quarter of an hour at a time come from weather models that work in that detail only over central Europe (Germany's ICON-D2 and France's AROME) and North America (the US's HRRR). Elsewhere each quarter-hour is filled in from the hourly forecast, so it can't say just when in the hour a shower starts or stops.
- **Your location's:** like the `weather.*` readings, it's always your location's rain, even while a *Weather* widget beside it shows one of your [more places](#more-places).

Times are the place's own, like the hourly forecast's. It all comes in the same request as the rest of the forecast, so nothing more is sent and there are no extra requests.

#### More places

Save other places, such as where you're off to on holiday or where family live abroad, and the *Weather* widget can move between them and your location.

1. Once your location is set, *Weather* → *More places* → *Add…*, and search for the place as you would for your location.
2. In the editor, select the weather widget and tick the places under *Places*, dragging them into order by their handles. Your location is first in the list.

With more than one place ticked, the widget shows one at a time with its name (even with *Location* off), moving on every 10 seconds; *Seconds per place* changes that, from 3 seconds to 10 minutes. Tap the widget to move on straight away; any other weather widget on the dashboard showing the same places moves on with it, so a forecast beside the weather now keeps to the same place. A tap on a warning along the bottom still opens the warning. The tablet view can't be tapped, but moves on by itself. With nothing ticked the widget shows just your location, as it always has, and with one place ticked it shows just that one, with its name unless it's your location. Places with the same name are told apart in the editor by the next part of their name, such as *Paris (Texas)*, and a place set by typing its coordinates goes by both of them, such as *50.21 -5.48*.

Your location stays the main place: the `weather.*` readings, [air quality and pollen](#air-quality-and-pollen) and [severe weather warnings](#severe-weather-warnings) are all for it, so a warning only shows on the widget while your location is on show. On the *Weather* page, each place shows its weather now; drag places into order by their handles (or Alt+Up and Alt+Down), *Remove* one, or *Make main* to swap one with your location, say when you get there. Changing your location to one of the more places instead takes it off the list, so it isn't there twice.

Each place's coordinates go to Open-Meteo every 15 minutes for its forecast, just like your location's, half a minute apart rather than all at once. Nothing else is sent for them, and without more places nothing more is fetched. If one can't be reached it tries again two minutes later, and its last forecast stays for up to three hours.

#### Air quality and pollen

Turn on *Weather* → *Air quality and pollen* for readings of the air at the weather's location. It's off until you turn it on, and needs a location.

- **Air quality index:** `weather.aqi`, on the index chosen under *Air quality index*, and its level in that index's own words (`weather.aqi.level`). The European index (`weather.aqi.european`) is *Good* up to 20, *Fair* to 40, *Moderate* to 60, *Poor* to 80, *Very poor* to 100 and *Extremely poor* above that. The US index (`weather.aqi.us`) is *Good* up to 50, *Moderate* to 100, *Unhealthy for sensitive groups* to 150, *Unhealthy* to 200, *Very unhealthy* to 300 and *Hazardous* above that.
- **What's in the air:** fine particles (`weather.pm25`, PM2.5), particles (`weather.pm10`, PM10), ozone (`weather.ozone`) and nitrogen dioxide (`weather.no2`), in µg/m³.
- **Pollen:** grass (`weather.pollen.grass`), trees (`weather.pollen.tree`: alder, birch and olive) and weeds (`weather.pollen.weed`: mugwort and ragweed), in grains/m³, each with its level (`weather.pollen.grass.level` and so on): *None*, *Low*, *Moderate*, *High* or *Very high*. `weather.pollen` sums them up as the group at the highest level, e.g. *Grass high* (or *Grass and tree high* when they're level), or *None*. Pollen is only counted in Europe, and only in season; elsewhere these readings show a dash.

*Automatic* uses the European index for places in CAMS's European forecast area (latitude 30° to 72° N and longitude 25° W to 45° E: Europe, Iceland, Turkey, North Africa's coast and the eastern Mediterranean) and the US index everywhere else. The Canary Islands and the Azores are just outside it, so choose *European* there if you'd rather.

Each type of pollen has its own bands, since some bring on hay fever at far smaller counts than others: [MeteoSwiss's](https://www.meteoswiss.admin.ch) for alder, birch, grass, mugwort and ragweed, and the Spanish Aerobiology Network's for olive, which stop at *High*. A group takes the level of its worst type: 20 grains of birch is moderate, but 20 of olive is low. The bands are meant for a day's average, so an hour's count is a guide rather than the official level.

For example, `AQI {weather.aqi} ({weather.aqi.level}) · Pollen: {weather.pollen}` in a text widget reads *AQI 26 (Fair) · Pollen: Grass high*. While it's on, the *Weather* page's location line shows the air quality and pollen too, and the *Weather* widget can show them beside the temperature (tick them under *Details to show*).

Once an hour, just after the hour (when that hour's readings come out), the place's coordinates go to Open-Meteo's air quality service; nothing else is sent, and nothing at all while it's off or there's no location. If it can't be reached it tries again five minutes later, and the last readings stay for up to three hours. Air quality data by CAMS (Copernicus), via Open-Meteo.com: the forecasts of the [Copernicus Atmosphere Monitoring Service](https://atmosphere.copernicus.eu).

#### Severe weather warnings

Turn on *Weather* → *Severe weather warnings* for warnings of storms, floods, snow, ice, heat and the like at the weather's location: from the [Met Office](https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings) for places in the UK, and the [National Weather Service](https://www.weather.gov) (NWS) for places in the US. It's off until you turn it on, and needs a location in one of them; anywhere else, the *Weather* page says warnings aren't available there yet.

- **On the weather widget:** the most serious warning along the bottom with a warning sign, and *and 2 more* when there are others. Yellow and amber warnings (and the NWS's minor and moderate ones) are in the theme's warning colour, red ones (and the NWS's severe and extreme ones) in its critical colour. Tap it to read the warning on the Met Office's or the NWS's site (not in the tablet view). Its size follows *Forecast size*, or *Temperature size* on a widget without a forecast, and in a widget too short for both, the weather gives way to it. Nothing shows while there are none, so the widget looks as it always has. It only shows on a widget with *Weather now*, so a forecast-only widget beside it doesn't repeat it; turn off *Warnings* in the editor to leave it out altogether.
- **Notifications:** with *Notify me* on (it is, once warnings are), a Windows notification when a warning comes out or gets more serious, with its headline; click it to read the warning. Each comes up once while Impious Bonum runs, and several at once share one notification that lists the most serious few.
- **Readings for any widget:** `weather.warnings` (how many there are now, 0 when there are none), `weather.warning` (the most serious one's headline, e.g. *Yellow warning of rain affecting Wales*) and `weather.warning.level` (its level in the agency's own words: *Yellow*, *Amber* or *Red* from the Met Office, *Minor*, *Moderate*, *Severe* or *Extreme* from the NWS).

The Met Office gives warnings for 16 areas of the UK, such as *North West England* or *Strathclyde*, and *Automatic* picks the one your location is in. Near the edge of one, the next may suit you better: *Met Office area* chooses it. Places picked from a search know which country they're in. A location typed in as coordinates gets the Met Office's warnings if it's in Great Britain, but to get the NWS's, set it by searching for its name: coordinates alone are never sent to the NWS, in case they're in Canada or Mexico.

Every 10 minutes while they're on, for a UK place only its Met Office area is sent to the Met Office (`metoffice.gov.uk`), never the place itself; for a US place its coordinates go to the National Weather Service (`api.weather.gov`). A place anywhere else isn't sent anywhere, and nothing at all is fetched while warnings are off. If they can't be reached it tries again three minutes later, and the last warnings stay for up to an hour (each still goes when it ends). Warnings from the Met Office (© Crown copyright) and from the US National Weather Service.

### Calendar

The `agenda` widget lists what's coming up, by day: each event's time (or *All day*) and title, and where it is.
What's on now is in the accent colour, and something that started before today shows *Until* when it ends.

<p align="center"><img src="images/widgets/agenda.png" width="600" alt="An agenda of today's and tomorrow's events with their times and places, and beside it text widgets showing the next event, Call with Sam at 22:15"></p>

Add calendars on the *Calendars* page (or select the widget in the editor → *Calendars…*) by pasting their private iCal link:

- **Google Calendar:** Settings → your calendar → *Secret address in iCal format*.
- **Outlook / Microsoft 365:** Settings → Calendar → Shared calendars → *Publish a calendar*, then the ICS link.
- **Apple iCloud:** share the calendar as a public calendar and copy its `webcal://` link.

Each is checked every 15 minutes. Repeating events, moved and cancelled occurrences, all-day events and time zones
are all handled; events show in this PC's time. If a calendar can't be fetched, its last events stay for a day and
`calendar.status` says what's wrong. An agenda can show some calendars and not others (*Calendars*: `Work, Family`),
so a calendar's name can't have a comma in it or be `all`.
The metrics put the next event anywhere: `{calendar.next.title}` at `{calendar.next.time}`, or a `stat` of
`calendar.next.minutes`. Those look at all your calendars; each calendar has its own as well, listed in the metric
picker under *Calendar: Work* and so on, so `{calendar.work.next.title}` is the next event in *Work* only. The name in
the id is the calendar's, in lower case with a hyphen for spaces and punctuation (*Family Birthdays* is
`calendar.family-birthdays.…`), so renaming a calendar changes its metrics. A private link lets anyone who has it read the calendar, so it's kept only in this PC's
`settings.json`, never in layouts you export.

### News

The `news` widget shows headlines from news feeds (RSS or Atom), newest first: a list of the latest few, a
*ticker* that shows one at a time and moves on every few seconds, or *tickers*: a ticker for each feed, a line each,
taking turns to move on so they don't all change at once (*Headlines* is how many each goes through). Each headline
shows where it's from and how old it is, and tapping one opens the article in your browser. Add feeds on the *News feeds* page (or select the widget in the editor → *News feeds…*): most news sites and blogs have
one (look for an RSS link, or try the site's address with `/feed` or `/rss` on the end), and so do YouTube channels
and subreddits. Each feed is checked every 15 minutes. In the list and the ticker the same story in two feeds shows
once; with tickers, it's in each feed's.

A widget can show some feeds and not others. In the editor, *Feeds* lists your feeds: untick *All feeds*, which
leaves each feed ticked, then untick the ones you don't want and drag the rest into order by the handles at their left
(or Alt+Up and Alt+Down). That's the order the tickers go in; with *All feeds*, they follow the *News feeds* page's
order. The last feed ticked stays until you tick another (or *All feeds*), so swapping one for another doesn't go back
to all of them. A feed you've since removed or renamed stays ticked, marked *not found*, until you untick it. In a
layout file, `feeds` is `all` or the names separated by commas (`BBC, The Verge`), so a feed's name can't have a comma
in it or be `all`; one added before that was checked is greyed out in *Feeds*, and only shows with *All feeds*, until
you remove it and add it again with another name. `{news.title}` puts the newest headline anywhere.

<p align="center"><img src="images/widgets/news.png" width="600" alt="Four headlines, newest first, each with where it's from and how long ago"></p>

### Custom data

Anything that can answer a web request or print to a console can feed the dashboard: Home Assistant sensors, a server's health page, a script that checks your backups. The *Custom data* page lists your sources; *Add…* sets one up:

- **Where from:** a web address (`GET`, with optional headers such as an access token) or a command with arguments, run without a window. Either can return JSON or plain text.
- **How often:** every 5 seconds or more. Each source runs on its own, and gives up after 15 seconds.
- **Fields:** one per line, as `name = path : unit`. The path finds a value in JSON (`state`, `attributes.humidity`, `items[0].value`, `$["odd key"]`); with no fields, the whole output is the value. Units are `auto` (a number if it looks like one, else text), `text`, `number`, `percent`, `celsius`, `fahrenheit`, `bytes`, `bytespersecond`, `milliseconds`, `watts`, `volts`, `megahertz`, `rpm` and `fps`.
- **Test** reads it once and shows the metrics it gives.

Each field becomes the metric `custom.<source>.<field>`, for any widget: text, a gauge, a graph. `custom.<source>.status` says `OK` or why the last read failed, e.g. `HTTP 401 Unauthorized`. True and false read as 1 and 0.

Some examples:

| Source | Set up as |
|---|---|
| A Home Assistant sensor | Web address `http://homeassistant.local:8123/api/states/sensor.outside_temperature`, header `Authorization: Bearer <long-lived token>`, fields `temp = state : celsius` and `humidity = attributes.humidity : percent` |
| A PowerShell script | Command `powershell`, arguments `-NoProfile -File C:\Scripts\backup-age.ps1` printing a number of hours, no fields: the output is `custom.backup.value` |
| A JSON API | Web address `https://api.github.com/repos/<owner>/<repo>`, fields `stars = stargazers_count` and `issues = open_issues_count` |

Sources are stored in `settings.json` (`customData`) on this PC and never in a layout, so a layout from someone else can't add a command to run. Headers, tokens included, are stored there as plain text.

### Status checks

The `status` widget shows whether your own servers and services are up: a NAS, Pi-hole, Plex, the router, a website.
Each has a dot in the accent colour while it's up and the critical colour while it's down, its name, and how fast it
answered, as a list or a grid.

<p align="center"><img src="images/widgets/status.png" width="600" alt="Four status checks: the router, the NAS and Pi-hole up with orange dots, answering in 2, 4 and 3 ms and up for 12 days, 6 days and 3 hours, and Plex down in red for 12 minutes; beside them, 1 down: Plex, and 3 of 4 up"></p>

Add them on the *Status checks* page (or select the widget in the editor → *Add a check…*). Each has:

- **Name:** what the dashboard calls it, such as *NAS*.
- **Address:** a host name or IP address (`nas.local`, `192.168.1.10`), which is pinged, or a web address
  (`http://pi.hole/admin`, `https://nas.local:5001`), whose page is asked for. A page counts as up when it loads (an
  answer in the 200s or 300s); *401 Unauthorized* and the like count as down, so for a service behind a sign-in, use a
  page that doesn't need one. To check a port, give its web address (`http://nas:8080`).
- **Page must say:** for a web address, a word the page has to contain, in any case, to count as up, so a page that
  loads but shows an error doesn't. A move to another page of the same server is followed (from `/` to
  `/web/index.html`, say); a move to another site isn't, and then there's no page to find the word on.
- **Accept its own certificate:** for an https address whose certificate Windows doesn't trust, as home servers often
  have their own.
- **Every:** how often to check it: every 60 seconds unless you say otherwise, and 10 at the least.

*Check now* checks it once and says what it found. A ping waits 2 seconds for its reply and a page 10. One that fails
is checked again 10 seconds later, and only counts as down if that fails too, so one lost answer doesn't.

Each check has readings any widget can show, listed in the metric picker under *Status: NAS* and so on. The name in
the id is the check's, in lower case with a hyphen for spaces and punctuation (*Pi-hole* is `status.pi-hole.…`), so
renaming a check changes its readings.

| Reading | |
|---|---|
| `status.<name>.up` | 1 while it's up, 0 while it's down |
| `status.<name>.ms` | How long it took to answer, while it's up |
| `status.<name>.since` | How long it's been up or down, e.g. *3 h* or *12 min*, counted from when Impious Bonum first checked it |
| `status.up`, `status.down` | How many are up, and how many down |
| `status.summary` | *All up*, or which are down: *1 down: Plex* |

A widget can show some checks and not others: *Checks* works like the [news widget's feeds](#news), so a check's
name can't have a comma in it or be `all`. *Columns* makes a grid, and *Show response time* and *Show how long* say
what's beside each name. A check that's down says *Down* in words as well as colour.

**To be told when one goes down,** add an [alert](#alerts) on `status.<name>.up` below 1. *Alert…* beside the check
sets one up, with a notification saying *NAS is down*; it needn't wait any seconds, as a check is only down once it
has failed twice. With *Flash a banner on the dashboard*, the banner stays until the check is back. To be told when
it's back as well, add a second alert on `status.<name>.up` above 0; that one also goes off soon after Impious Bonum
starts, when it first finds the check up.

Nothing is checked until you add a check, and only the addresses you add are contacted: a ping, or a request for the
page, as often as you say. Checks are kept in `settings.json` (`statusChecks`) on this PC and never in a layout, so a
layout from someone else can't make the dashboard contact anything.

### Claude usage

The *Claude usage* widget shows your Claude plan's session (5-hour) and weekly limits as two bars, e.g. *Session 24 % · resets in 2h 14m*. It's only on screen while Claude Code is open and reporting, and shows faded while you edit the layout so you can still place it.

<p align="center"><img src="images/widgets/claude.png" width="600" alt="Two bars: the session at 38 %, resetting in 2 h 10 min, and the week at 61 %, resetting on Thursday"></p>

The numbers come from Claude Code: it passes them to its [status line](https://code.claude.com/docs/en/statusline), and only for Pro and Max plans. There's no official way for another app to read them, so Impious Bonum uses the status line. That means Claude Code in a terminal (the CLI): the Claude Desktop app doesn't run status lines or share usage with other apps. Usage in the Desktop app and on claude.ai still counts, because the limits are shared, and shows the next time the CLI reports.

1. *Claude usage* → *Connect…* (or select the widget in the editor → *Connect Claude Code…*; adding the widget offers this too, if it isn't set up yet). This sets the status line in Claude Code's `settings.json` (`%USERPROFILE%\.claude\settings.json`) to `ImpiousBonum.Sensors.exe claude-statusline`, re-run every 15 seconds. It keeps your other settings and saves a copy of the file first. If you already have a status line, it asks before replacing it.
2. Start Claude Code in a terminal (`claude`), or restart it if it's open. The new status line shows the model, context use and your limits, e.g. *Opus 5.5 · context 8% · session 24% (2h 14m) · week 41%*.
3. Add the widget: *Add widget* → *Claude usage* in the editor. The usage appears after Claude Code's next reply.

How it decides Claude Code is open: each status line run updates `claude-usage.json`, and Claude Code re-runs it every 15 seconds while it's open. If there's been no run for a minute, the widget hides. Usage you spend on claude.ai shows up the next time Claude Code reports. A window that has reset reads *0 % · not started* until you use Claude again.

**Keeping your own status line:** have your script also pass its input to Impious Bonum, e.g. in a Bash script that has read stdin into `$input`:

```bash
echo "$input" | ~/AppData/Local/ImpiousBonum/current/sensors/ImpiousBonum.Sensors.exe claude-statusline > /dev/null
```

and set `"refreshInterval": 15` on your `statusLine` so the widget knows Claude Code is still open while it's idle.

The `bars` widget is the same widget for any metrics, e.g. CPU and RAM load. Its *Hide without a reading* setting works the same way.

### Microphone and camera

Whether your microphone is muted, and which apps are using it or your camera: for a light that goes red while you're live, or a reminder that you're muted.

| Metric | |
|---|---|
| `mic.muted` | 1 while the microphone is muted, 0 while it isn't |
| `mic.name` | The microphone's name, as Windows' sound settings show it |
| `mic.inuse`, `camera.inuse` | 1 while any app is using the microphone (or the camera), 0 while none is |
| `mic.apps`, `camera.apps` | The apps using it, e.g. *Discord, OBS*. No reading while none is |

- **Which microphone:** the one Windows uses for calls. That's your default microphone, unless you've picked a different *Default Communication Device* in the Sound control panel's *Recording* tab.
- **Muted** means Windows' own mute, the one in the sound settings, which a [`mic-mute` button](#now-playing-and-buttons) turns on and off. An app's own mute (Discord's, say) or a mute switch on a headset doesn't change it, so it doesn't show here.
- **The apps** using them are read from the record Windows keeps for its privacy settings (*Settings* → *Privacy & security* → *Microphone* → *Recent activity*) and its microphone and camera icons. Reading it needs no permission, and Impious Bonum never uses the microphone or camera itself, so Windows doesn't ask or show its icons for it. Desktop apps go by their program's name (*Discord*, *Chrome*, *OBS*), Store apps by the end of their package name (*WindowsCamera*).
- The microphone's metrics appear on PCs with a microphone, or where an app has used one; the camera's once an app has used a camera.

<p align="center"><img src="images/widgets/microphone.png" width="600" alt="MIC lit up red while Discord is using the microphone, CAMERA dim because the camera's off, the microphone's name, In use by Discord, and a Mute button with a microphone icon"></p>

To make a widget go red, give it a warning colour on one of these metrics (see [editing layouts by hand](#editing-layouts-by-hand)). The picture's widgets: *MIC* lights up while an app is using the microphone and *CAMERA* while one is using the camera (`track` is the theme's dim bar colour, so they're barely there until then), the microphone's name turns red while it's muted, the apps using it show underneath only while there are some, and the button mutes and unmutes it, its icon crossed out while muted:

```json
{ "type": "text", "x": 30, "y": 20, "width": 200, "height": 70, "fontSize": 48, "text": "● MIC", "color": "track",
  "thresholds": [{ "metric": "mic.inuse", "above": 0, "color": "critical" }] },
{ "type": "text", "x": 250, "y": 20, "width": 300, "height": 70, "fontSize": 48, "text": "● CAMERA", "color": "track",
  "thresholds": [{ "metric": "camera.inuse", "above": 0, "color": "critical" }] },
{ "type": "icon", "x": 30, "y": 115, "width": 520, "height": 56, "icon": "mic", "text": "{mic.name}", "fontSize": 34,
  "iconSize": 48, "gap": 20, "thresholds": [{ "metric": "mic.muted", "above": 0, "color": "critical" }] },
{ "type": "text", "x": 98, "y": 176, "width": 452, "height": 40, "fontSize": 26, "text": "In use by {mic.apps}",
  "color": "secondary", "hideUnavailable": true },
{ "type": "buttons", "x": 590, "y": 20, "width": 180, "height": 200, "iconSize": 64,
  "buttons": [{ "label": "Mute", "icon": "mic", "action": "mic-mute" }] }
```

In the editor, the same rules are each widget's *Warning colours*: pick the metric, set *Above* to 0, and choose a colour.

## Widgets in depth

### Top apps

The `processes` widget is a small Task Manager: the apps using the most CPU or memory, one row each with both numbers.
An app's processes count together, so a browser with thirty tabs is one row (with the count in brackets). Sort it by CPU
or memory, choose how many rows and whether to show both numbers, and give the CPU figure warning colours the same way
as other widgets. For a single figure elsewhere, use the metrics: `{top.cpu.1.name}` at `{top.cpu.1}` in a `text` widget
shows the busiest app. It reads every process in one call every 2 seconds, without opening any of them, so it costs
about as much as Task Manager's Processes tab does.

<p align="center"><img src="images/widgets/top-apps.png" width="600" alt="Six apps by CPU use, each with its process count, CPU share and memory: a game at 21.4 % and 7 GB, then Firefox, Discord, Music, Impious Bonum and Windows Explorer"></p>

### Multi-line graphs

The `lines` widget draws several metrics' histories on one scale, so they can be compared at a glance: CPU against
GPU load, download against upload, CPU against GPU temperature. Each line has its own label, metric and colour (a
theme colour such as `accent` or `warning`, or `#RRGGBB`), and the legend above shows each one's live value. Lines
whose metrics share a natural maximum (percentages, say) are drawn out of it; otherwise the scale fits the highest
line, or set *Maximum* yourself.

<p align="center"><img src="images/widgets/graphs.png" width="600" alt="A line graph of CPU load and an area graph of memory, each with a label and value, beside a multi-line graph of CPU and GPU load in orange and blue with a legend"></p>

### World clocks

The `clock` widget can show the time in other places under the date. In the editor, add entries to *Other places*,
each with a label and a time zone: a name such as `Europe/London`, `America/Los_Angeles` or `Asia/Tokyo`, or a Windows
name such as `GMT Standard Time`. They sit side by side (label above time) or stacked (label left, time right), in
their own size and format, with *+1* or *−1* when it's already tomorrow or still yesterday there. A zone that can't be
found says so in red. The main time can be another place's too: set the clock's *Time zone*. Daylight saving is
handled by Windows' time zone data.

<p align="center"><img src="images/widgets/clocks.png" width="600" alt="A clock at 21:47 with the date, and under it the time in San Francisco, New York, Tokyo and Sydney, the last two marked +1 for tomorrow"></p>

### Timers

The `timer` widget is a stopwatch, a countdown or a count to a moment:

- **countdown** (the default) counts down from a length such as `25:00` or `1:30:00`. Tap the time to start and pause it.
  At zero it shows *Time's up* (or your own text) in the warning colour, blinking, and sends a Windows notification.
  Tap it again to go back to the start.
- **stopwatch** counts up; tap to start and pause.
- **until** counts down to a time of day (`18:00`, which comes round again the next day) or a date (`2026-12-25`,
  `2026-12-25 09:00`), on its own, with days shown once it's more than a day away. A month and day with no year
  (`12-25`, or `10-31 18:00`) comes round every year: it shows *Time's up* (or your own text, such as *Merry
  Christmas!*) for the rest of that day, then counts down to next year's. Write it month first, like the dates.

<p align="center"><img src="images/widgets/timers.png" width="600" alt="Three timers: a 25:00 focus countdown and a stopwatch, both paused with Reset under them, and 1:43:00 until bedtime"></p>

The *Reset* button underneath puts a countdown or stopwatch back to zero. A running timer keeps going when you swipe to
another page, switch layouts or edit the layout: timers with the same label, mode and length share one clock, so a
countdown on two pages is the same countdown. Timers start again from scratch when the app restarts. The notification
only comes while the timer is on screen; if it's on a page you're not showing, it comes when you next show it.

### Countdowns

The `countdowns` widget lists how long it is until the days you're looking forward to, soonest first: birthdays,
holidays, a game's release, a trip. In the editor, add entries to *Events*, each with a label and a date, written
month first like the timer's:

- A month and day with no year, such as `12-25` or `03-14 09:00`, comes round every year. On the day it says *Today*,
  and the day after it starts counting to next year's.
- A date, such as `2026-12-25` or `2026-12-25 18:00`, happens once. It says *Today* on the day, then leaves the list.
- A time of day, such as `23:30`, counts to the next one, so it's always today or tomorrow.

Each row shows *69 days*, *Tomorrow* or *Today*; set *How long* to `time` for the days, hours and minutes instead, such
as *69d 02:13*. Events that are today are in the accent colour (or your own *Today's colour*). The order you add them
in doesn't matter: the list always shows the soonest first. *Rows* is the most it shows, and with *Shrink to fit* on,
rows that don't fit are left out rather than cut in half. A *Heading* can go above the list, and *When there's
nothing* is shown once everything has gone. A date it can't read says *Unknown date* in red, at the bottom.

<p align="center"><img src="images/widgets/countdowns.png" width="600" alt="Two lists of countdowns, soonest first: the days until Halloween, Bonfire Night, a birthday, Christmas and New Year, and the days, hours and minutes until bedtime (in orange, as it's today), the fireworks, Christmas and a flight"></p>

### Now playing and buttons

The *Now playing* widget (`media`) shows what's playing in any app that appears in Windows' media controls (the ones in the volume flyout): Spotify, YouTube in a browser, Media Player and so on. It has the album art, title and artist (both templates, so `{media.artist} · {media.album}` works too), a progress bar with the position and length, and previous / play-pause / next buttons. It hides while nothing is playing, and shows faded while you edit the layout. The `media.*` metrics work in any widget too, e.g. a gauge of `media.progress`.

<p align="center"><img src="images/widgets/media.png" width="600" alt="Now playing: album art, the title and artist, a progress bar at 1:37 of 4:04 and previous, pause and next buttons, above four buttons: Mute mic, Screenshot, Gaming and Sound"></p>

The *Buttons* widget (`buttons`) is a row or grid of buttons to tap, each with a label, an icon or both. Each button does one of:

| Action | |
|---|---|
| `play-pause`, `next`, `previous`, `stop` | Like the keyboard's media keys: they act on what *Now playing* shows |
| `volume-up`, `volume-down`, `mute` | Like the keyboard's volume keys |
| `mic-mute` | Mutes the microphone Windows uses for calls, or unmutes it (see [Microphone and camera](#microphone-and-camera)). With the `mic` icon, the button shows `mic-off` while it's muted, whatever muted it |
| `open` | Opens the *target*: a program (`calc.exe`), a file or folder (`C:\Games`), a web address, or a Windows link such as `ms-settings:sound`. *Arguments* are passed to a program |
| `keys` | Sends a shortcut such as `Ctrl+Shift+M`, `Win+D` or `F13` to the app you're using. Names are like `A`, `5`, `F1`–`F24`, `Enter`, `Space`, `Esc`, `Tab`, `Home`, `PgUp`, `Up`, `Delete`, `VolumeUp`, `MediaPlayPause` |
| `layout` | Switches to the layout named in *target* |
| `web` | Sends a web request to the *target* address, for Home Assistant, smart lights, Node-RED or ntfy (see [Web request buttons](#web-request-buttons)) |

Taps never take focus from the app you're using, which is what makes `keys` useful: a button for a game's or a call app's shortcut works without leaving it. Shortcuts can't reach apps running as administrator (Windows doesn't allow it). Buttons don't do anything while you edit the layout, or in the tablet view unless you [let tablets press them](#tablet-view). If a button can't do its job (a program that isn't there, a misspelt key), a notification says why.

#### Web request buttons

A `web` button sends a web request when you tap it: to run a Home Assistant script or scene, switch a smart light or plug that has a web address of its own, start a Node-RED flow, or send a notification to your phone with [ntfy](https://ntfy.sh). As well as its *target*, the address (`http://` or `https://`), each one has:

- **Method:** `POST` (the default), `GET` or `PUT`.
- **Body:** what a `POST` or `PUT` sends, such as JSON or a message. *Content type* says what it is; left empty, it's `application/json` for a body in `{ }` or `[ ]` and `text/plain` for anything else.
- **Header:** one header as `Name: value`, usually `Authorization: Bearer` and a token.

The request goes in the background, so the dashboard carries on, and gives up after 10 seconds. The button stays pressed until the answer comes, then lights up for a moment: in the theme's accent colour if it worked, or its critical colour if it didn't, when `dashboard.log` says why (*homeassistant.local answered 401 Unauthorized*, say) but never what's in the header. Taps while it waits don't count, so a slow server doesn't get a queue of requests. With *Let tablets press buttons* on (see [Tablet view](#tablet-view)), a tablet can press it too, and sees it light up.

| To | Set up as |
|---|---|
| Run a Home Assistant automation | Give the automation a [webhook trigger](https://www.home-assistant.io/docs/automation/trigger/#webhook-trigger), then target `http://homeassistant.local:8123/api/webhook/<webhook id>` with `POST`. It needs no token: the webhook id is the secret, so make it hard to guess |
| Run a Home Assistant script or scene | Target `http://homeassistant.local:8123/api/services/script/turn_on`, body `{"entity_id": "script.movie_night"}`, header `Authorization: Bearer <long-lived token>`. Make the token on your Home Assistant profile's *Security* tab, under *Long-lived access tokens*. For a scene it's `scene/turn_on` and `scene.evening`; to switch a light, `light/toggle` and `light.desk_lamp` |
| A notification on your phone | Target `https://ntfy.sh/<your topic>`, body `Dinner's ready`, and subscribe to the topic in the ntfy app, which shows the body as the notification. A header such as `Title: From the dashboard` gives it a title |

In the layout file, those three look like this:

```json
{ "type": "buttons", "x": 30, "y": 30, "width": 600, "height": 140,
  "buttons": [
    { "label": "Lights off", "icon": "power", "action": "web",
      "target": "http://homeassistant.local:8123/api/webhook/lights-off-7f3a9c" },
    { "label": "Movie night", "icon": "moon", "action": "web",
      "target": "http://homeassistant.local:8123/api/services/script/turn_on",
      "body": "{\"entity_id\": \"script.movie_night\"}", "header": "Authorization: Bearer <long-lived token>" },
    { "label": "Dinner", "icon": "chat", "action": "web", "target": "https://ntfy.sh/<your topic>",
      "body": "Dinner's ready", "header": "Title: From the dashboard" }
  ] }
```

The header is kept in the layout as plain text. *Export…* leaves headers out, since they're usually a password or token, and says so: whoever imports the layout adds their own. The address and body go with it, so leave secrets out of those in a layout you share. A layout from someone else could send requests to things on your network, so importing one with web buttons lists where they send and asks first (see [Sharing layouts](#sharing-layouts)).

### Audio visualiser

The `visualizer` widget's bars move with whatever the PC is playing (music, a game, a video), from low notes on the
left to high on the right, with peak markers that drift down. *Mirror* grows the bars up and down from the middle.
It listens to the default output device the way Windows' own volume meters do (WASAPI loopback): nothing to install
and nothing changes in what you hear. When nothing is playing, the bars settle to zero.

It's the one widget that draws many times a second (25 by default; *Frames a second* goes from 5 to 60), so it costs a
little CPU, but only while it's on screen: sound is captured only while a visualiser is showing, and stops when it's
on another page, the dashboard is hidden, or the layout doesn't have one. It doesn't move in the tablet view, which
updates once a second.

### Images and shapes

The *Image* widget shows a picture from a PNG, JPG, GIF (first frame), BMP or TIFF file: a logo, album artwork, a photo of your build. *Fit* works like the background image's (fill, fit, stretch, center, tile), and it can have rounded corners and be see-through. *Recolour* draws the picture's shape in a theme colour, so a white logo on a transparent background turns the theme's accent and follows theme changes. Pictures are decoded at about the size they're shown, so a large file costs little memory.

The *Shape* widget is a rounded panel, a circle or a divider line, for grouping widgets or separating them. Shapes are added behind the other widgets, so a panel sits under whatever you put on it; drag it lower in the editor's *Widgets* list (or press Ctrl+]) to draw it on top instead. Their default colour is the theme's `track`, the same subtle shade as an empty bar.

`track` and `background` work as colour names everywhere, like `foreground`, `secondary`, `accent`, `warning` and `critical`.

Exported layouts carry their image widgets' pictures, like the background image and font.

### Photo frame

The *Photo frame* widget (`photos`) is a slideshow of the pictures in a folder, like a digital photo frame on the
dashboard. In the editor, choose the folder with *Browse…* beside *Folder*. It shows the PNG, JPG, GIF, BMP and TIFF
files there, and in the folders inside it unless you turn off *Include subfolders*. Each photo shows for *Seconds per
photo* (30 to start with) and then fades into the next; tap the frame to move on straight away. *Order* is *shuffle*
(every photo once, in a random order, before any comes round again), *name* (A to Z, with IMG2 before IMG10, as
Explorer sorts them) or *date* (oldest first, by when the files last changed).

<p align="center"><img src="images/widgets/photos.png" width="600" alt="A photo of a lake at dusk below hills, with rounded corners and 14 August 2026 along the bottom, beside the time and the date"></p>

*Fit* is *fill*, which covers the frame and crops the edges, or *fit*, which shows the whole photo, with the rounded
corners and caption on the photo itself. *Caption* puts the photo's file name, or the date it was taken, along the
bottom over a shade of the theme's background, so it can be read over any photo. The date is the one the camera
recorded, or else the file's, in the *Date format* you choose. Photos taken with the camera on its side show the right
way up.

It stays light however big your photos are: only the photo on show and the next one are read, each at about the size
it's shown. Files that turn out not to be pictures, or can't be read, are passed over. The folder is looked at again
every five minutes, so photos you add turn up and ones you delete stop showing. A folder with more than 10,000 pictures
in it shows the first 10,000 found. While the dashboard is hidden (over a fullscreen game, say, or on a page that isn't
showing) the slideshow waits and reads nothing. The tablet view moves on by itself, without the fade.

The folder is a place on your PC, so it stays there: exported layouts and layouts in the gallery leave it out, and
whoever adds the layout chooses a folder of their own. Until it has a folder, the frame is empty on the dashboard and
says *Choose a folder* in the editor.

## Automatic behaviour

### Alerts

Warning colours only help if you're looking. An alert tells you when a reading stays out of bounds: the GPU hot spot above 95 °C for 10 seconds, a drive over 95% full, a frame rate below 30, a [custom data](#custom-data) source reporting an error, one of your [servers](#status-checks) going down.

*Alerts* → *Add…* picks the metric (the window shows its reading now), *Above* and/or *Below* a number, for how many seconds (so a brief spike doesn't count), an optional message (a template, so `GPU is cooking: {gpu.hotspot}` shows the reading), and what happens:

- **A Windows notification**, with the reading in it.
- **A banner flashing on the dashboard**, with a pulsing red outline, until the reading is back to normal. It shows over dimming too. Tap it to dismiss it.
- **A sound:** one of Windows' own (*Exclamation*, *Asterisk*, *Hand*, *Question*, *Beep*) or a `.wav` file. *Play* tries it.
- **Switch to a layout**, say one that shows your temperatures large.
- **Run a program**, hidden, such as a `.bat` file or `powershell.exe` with `-File C:\Scripts\alert.ps1`. It gets the alert in
  environment variables rather than on its command line: `IB_ALERT_MESSAGE` (the alert's text), `IB_ALERT_METRIC`,
  `IB_ALERT_VALUE` (the reading, with a dot for decimals) and `IB_ALERT` (e.g. *gpu.hotspot above 95 for 10 s*). Nothing
  waits for it to finish.
- **Send it to a web address**, as a POST. Normally that's JSON, which suits a [Home Assistant webhook](https://www.home-assistant.io/docs/automation/trigger/#webhook-trigger)
  (`http://homeassistant.local:8123/api/webhook/<id>`):
  `{"alert": "gpu.hotspot above 95 for 10 s", "message": "GPU is cooking: 97.2 °C", "metric": "gpu.hotspot", "value": 97.2, "time": "2026-10-14T21:30:00Z"}`.
  *Just the message, not JSON* sends only the text, which is what [ntfy](https://ntfy.sh) shows as a notification on
  your phone (`https://ntfy.sh/<your topic>`).

Everything but the banner happens once, when the alert goes off. If a sound, layout, program or web address doesn't
work, a notification says why and it's written to `dashboard.log`. In the demo (`--demo`) the readings are made up, so
alerts there never switch layouts, run programs or send anything.

An alert goes off once, and only goes off again after the reading has been back to normal for 30 seconds, so a value hovering around the limit doesn't send a stream of notifications. A metric with no reading (no game running for `fps`, say) never counts as out of bounds. Alerts are kept in `settings.json` (`alerts`) and written to `dashboard.log` when they go off.

### Hotkeys

The *Hotkeys* page sets key combinations that work from anywhere in Windows, even while a game has focus:

- **Show or hide the dashboard.** Hidden stays hidden until you press it again (or restart the app).
- **Next page** and **Previous page**, through the same layouts as swiping (see [Pages](#pages)).
- **Edit the layout.**
- **Show** a particular layout: there's a row for each saved one.

Click a box and press the keys; Backspace clears it. Each is saved as soon as it's set, unless it clashes with another. A hotkey needs Ctrl, Alt or Win, so it doesn't take a key away from
every other app, except F13 to F24, which macro pads and programmable keyboards can send. If another app already
uses a combination, a notification says so and the rest still work. Hotkeys are saved in `settings.json` as
`"hotkeys": [{ "action": "show-hide", "keys": "Ctrl+Alt+D" }, { "action": "layout", "keys": "Ctrl+Alt+1", "layout": "Gaming" }]`
(the actions are `show-hide`, `next-page`, `previous-page`, `edit` and `layout`), and they follow a layout when you rename it.

### Game layouts

A game can have its own layout: FPS front and centre, its colours, a themed font. When a linked game has been in front for about 3 seconds the dashboard switches to its layout, and when it has been gone for about 3 seconds it goes back to the layout you chose. Alt-tabbing out briefly, or a launcher flashing up, doesn't switch anything.

To link a game: on the *Game layouts* page, under *Link a game*, pick the game (the list has the apps you've had in front lately, so play it for a moment first, or type its name) and a layout, then *Link*. To keep your usual layout and only change its look, pick *Theme only:* and a theme instead. The page lists the links with *Remove*, and *Switch automatically* turns the whole thing off and on (remembered).

"In front" means the same thing as for the FPS reading: the foreground app, or with *Sensors → FPS from → Top app on a monitor*, the top app on that monitor, so a game on one screen keeps its layout while you type on another.

Game layouts are temporary: picking a layout from the tray while a game is running makes that your choice to come back to, and the editor keeps whichever layout it opened on (switching waits until you leave the *Layouts* page). Switches are written to `dashboard.log`. A link to a layout or theme that no longer exists is ignored, with one notification and a line in the log; renaming a layout updates its links.

Links live in `settings.json` (*General* → *Settings folder*) and can be edited by hand while the dashboard isn't running. `process` is the program's name as Task Manager's *Details* tab shows it, with or without `.exe`, matched ignoring case. A link has a `layout`, a `theme` (which restyles whichever layout is showing), or both:

```json
"gameLayouts": [
  { "process": "eldenring", "theme": "Elden Ring" },
  { "process": "Cyberpunk2077.exe", "layout": "FPS" },
  { "process": "doom", "layout": "FPS", "theme": "Terminal" }
],
"gameLayoutsEnabled": true
```

### Session recaps

When you stop playing a game linked under *Game layouts*, a notification sums up how it went: how long you played,
the average, lowest and highest FPS and the average 1% low, CPU and GPU load (average and peak), and the hottest the
CPU and GPU got. Each session is also added to `sessions.csv` in the settings folder, one line per session, ready for
a spreadsheet.

A session starts once the game has been in front for a few seconds and ends when it's been gone for a minute, so
alt-tabbing to a chat app doesn't split it, and readings only count while the game is in front. Sessions under a
minute (a launcher, a quick look) aren't recapped. FPS and temperatures need the [sensor service](#sensor-service-temperatures-fans-power-fps).

The `session.*` metrics show the session so far while you play, and the last one afterwards, so a layout can show
it: `{session.game}` · `{session.minutes} min` · `{session.fps.avg} average`, or a `stat` of `session.fps.avg`.
`session.playing` reads *Playing* or *Last played*. *Game layouts* → *Recap each session* turns it off.
This works whether or not *Switch automatically* is on.

### Schedule

The dashboard can change with the time of day: the dim red *Night Watch* theme from 22:00 to 07:00, say, a calmer layout during working hours, or the screen at 30% overnight. It can also follow the sun, Windows' dark and light mode, or the time of year: *Night Watch* from sunset to sunrise, *Nord* while Windows is dark and *Glacier* while it's light, or a spooky theme from 24 October to 1 November.

*Pages and schedule* → *Add to the schedule* picks when and what to do. When is one of:

- **Between two times**, which may cross midnight.
- **From sunset to sunrise**, or **from sunrise to sunset**. The sun's times are worked out on your PC for the weather's location (see [Weather](#weather)), so nothing is fetched for them, but without a location these rules never apply: the dialog and the page say so.
- **While Windows is in dark mode**, or in light mode.
- **All day**, on the dates below.

*Only from … to …* limits any of them to the same dates every year, written month then day: `10-24` to `11-01`, or `12-20` to `01-06`, which crosses New Year. Both days are included, and the same date twice is a single day. The dialog spells the dates out (*Every year from 24 Oct to 1 Nov*) so a day written first shows up. A night belongs to the evening it starts: 01:00 on 1 November is still the night of 31 October, so a Halloween rule from 18:00 to 02:00 on `10-31` lasts into the small hours, and so does one from sunset to sunrise.

What to do is show a layout, a theme, dim the screen (see [Dimming](#dimming)), or any mix. The page lists the rules, marking the ones that apply now, and *Follow the schedule* turns them all off and on. Rules combine: when several apply, the layout comes from the first of them that names a layout, the theme from the first that names a theme, and the brightness likewise. So put a rule for some dates above an everyday one for its theme to win on those dates.

A game linked under *Game layouts* still wins while it's in front, and the schedule fills in what its link doesn't set: a game linked to a layout gets the night theme at night. Like game layouts, scheduled ones are temporary: picking a layout from the tray makes it your choice, and renaming a layout updates the rules. The schedule is in `settings.json` too:

```json
"schedule": [
  { "fromDate": "10-24", "toDate": "11-01", "theme": "Grimoire" },
  { "when": "night", "fromDate": "12-01", "toDate": "12-31", "theme": "Ice" },
  { "from": "22:00", "to": "07:00", "theme": "Night Watch" },
  { "when": "dark", "theme": "Nord" },
  { "from": "09:00", "to": "17:30", "layout": "Work" },
  { "from": "00:00", "to": "06:00", "brightness": 0 }
],
"scheduleEnabled": true,
"holidayThemes": false
```

`when` is `dark` or `light` for Windows' mode, or `night` (sunset to sunrise) or `day` (sunrise to sunset). `fromDate` and `toDate` go with any rule, or make one on their own for the whole of those days.

#### Holiday themes

*Pages and schedule* → *Holiday themes* (off until you turn it on) shows the built-in holiday theme on and around each holiday, and says which it's showing or which comes next:

| Theme | When |
|---|---|
| *Lunar New Year* | From the eve, for a week |
| *Valentine* | 13 and 14 February |
| *Shamrock* | 16 and 17 March, for St Patrick's Day |
| *Easter* | Good Friday to Easter Monday |
| *Eid* | Eid al-Fitr from the eve for three days, and Eid al-Adha from the eve for four |
| *Diwali* | Its five days, from two before Lakshmi Puja to two after |
| *Halloween* | 24 to 31 October |
| *Day of the Dead* | 1 and 2 November |
| *Fireworks* | Bonfire Night (5 November), and New Year's Eve to New Year's Day |
| *Hanukkah* | From the eve, for the eight days |
| *Gingerbread* | 1 to 17 December |
| *Christmas* | 18 to 30 December |

Easter is worked out, and Hanukkah, Eid and Lunar New Year come from the Hebrew, Um al-Qura (Saudi Arabia's, which most printed calendars follow) and Chinese calendars that come with Impious Bonum, so they keep coming round. Eid starts when the new moon is seen, so where you are it may begin a day later. Diwali's dates are in a table that runs to 2035. When two holidays overlap, the shorter one shows: Christmas Day over Hanukkah, Hanukkah over the run-up to Christmas, and Diwali over Halloween.

A holiday's theme comes after everything else: a rule above that sets a theme (while *Follow the schedule* is on) wins, and so does a game's link, but a rule that only sets a layout or brightness still gets the holiday's theme. It's stored as `holidayThemes` in `settings.json`.

#### Dimming

A bright strip beside your bed or in a dark room is the most common complaint about these screens. Two things dim it, and the darker of the two wins:

- **The schedule:** a rule's brightness, from 75% down to black (above).
- **Being away:** *Screens* → *When you're away* dims the dashboards after 5 to 60 minutes without keyboard, mouse or touch input, to 40%, 20% or black. Any input brings it straight back. A game or video drawing frames in front counts as someone there, since a gamepad doesn't count as input to Windows. Stored as `awayDimMinutes` and `awayBrightness` in `settings.json`.

Dimming fades in and out. When the screen is too dark to see (20% or less), a tap shows it at full brightness for 30 seconds instead of pressing whatever is under your finger. Nothing dims while you edit the layout, and the tablet view isn't dimmed.

### Logging

The *Logging* page writes readings to CSV files for a spreadsheet, to see how temperatures crept up over an afternoon
or what the CPU was doing overnight. Pick the metrics (*Add…* opens the same picker as the editor), how often to
write a row (every second to every 5 minutes) and how long to keep the files.

Each row is one period, and the readings are taken every second within it, so a row every minute still catches a
short spike: the time the period ended, then each metric's average, lowest and highest. A metric with no reading in a
period has empty cells. Periods line up with the clock (10:00:00, 10:00:10, …). There's one file a day in the `logs`
folder in the settings folder (*Logs folder* → *Open* on the page), e.g. `2026-10-11.csv`. Changing the metrics during
the day starts `2026-10-11 (2).csv`, so each file's header matches all its rows. Files older than the time you chose
are deleted once a day. Numbers use a dot for decimals whatever the Windows region, so any spreadsheet reads them.

## More screens

### More dashboards

With more than one spare screen, each can have a dashboard of its own, with a different layout: on the *Screens* page, pick a screen on the map, then *Add a dashboard*. (The *Gallery* page can add one too, showing a ready-made layout.) The new dashboard gets a new, empty layout of its own, named after the screen, the screen's size and in the main dashboard's theme, and the editor opens on it so you can add its widgets.

- **Editing it:** right-click or press and hold on that dashboard, or pick its screen on the *Screens* page → *Edit*. In the editor, *Screen* moves between dashboards. Changes show live on the dashboard you're editing (and on any other showing the same layout); the rest keep theirs.
- **Its layout:** any saved layout can go on any screen: pick it in the editor's *Layout* list while you're on that screen, or under *Layout* when you pick its screen on the *Screens* page. Two screens showing the same layout show the same changes, so give each its own (*Layouts ▾* → *Duplicate…* makes a copy to change).
- **Which screen the tray means:** tray → *Layout*, *Edit layout…*, pages, game layouts, the schedule, hotkeys and buttons that switch layouts are all the main dashboard's; the others keep their layout. Pick another's screen on the *Screens* page for its *Layout*, *Edit* and *Remove this dashboard*. Dimming, *Always on top*, *Hide over fullscreen apps* and the show/hide hotkey apply to all of them, and buttons and media controls work on each.
- A layout made for another screen's shape still fills it, scaled evenly. For a better fit, give it the screen's size: *Match dashboard screen* under *Canvas* in the editor.
- A dashboard whose screen is unplugged or off hides until it's back, rather than landing on another screen. There's one dashboard to a screen: moving the main dashboard onto a screen that has another one removes the other.

### Tablet view

A tablet or phone on the same Wi-Fi can show the dashboard in its web browser, as a wireless second screen. *Tablet and USB screen* → *Show on tablets* turns it on and shows a link and a QR code (*Show QR code…* shows them again); open it on the tablet. It shows exactly what the dashboard shows (same layout, theme, background and game layouts), updated once a second, and keeps going while the dashboard window is hidden over a fullscreen game.

- **A layout of its own:** each tablet can show a different saved layout instead. Pick it under *Layout for the link*, then open that link (or scan its QR code) on the tablet; the link ends in `&layout=` and the layout's name. That tablet then shows that layout in its own theme, whatever the dashboard is showing: game layouts, the schedule and pages only change the main dashboard. Changes in the editor show on it as you make them, like on a dashboard. Renaming or deleting the layout stops its links working, and the tablet says there's no layout of that name: pick it again for a new link. Up to four layouts can show on tablets at once, besides the main dashboard.
- **Add it to the home screen** (Safari: Share → *Add to Home Screen*; Chrome: ⋮ → *Add to Home screen*) to open it without the browser's bars, or tap *Full screen* where the browser offers it. Set the tablet's screen timeout to never, since the page can't keep the screen on by itself over plain HTTP.
- **Cost:** with no tablet watching it draws nothing. With one watching, it draws the layout off-screen and sends one JPEG a second, and only when something changed. A layout of its own is only made when a tablet first asks for it, costs the same as the main one while it's watched, and is let go a few minutes after the last tablet showing it stops asking.
- **Pressing buttons:** with *Let tablets press buttons* on (under *Tablet view*, off by default), tapping a button on the tablet presses it on the PC: the layout's *Buttons* widgets and the media controls, and nothing else. On a tablet showing a layout of its own, it's that layout's buttons; one that switches layout switches the main dashboard's. A ripple shows where the tap landed, and the result shows on the next frame. Anyone who has the link can then press those buttons, so only turn it on if the layout's buttons are ones you'd be happy for them to press, and use *New link* if a link gets out.
- **Security:** it's off by default and read-only unless you let tablets press buttons: otherwise the page can't change anything on the PC. The link carries a random secret, and *New link* stops old links working. It only answers addresses on the local network (private, link-local and loopback addresses), never the internet. When Windows asks about the firewall the first time, allow **private networks** only. It's plain HTTP, so someone watching your Wi-Fi traffic could see the dashboard's numbers.
- It listens on port 8787. If another app uses that port, change `tabletPort` in `settings.json` and turn the tablet view off and on.

#### Readings for other apps

Other apps on your network can read the dashboard's readings as JSON: Home Assistant, Rainmeter on another PC, or a script of your own. Turn on *Share readings with other apps* under *Tablet view* (off by default). It works only while the tablet view is on, since it's the same small server, and it needs the link's secret (the part after `k=` in the tablet link): in the address as `?k=`, or in an `Authorization: Bearer` header.

- `http://<PC>:8787/api/metrics` has every reading, `http://<PC>:8787/api/metrics/cpu.load` just one. Use the PC's address from the tablet link. Ids are the same as in a text widget (`{cpu.load}`), and the editor's metric picker lists them.
- Each reading has its `id`, `name`, `category`, `unit` (`percent`, `celsius`, `bytesPerSecond` and so on), `value` (a number, or `null` for a text reading or one with nothing yet), `text` (for a text reading, such as what's playing) and `formatted`, the reading as the dashboard shows it:

  ```json
  {"metrics":[{"id":"cpu.load","name":"CPU load","category":"CPU","unit":"percent","value":37.5,"text":null,"formatted":"37.5 %"}]}
  ```

- **What's shared:** every reading the dashboard has, including the text ones (what's playing, your next calendar event, your IP address and Wi-Fi network's name), with anyone on your network who has the link. Nothing else, and nothing can be changed this way. *New link* stops the old secret working, so give the apps the new one. Each request just reads the latest readings; nothing is drawn.

A [Home Assistant RESTful sensor](https://www.home-assistant.io/integrations/sensor.rest/), in `configuration.yaml`, reading two in one request every 10 seconds:

```yaml
rest:
  - resource: http://192.168.1.20:8787/api/metrics
    headers:
      Authorization: Bearer YOUR-SECRET
    scan_interval: 10
    sensor:
      - name: "PC CPU load"
        value_template: "{{ (value_json.metrics | selectattr('id', 'eq', 'cpu.load') | first).value }}"
        unit_of_measurement: "%"
      - name: "PC GPU temperature"
        value_template: "{{ (value_json.metrics | selectattr('id', 'eq', 'gpu.temp') | first).value }}"
        unit_of_measurement: "°C"
        device_class: temperature
```

A [Rainmeter](https://docs.rainmeter.net/manual/measures/webparser/) skin showing the CPU load as the dashboard does, every 5 seconds:

```ini
[Rainmeter]
Update=1000

[MeasureCpu]
Measure=WebParser
URL=http://192.168.1.20:8787/api/metrics/cpu.load?k=YOUR-SECRET
RegExp=(?siU)"formatted":"(.*)"
CodePage=65001
UpdateRate=5
StringIndex=1

[MeterCpu]
Meter=String
MeasureName=MeasureCpu
Text=CPU %1
FontColor=255,255,255
FontSize=14
AntiAlias=1
```

### USB screens

Small 3.5" USB screens (the *Turing Smart Screen* and the many unbranded ones like it, 320 × 480, sold for showing
PC stats inside a case) can show a layout too, with no software of their own running. Plug it in, then *Tablet and USB
screen* → *Show on a USB screen*. It finds the screen by itself (or pick its COM port under *Port*),
and you choose its *Layout* (the dashboard's, or one made for its size: give a layout a 480 × 320 canvas, or 320 × 480
for portrait), *Brightness* and *Way up* for how it's mounted. A layout of a different shape is scaled to fit, with the
theme's background around it.

It's drawn once a second like the tablet view, and only the parts that changed are sent, since these screens are
slow to fill completely. If it's unplugged it picks up again when it's back. Close the screen maker's own software
first: only one program can use it at a time. These screens speak the "revision A" protocol; newer models with other
protocols (the 5" and 8.8" ones, for instance) aren't supported yet. **This hasn't been tried on a real screen yet**,
so please report what happens.

## Command line

| Option | |
|---|---|
| `--data-dir <dir>` | Use a different settings folder |
| `--snapshot <file.png>` | Render the layout with live data to a PNG and exit |
| `--layout <name>` | With `--snapshot`, render this saved layout instead of the one being shown |
| `--warmup <seconds>` | Sampling time before a snapshot (default 3) |
| `--demo` | Made-up readings instead of this PC's, the ones the screenshots use: with `--snapshot`, or to run a copy of the dashboard with them (pair it with `--data-dir` to leave your own settings alone). Such a copy also pretends to find an update, to show *About and updates* at work |
| `--widget-docs <file>` | Regenerate the widget reference from the widget descriptors |
