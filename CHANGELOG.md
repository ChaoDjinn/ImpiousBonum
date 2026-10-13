# Changelog

What's new in each release. Each version's section becomes its release notes on GitHub, so write for people using
the app; changes since the last release go under *Unreleased*, renamed to the version when it's released. The [user guide](docs/guide.md) has the details.

## Unreleased

**More weather readings and longer forecasts**
- New weather readings for any widget: wind gusts and the wind direction in degrees, the UV index now and today's highest with its level (*Low* to *Extreme*), air pressure and whether it's rising, falling or steady over the last three hours, dew point, visibility, cloud cover, rain or snow in the last 15 minutes and today's total, the chance of rain this hour and tomorrow, and today's daylight (*10 h 49 min*). They come in the same request as the forecast Impious Bonum already fetches, so there are no extra requests and nothing more is sent.
- How high the sun is, in degrees, worked out on your PC from your location and the time, so it keeps moving between forecasts.
- The *Weather* widget's forecast goes up to 16 days, or 48 hours, ahead.
- *Chance of rain from* on the *Weather* widget chooses when a day or hour shows its chance of rain: from 30% as before, or 0 to always show it.
- With imperial units, pressure is in inches of mercury, visibility in miles and rain in inches.

**More precise weather places**
- *Set location…* finds suburbs, villages and postcodes, and you can say which place you mean by adding its town, county or country after a comma: `West Derby, Liverpool` finds the one in Liverpool rather than the one in South Africa. The start of a name is enough, and `UK` and `USA` work as countries.
- Places in the list say which town or county they're in, such as *West Derby, Liverpool, England, United Kingdom*, so places with the same name are easy to tell apart.
- When Open-Meteo doesn't know a place, the search asks OpenStreetMap instead, which knows more suburbs, parks and postcodes (`L12 5HJ`, say). It's only asked when you press *Search* or Enter, never as you type, and the forecast still comes from Open-Meteo using the place's coordinates.

**Drag to reorder in the editor**
- Drag a widget up or down the editor's *Widgets* list to change which draws on top, in place of the arrow buttons in the toolbar. From the keyboard, Ctrl+] brings the selected widget forward and Ctrl+[ sends it back, and right-clicking a widget in the list has *Bring forward*, *Send back*, *Duplicate* and *Delete*.
- Entries in a widget's lists (a rows widget's rows, buttons, other places, warning colours and so on) have a handle at their left to drag them into place, instead of up and down arrows. From the keyboard, Alt+Up and Alt+Down move the entry from its handle or its text boxes.

**Air quality and pollen**
- Turn on *Weather* → *Air quality and pollen* for readings of the air at your weather's location: an air quality index with its level, from *Good* to *Extremely poor* on the European index or *Good* to *Hazardous* on the US one, and fine particles (PM2.5), PM10, ozone and nitrogen dioxide. *Air quality index* chooses which; *Automatic* uses the European index in Europe and the US index elsewhere.
- Grass, tree and weed pollen, each with its level (*None* to *Very high*), and the worst of them in a few words such as *Grass high*. Pollen is only counted in Europe, in season.
- They're readings for any widget, like the weather's: `{weather.aqi.level}` or `{weather.pollen}` in a text widget, say, or a gauge of `weather.aqi`. The *Weather* page's location line shows them too.
- It's off until you turn it on. Then the place's coordinates go to Open-Meteo once an hour, and nothing else is sent. Air quality data by CAMS (Copernicus), via Open-Meteo.com.

**Fonts you can see before you pick**
- The editor's font list shows each font's name in that font, as Word does. Symbol and icon fonts like *Wingdings* show their name as usual, with a few of their symbols beside it.
- No more clicking through fonts one at a time: with the list open, the font under the mouse (or the one you move to with the arrow keys) shows on the widget straight away. Click it or press Enter to keep it; press Esc or click elsewhere and the font you had comes back. Looking through the list doesn't fill up undo or count as a change to save.
- With the list closed, Up and Down change to the next font straight away, and a single undo goes back to the font you started with.
- A widget's own *Font* has *Theme font* at the top of the list, to go back to the layout theme's font.
- The list opens quickly however many fonts you have, as only the fonts in view are loaded.

## 0.13.0

