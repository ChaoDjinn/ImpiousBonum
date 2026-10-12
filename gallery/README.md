# Layout gallery

Ready-made layouts to start from, and a place to share yours. Browse them on the *Gallery* page of the Impious Bonum
window: pick one of your screens to see the layouts that suit its shape, and choose simple or busy ones. Each is drawn
live with your own readings, and *Show on* puts it on that screen. The thirteen so far were made along with the app; a
layout of yours would be the first from someone else. See [Layout gallery](../docs/guide.md#layout-gallery) in the user guide.

| Layout | Screen | Widgets | What's on it |
|---|---|---|---|
| USB Mini | 480 × 320 | 3 | The time, and the CPU and GPU temperatures as dials |
| Small Screen | 800 × 480 | 4 | The time, CPU and GPU load, and the CPU temperature |
| Pocket | 1024 × 600 | 5 | The time, the weather, and CPU, GPU and memory dials |
| Gaming HUD | 1920 × 480 | 10 | Frame rate, frame times, 1% lows, temperatures and the session so far |
| Desk Companion | 1920 × 480 | 4 | World clocks, today's calendar, the weather and what's playing |
| System Monitor | 1920 × 480 | 8 | Load and network lines, every CPU thread, the busiest apps, drives and memory |
| Focus | 1920 × 480 | 4 | A 25-minute focus timer, today's agenda, the time and your music |
| Sim Rig | 1920 × 720 | 9 | The frame rate and how smooth it is, loads, temperatures and the time |
| Living Room | 1920 × 1080 | 5 | For a TV across the room: the time, the weather, today's calendar and what's playing |
| Command Centre | 1920 × 1080 | 12 | Loads, network, every CPU thread, temperatures, frame rates, apps, drives and memory |
| Workstation | 2560 × 1440 | 13 | Your day, a focus timer, weather, news, music, loads and Claude usage |
| Big Picture | 3840 × 2160 | 17 | World clocks, weather, calendar and news beside everything about your PC |
| Tower | 1080 × 1920 | 9 | Portrait: the time, the weather, loads, temperatures, apps and drives |

### Gaming HUD
![Gaming HUD](images/gaming-hud.png)

### Desk Companion
![Desk Companion](images/desk-companion.png)

### System Monitor
![System Monitor](images/system-monitor.png)

### Focus
![Focus](images/focus.png)

### Small Screen
<img src="images/small-screen.png" width="400" alt="Small Screen">

### USB Mini
<img src="images/usb-mini.png" width="320" alt="USB Mini">

### Pocket
<img src="images/pocket.png" width="480" alt="Pocket">

### Sim Rig
![Sim Rig](images/sim-rig.png)

### Living Room
![Living Room](images/living-room.png)

### Command Centre
![Command Centre](images/command-centre.png)

### Workstation
![Workstation](images/workstation.png)

### Big Picture
![Big Picture](images/big-picture.png)

### Tower
<img src="images/tower.png" width="360" alt="Tower">

These are drawn with made-up readings by `build/screenshots.ps1`; in the app, the gallery shows each one with yours.

## Sharing a layout

1. In the layout editor, *Layouts ▾ → Export…* saves your layout as one `.ibl` file, with its font, background and
   pictures inside. (A plain layout `.json` works too, if it has no pictures or fonts.)
2. Add the file to this folder, and an entry for it in [`index.json`](index.json): its `name`, `file`, `author`, a
   one-line `description`, the canvas `width` and `height`, and how many `widgets` it has (not counting shapes and
   pictures).
3. Open a pull request. The tests check that every gallery layout opens and is valid. If you can, run
   `pwsh build/screenshots.ps1` to add a picture of it to `gallery/images`.
