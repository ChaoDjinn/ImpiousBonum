# Layout gallery

Layouts people have shared. Browse them in the app with tray → *Layout* → *Gallery…*: each one is drawn live with
your own readings, and *Import* adds it to your layouts. See [Layout gallery](../docs/guide.md#layout-gallery) in the
user guide.

| Layout | Screen | What's on it |
|---|---|---|
| Gaming HUD | 1920 × 480 | Frame rate, frame times, 1% lows, temperatures and the session so far |
| Desk Companion | 1920 × 480 | World clocks, today's calendar, the weather and what's playing |
| System Monitor | 1920 × 480 | Load and network lines, every CPU thread, the busiest apps, drives and memory |
| Focus | 1920 × 480 | A 25-minute focus timer, today's agenda, the time and your music |
| Small Screen | 800 × 480 | The time, CPU and GPU load, and the CPU temperature |

## Sharing a layout

1. In the layout editor, *Layouts ▾ → Export…* saves your layout as one `.ibl` file, with its font, background and
   pictures inside. (A plain layout `.json` works too, if it has no pictures or fonts.)
2. Add the file to this folder, and an entry for it in [`index.json`](index.json): its `name`, `file`, `author`, a
   one-line `description`, and the canvas `width` and `height`.
3. Open a pull request. The tests check that every gallery layout opens and is valid.