Impious Bonum has a window of its own, and the tray menu keeps to the basics.

**A window for everything**
- *Open Impious Bonum* in the tray (or double-click the icon, or start it again from the Start menu) opens a window with a sidebar, like Windows' Settings: *Layouts*, *Gallery*, *Screens*, *Tablet and USB screen*, *Pages and schedule*, *Game layouts*, *Sensors*, *Weather*, *Calendars*, *News feeds*, *Claude usage*, *Custom data*, *Alerts*, *Hotkeys*, *Logging*, *General* and *About and updates*. Changes take effect as you make them.
- The layout editor is the *Layouts* page. Its *Screen* list is always there, so you pick which screen's dashboard you're editing, then its layout. Leaving the page with unsaved changes asks first.
- *Screens* shows your monitors as Windows arranges them, with the dashboard on each. Pick one to choose its layout, edit it, make the main dashboard a window, add a dashboard there, or move the main one there.
- *About and updates* checks for updates, shows the download as it goes, and has *Restart to update* right there once it's ready, instead of a second trip to the tray. A dot in the sidebar says when one is waiting.
- The tray menu now has *Open Impious Bonum*, *Edit layout…*, *Layout* (to switch quickly) and *Exit*, plus *Restart to update* when there's an update.

**A bigger gallery**
- Eight new layouts for common screen sizes, from a 3.5" USB screen (480 × 320) and a 7" panel (1024 × 600) to a 1920 × 720 sim-rig bar, 1080p, 1440p and 4K monitors, a TV across the room, and a monitor turned on its side.
- The *Gallery* page shows the layouts that suit one of your screens' shape, closest in size first, and can show only simple ones (a few big things) or busy ones (lots at once). *Show on* puts the layout straight on that screen, adding a dashboard there if it has none.

## 0.12.0

Each screen can have a layout of its own: a new screen gets a new, empty layout, and the editor edits whichever screen you open it from.

**A different layout on each screen**
- *Display* → *More dashboards* → *Add on* gives the new dashboard a new, empty layout of its own, sized for its screen and in the main dashboard's theme, and opens the editor on it. It used to start with one of your existing layouts, so with only one layout it showed a copy of the main dashboard.
- The editor edits the dashboard you open it from: right-click or press and hold on another screen's dashboard, or tray → *More dashboards* → the dashboard → *Edit layout…*. With more than one dashboard, a screen list beside *Layout* moves between them, and picking a layout there changes only that screen's. Editing another screen's layout no longer switches the main dashboard to it.
- Renaming a layout keeps it on the screens and the USB screen that show it, and deleting one moves those screens to the main dashboard's layout rather than a missing one.

## 0.11.1

- Submenus in the tray menu stay open again. With the tray at the right of the screen, a submenu's own submenus (*Display → When you're away*, *More dashboards → Add on*) opened over the main menu and closed as soon as you moved onto them, and with a monitor to the right, submenus opened on that monitor. Each now opens beside the last on the tray's own screen.

## 0.11.0

A polish release: a new tray menu with Help and About, a cleaner editor and dialogs, text that fits, a better first run, and fixes for the editor asking to save changes nobody made.

**A tidier tray**
- A new look for the tray menu: dark or light to match the taskbar, with Windows 11's rounded corners, roomier rows, an icon beside each main item, and check marks in the app's orange.
- The tray menu is grouped: *Edit layout…*, *Layout* and *Display* first, then where readings come from (*Sensors*, *Weather*, *Calendar…*, *News feeds…*, *Claude usage*, *Custom data…*), then *Alerts…*, *Hotkeys…* and *Logging…*.
- *FPS from* is under *Sensors*, and *Open layout file* and *Reload* under *Layout*.
- A new *Help* menu has the user guide, widget reference and what's new for your version, *Report a problem…*, *Open settings folder*, and *About Impious Bonum* (with *Copy details* for a problem report).
- Screens are listed by name, like *SAMSUNG · 3840×2160*, rather than *DISPLAY6*.
- A sharper tray icon at 125% and 150% scaling.
- Clicking a notification about an error opens the log, and clicking *Impious Bonum x.y.z is ready* restarts into the update.

**A cleaner editor and dialogs**
- The editor's toolbar has icons, and *Add widget* groups widgets by what they're for: readings, time and calendar, media and controls, weather, news and Claude, and pictures and shapes. The widget reference has the same groups.
- The widget list shows each widget's type and what tells it apart, without a scroll bar, and the preview sits on its own panel in light mode too.
- Dialogs have their buttons along the bottom as in Windows 11, follow light and dark mode throughout, show hints in empty boxes (*Search by name, id or hardware…*) and say when a list is empty. They no longer open complaining about fields you haven't filled in yet.
- The *Close* buttons in *Alerts…* and *Custom data…* now close them (and so does Esc).
- The colour picker opens in front of the editor, and the swatch shows the theme's *track* and *background* colours too.
- *Show it as* in the metric picker appears once you've picked a metric.
- The editor opens on your main screen, whichever screen you open it from. Opened from a dashboard on a short screen, it used to hang off it.
- Selecting a widget in the editor no longer marks the layout as changed (and asks to save on closing), or writes its default colours into the layout.

**A better first impression**
- On a PC with only one screen, the dashboard now starts as a window you can move and resize, instead of covering the whole screen.
- A tidier default layout for new installs: CPU, GPU and memory graphs, each labelled, with the temperatures and frame rate beside them, the clock centred, and drives and network on the right. Your own layouts aren't changed.
- Drives are labelled `C:` rather than `C:/`.

**Text that fits**
- Text and *Icon and value* widgets shrink long text to fit their width instead of cutting it off, so 144 FPS fits where 60 did. *Shrink to fit* turns it off.
- Text widgets can hide until one of their readings arrives (*Hide without a reading*), like the Gaming HUD's session lines before you've played.
- The Gaming HUD gallery layout no longer cuts off its frame rate, or says "141 FPS FPS average".

**Also**
- Screenshots of the dashboard, the themes, the gallery's layouts and the editor in the README and the guide.
- If `settings.json` can't be read, Impious Bonum says so and keeps a copy as `settings.unreadable.json` before starting with the defaults, rather than quietly replacing your settings.

## 0.10.0

More than one dashboard, widgets with their own colours and theme, colour weather icons, and setting widgets up from the editor.

**Weather widget**
- *Weather now* can be turned off, leaving just the forecast: one widget of the hours ahead beside another of the days, say.
- Full-colour icons: a yellow sun, white clouds, grey rain clouds with blue rain, white lightning. Outline icons in one colour are still the default.
- Its own text and detail colours, instead of always the theme's.

**Every widget can override the theme**
- Each widget has its own *Widget theme* settings: font, weight, text, secondary, accent, bar track, warning and critical colours. Empty uses the layout's theme, as before.
- Widgets that borrowed the theme's bar track for other things have their own colours now. The *Now playing* album-art background and progress bar no longer change with the bar track. Bars, drives, gauges, CPU threads and buttons each have their own track or tile colour, and bars, drives, buttons and *Now playing* their own text colours.

**Set up widgets from the editor**
- Select a weather, agenda, news or Claude usage widget and the top of its properties says what's set up and has a button for it: the weather location, calendars, news feeds or connecting Claude Code. The tray still has them too.

**More dashboards**
- With more than one spare screen, give each a dashboard with a layout of its own: tray → *Display* → *More dashboards*. Each fills its screen, and the editor's changes show live on every dashboard showing that layout.

## 0.9.0

Fifteen new features and seven new widgets, from top apps to a layout gallery.

**New widgets**
- **Top apps**: the apps using the most CPU or memory, like a small Task Manager, with an app's processes counted together.
- **Timer**: a stopwatch, a countdown with a notification when it's done, or the time left until a time or date. Tap to start and pause.
- **Agenda**: what's coming up in your calendars, from their iCal links (Google, Outlook, iCloud). Tray → *Calendar…*.
- **News**: headlines from RSS and Atom feeds, as a list or a ticker; tap one to read it. Tray → *News feeds…*.
- **Multi-line graph**: several metrics on one scale, each in its own colour, with a legend.
- **Audio visualiser**: bars that move with whatever the PC is playing. It only listens while it's on screen.
- **CPU threads**: every thread's load as bars or a heatmap. **Stat tile**: a big value with its trend and range. **Images and shapes**: pictures, panels, circles and dividers.
- The **clock** can show other places' time zones too, as a world clock.

**New features**
- **Layout gallery** (tray → *Layout* → *Gallery…*): browse shared layouts drawn live with your own readings, and import one. It starts with five.
- **Hotkeys** (tray → *Hotkeys…*): show or hide the dashboard, turn pages, open the editor or switch layout from anywhere, even in a game.
- **Session recaps**: when you stop playing a linked game, a summary of its FPS, 1% lows, loads and temperatures, also saved to `sessions.csv`.
- **Logging** (tray → *Logging…*): readings to daily CSV files, with each one's average, lowest and highest.
- **Alerts** (tray → *Alerts…*): a notification or a flashing banner when a reading stays out of bounds.
- **Custom data** (tray → *Custom data…*): your own metrics from web addresses and commands.
- **Pages**: swipe between layouts, or rotate through them.
- **Dimming**: dim or black out on a schedule or when you're away, and tap to peek.
- **USB screens** (tray → *Display* → *USB screen*): show a layout on a small 3.5" Turing-style screen. Not yet tried on real hardware, so please report how it goes.
- **Tablets can press buttons** (opt-in, off by default): taps on the tablet view press the layout's buttons and media controls.

**Themes**
- **Gradients and glow**: the accent and background can fade to a second colour, and widgets can glow. Outrun, Vaporwave, Aurora, Magma, Cyberpunk and Toxic use them.
- **Moving backgrounds**: an animated GIF or a video (MP4, WMV) behind the widgets, paused while the dashboard is hidden.

**Also**
- `settings.json` no longer gets `"layout": null` lines.
- A new [user guide](docs/guide.md) covers every feature.

## 0.8.0

**New**
- **40 new built-in themes**, from clean to retro to neon and the well-known editor palettes, with colour samples in the theme picker.
- **Gauge widget**: a dial (arc, semicircle or ring) for any metric.
- **Now playing and buttons**: what's playing with album art and controls, and buttons for media keys, opening programs, keyboard shortcuts and switching layout.
- **Weather**: conditions and forecast where you are, from Open-Meteo, with no account needed.
- **Frame times and 1% lows**: see stutter, not just average FPS.
- **Schedule**: switch theme or layout by time of day, or with Windows' dark mode.

## 0.7.1

- **Drives widget**: any metric in each drive's row, plus the drive's health, temperature and hours from the sensor service.

## 0.7.0

- **More metrics**: GPU memory used %, GPU load per engine (3D, decode, encode, compute), CPU clock, per-thread load, disk activity per drive, uptime and battery.
- **Claude usage**: the app offers to connect Claude Code when you add the widget, and explains what the Desktop app can't do.

## 0.6.0

- **Claude usage widget**: your Claude plan's session and weekly limits, from Claude Code's status line.

## 0.5.1

- The sensor service now picks up PawnIO even when that starts after the service.

## 0.5.0

- **Tablet view**: show the dashboard in a tablet's or phone's browser over your Wi-Fi.

## 0.4.0

- **Windowed mode**: the dashboard as a window you can drag, resize, lock in place and keep on top.

## 0.3.0

- **Saved layouts**: keep several named layouts and switch between them from the tray.
- **Game layouts**: show a game's linked layout while it's in front.
- **Saved themes**: reuse a font and colour scheme across layouts.
- **Sharing layouts**: export a layout, with its font and pictures, as one `.ibl` file, and import others'.
- **Background image** behind the widgets, and **warning colours** when a value crosses a threshold.
- FPS for Vulkan and OpenGL games too.

## 0.2.3

- Re-release of 0.2.2's signed updates (0.2.2 was tagged on an older commit).

## 0.2.2

- Updates are now signed, and the app only installs signed ones.

## 0.2.1

- A log file (`dashboard.log`) for startup, updates and errors, which also records unexpected errors.

## 0.2.0

The first release with an installer.

- A dashboard for a spare screen: CPU, memory, GPU, drives, network and a clock, filling the monitor you pick.
- **Layout editor** with live preview, drag and resize, a metric picker with live values, and touch editing on the dashboard itself.
- **Sensor service** for temperatures, fans, clocks, power and per-app FPS.
- **Installer and automatic updates** from GitHub Releases.
