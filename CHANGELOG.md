# Changelog

What's new in each release. Each version's section becomes its release notes on GitHub, so write for people using
the app; changes since the last release go under *Unreleased*, renamed to the version when it's released. The [user guide](docs/guide.md) has the details.

## Unreleased

- The editor's *Widgets* list is the right way up: the widget at the top of the list draws on top of the others, as the note under the list now says, so dragging one up brings it forward. It used to be the other way round. Layouts aren't changed, only how the list shows them.
- Group widgets in the editor: select two or more and press Ctrl+G (or right-click, *Group*), say a shape and the weather widgets on it. The group has its own row in the *Widgets* list with its widgets under it, and they stay together in the drawing order. Clicking one on the canvas selects the whole group, to move it as one, and clicking it again selects just that widget. Drag a widget into or out of a group in the list, or drag the group's row to move all of it. *Rename group*, *Ungroup* (Ctrl+Shift+G) and duplicating a whole group are in the list's right-click menu too. The dashboard looks the same; groups are only for editing.
- Typing in the editor's *Widgets* list jumps to the widget (or group) whose name starts with what you type. Before, any L jumped to the next row and other letters did nothing.
- The Weather widget's *Forecast* setting, for days or hours, is now under *Content* beside *Days or hours* and *Hours apart*, rather than further down under *Text* where it was easy to miss.
- Web buttons and alerts that send to this PC itself (`localhost` or `127.0.0.1`, such as a Home Assistant running on it) go straight there, without first asking Windows whether to use a proxy. No proxy could reach them anyway, and with Windows' automatic proxy detection on, as it is by default, asking could hold the request up.

## 0.16.0

A big one: themes for the holidays and a schedule that puts them on by itself, nine new widgets and readings (countdowns, a photo frame, rain soon, the sun and moon, status checks, energy and cost, the microphone and camera, device batteries and game cover art), a layout for each tablet, snapping and alignment in the editor, themes made from a picture, warnings you can tell apart without colour, and backups of your whole setup.

**Holiday themes**
- Twelve new built-in themes for the holidays through the year: *Lunar New Year* (gold on lantern red), *Valentine* (rose pinks in a love-letter script), *Shamrock* (emerald and gold for St Patrick's Day), *Easter* (a light theme in lilac and mint), *Eid* (lantern gold on a teal night), *Diwali* (marigold glowing into rangoli pink), *Halloween* (pumpkin orange fading to witch's purple, with a jack-o'-lantern glow), *Day of the Dead* (marigold, magenta and turquoise), *Fireworks* (gold bursting into magenta, for Bonfire Night or New Year's Eve), *Hanukkah* (blue and silver by candlelight), *Christmas* (fairy-light gold and holly red on evergreen) and *Gingerbread* (icing, candy pink and gumdrop green). That's 55 in all.

**Schedule**
- Schedule rules can follow the sun: show *Night Watch* from sunset to sunrise, say, or a bright theme from sunrise to sunset. Sunset and sunrise are worked out on your PC for the weather's location, so nothing extra is fetched; without a location these rules never apply, and the dialog and *Pages and schedule* say so.
- Rules can be limited to the same dates every year, such as 24 October to 1 November or 20 December to 6 January (across New Year), on their own for the whole day or along with times, sunset and sunrise, or Windows' dark and light mode. A night belongs to the evening it starts, so a Halloween rule until 02:00 lasts into the small hours of 1 November.
- *Holiday themes* on *Pages and schedule* shows the matching new holiday theme on and around each holiday: *Lunar New Year*, *Valentine*, *Shamrock*, *Easter*, *Eid*, *Diwali*, *Halloween*, *Day of the Dead*, *Fireworks* for Bonfire Night and New Year's Eve, *Hanukkah*, *Gingerbread* and *Christmas*. Easter is worked out, and Hanukkah, Eid and Lunar New Year come from the Hebrew, Islamic and Chinese calendars that come with the app, so they move with the year; Diwali's dates run to 2035. It's off until you turn it on, and a rule in the schedule or a game that sets a theme still wins.

**Snow, leaves and more**
- A new theme setting, *Particles*: snow, autumn leaves, cherry-blossom petals or confetti falling behind the widgets, or embers rising from the bottom. Snow sways as it falls, leaves tumble, petals flutter, and embers drift up and fade. *Amount* sets how many, and *Colour* makes them all one colour; otherwise snow is white, leaves are autumn colours, petals pink and embers orange and gold, and confetti comes in the theme's colours.
- They look after their own cost. While a theme has particles, its widgets go without its glow, which made every frame many times dearer. They start slower on a big screen drawn without the graphics card, and slow down, or stand still, whenever Impious Bonum would otherwise use more than a tenth of a CPU core. On a 1920 × 480 strip they cost under 1% of one core.
- The built-in themes leave them off. The guide has the numbers, and suggests snow with *Christmas* and leaves or embers with *Halloween*.
- They stop while the dashboard is hidden or minimised, and the tablet view, USB screens and snapshots show them still. Moving backgrounds now stop while it's minimised too.

**Themes from a picture**
- *Colours from ▾* in the editor's theme settings makes the layout's colours from your Windows wallpaper, the layout's background image, a picture you choose, or Windows' accent colour. It shows in the preview straight away, one undo puts your colours back, and *Save theme as…* keeps it as a theme of your own.
- The background comes from a dark colour that covers much of the picture, or a light one for a mostly light picture, which makes a light theme. The accents come from its most vivid colours, and the text, bar track, warning and critical colours are made to go with them.
- However dark, light, dull or garish the picture, the text stays easy to read and the accents, warnings and critical colours stand out from the background as much as in the built-in themes. Warnings stay amber and critical red where they can, turning towards yellow, orange or pink when the picture's own colours are close to them.
- With Windows' accent colour, the theme is dark or light to match your apps in Windows. A wallpaper slideshow or Windows Spotlight gives the picture showing at the moment, and a solid colour wallpaper works too.

**Warnings you can tell apart without colour**
- A new theme setting, *Warning symbols*: a value in the theme's warning colour gets a ▲ beside it too, and one in its critical colour a ◆, so warning and critical don't rely on colour alone. Text, icon and value, stat tiles, graphs, gauges, bars, drives and *Top apps* show it; *CPU threads* keeps to colour. It's off unless you turn it on, and text that only just fits shrinks a little to make room for it.
- A new built-in theme, *Clear Sight*, for colour blindness: a sky blue accent, gold warnings and deep pink-red critical ones that stay apart with every common kind of colour blindness, with warning symbols on. That's 56 in all.
- Ticking a box in the editor's settings with Narrator now changes the setting, as clicking it does.

**Themes in the gallery**
- The *Gallery* page has a *Themes* tab: themes other people have made, each shown on the default layout with your own readings. *Add to my themes* keeps one with your saved themes, for any layout to use, asking before it replaces a theme of yours with the same name.
- Five to start with: *Deep Sea* (bioluminescent teal glowing on abyss blue), *Nebula* (violet fading into blue, with a pink glow), *Copper* (copper and brass on dark gunmetal), and two light ones, *Matcha* (soft greens on a pale foam) and *Paper* (e-ink greys).
- To share a theme of your own, see [Sharing a theme](gallery/README.md#sharing-a-theme).

**More layouts in the gallery**
- *Listening Room* (1920 × 480): what's playing with its album art, volume buttons, the time, and an audio visualiser right across the bottom.
- *Network* (1920 × 480): download and upload over time, the Wi-Fi signal and network name, the ping, the IP address and how much has gone each way.
- *Festive* (1920 × 480): the days left until Christmas, the time, the weather and your music, in the new Christmas theme.
- *Trick or Treat* (1280 × 800, for a tablet): the countdown to trick-or-treating, the time, tonight's weather hour by hour and your spooky playlist, in the Halloween theme.
- *Streamer* (1024 × 600, for a touch screen): buttons for your streaming app's scenes and muting your mic, an on-air stopwatch, and your FPS, upload, CPU and GPU.
- *Weather Station* (1080p): the weather now in detail, every three hours ahead and the week, dials for UV, humidity, air quality and wind, and the sun and pressure.
- *News Desk* (1080p): a ticker for each news feed, the time around the world, the weather and your calendar, laid out like a front page.
- *Case Strip* (480 × 1920): for the tall screens inside a PC case, with temperatures and FPS, CPU and GPU dials, every CPU thread, memory and an audio visualiser.
- That's 21 in all. The gallery's pictures now show the audio visualiser moving, as it would with music on.

**Lining widgets up in the editor**
- Widgets snap to each other as you drag or resize them: their edges and centres line up with other widgets' edges and centres, and with the canvas's edges and middle, and a thin line shows what they've lined up with. Hold Alt to place freely, as before.
- Select several widgets at once: Ctrl+click or Shift+click them in the preview or the *Widgets* list, drag a box around them on empty canvas, or press Ctrl+A for all of them. Drag one to move them all, nudge them with the arrow keys, or duplicate or delete them together.
- *Align* on the toolbar, and in the list's right-click menu, lines up the selected widgets by their left edges, centres, right edges, tops, middles or bottoms, spaces three or more out with equal gaps, or makes them all the same width or height. Each is one undo.

**Countdowns every year**
- A timer's *Until* can be a month and day with no year, such as `12-25` or `10-31 18:00`, which comes round every year: on the day it shows your *When done* text (*Merry Christmas!*, say) until midnight, then starts counting down to next year's.

**Countdowns widget**
- A new *Countdowns* widget, under *Time and calendar*, lists how long it is until the days you're looking forward to, soonest first: birthdays, holidays, a game's release, a trip. Each row shows its label and *69 days*, *Tomorrow* or *Today*, or the days, hours and minutes (*69d 02:13*).
- A month and day such as `12-25` comes round every year, starting again the day after; a date such as `2026-12-25 18:00` leaves the list once its day is over. The ones that are today are in the accent colour.
- It can have a heading, and says *Nothing coming up* (or your own words) when there's nothing left to count down to.

**Photo frame**
- A new *Photo frame* widget, under *Pictures and shapes*: a slideshow of the pictures in a folder, like a digital photo frame. Choose the folder in the editor, how long each photo shows, and whether they're shuffled or go by name or date. Tap it for the next one.
- Photos fade into one another, fill the frame or fit inside it, and can have rounded corners and their file name or the date they were taken along the bottom. Photos taken with the camera on its side show the right way up.
- It stays light with big photos: only the photo on show and the next one are read, at about the size they're shown, and nothing is read while the dashboard is hidden. Photos added to the folder turn up within a few minutes, and pictures that can't be read are passed over.
- The folder stays on your PC: exported layouts and layouts in the gallery leave it out.

**The cover art of the game you're playing**
- An *Image* widget can show the game you're playing instead of a picture from a file: set *Show* to *game* and pick its *Game picture*. For a Steam game that's Steam's own art: the tall *cover*, the wide *header* banner or the big *hero* picture from its library page. Other games show their own icon.
- The pictures come from Steam's cache on your PC, so nothing is downloaded. A game counts when Steam installed it, or when you've linked it to a layout or theme on the *Game layouts* page, which is how games from other stores get their icon.
- The widget is empty while no game is in front, or with *Hide when there's no game*, hidden. A quick alt-tab doesn't blank it.
- Two new readings for any widget: `game.name` (Steam's name for the game, or the one in its exe) and `game.appid` (its Steam app id).
- The *Gaming HUD* in the gallery shows the game's banner beside the session so far.
- An *Image* widget set to *fill* now crops its picture to the widget's own shape, rather than sometimes cutting off more than it needed to.

**Rain soon**
- A new *Rain soon* widget, under *Weather, news and Claude*, shows the rain in the next two hours at your location: a bar for each quarter of an hour, taller the heavier the rain, with the time every half hour under them. A drizzle still shows, and two dry hours are just a line.
- A line above the bars says what's coming: *Dry for the next 2 hours*, *Rain from 14:30*, *Rain stopping around 15:15* or *Light rain for the next 2 hours*, and snow when the forecast is for snow.
- The same as readings for any widget: `weather.rain.soon` for that line, and `weather.rain.next` for the minutes until it rains (0 while it is). An alert on it below 30 is a reminder to bring the washing in.
- It's most precise in central Europe and North America, where Open-Meteo's forecast is worked out a quarter of an hour at a time; elsewhere it's filled in from the hourly forecast. It comes in the same request as the rest of the forecast, so nothing more is sent.

**Sun and moon**
- A new *Sun and moon* widget, under *Weather, news and Claude*: the day as an arc from sunrise to sunset with the sun on its way across and how long it's light, and beside it the moon as it looks tonight, with its phase, how much of it is lit, the next full moon, and when it rises and sets. It can show just the sun or just the moon too.
- The moon is drawn the way up it looks from where you are: lit on the right as it grows in the northern hemisphere, on the left in the southern.
- New readings for any widget: the moon's phase (`moon.phase`, e.g. *Waxing gibbous*), how much of it is lit (`moon.illumination`), how many days since the new moon (`moon.age`), the next full and new moons (`moon.next.full`, `moon.next.new`, e.g. *Sat 24 Oct*), and moonrise and moonset (`moon.rise`, `moon.set`).
- It's all worked out on your PC from the date and the weather's location, so nothing is sent. The moon's phase works without a location; the sun, moonrise and moonset need one.

**Status checks**
- A *Status checks* page and a `status` widget for your own servers and services: a NAS, Pi-hole, Plex, the router or a website. Each check pings a host name or IP address, or asks a web address for its page (which can have to say a word, such as *Plex*), every 60 seconds or as often as you like. The widget lists them, or puts them in a grid, each with a dot in the accent colour while it's up and the critical colour while it's down, and how fast it answered.
- A check that fails is checked again 10 seconds later, and only counts as down if that fails too, so one lost answer doesn't.
- Each check has readings any widget can show, such as `status.nas.up`, `status.nas.ms` and `status.nas.since` (*3 h*), and `status.summary` says *All up* or which are down (*1 down: Plex*). *Alert…* beside a check sets up an alert for when it goes down.
- Nothing is checked until you add a check, and only the addresses you add are contacted.

**Energy use and cost**
- A new *Energy* page, and readings for any widget: your whole PC's power now (`power.total`: the CPU's and graphics card's from the sensor service, and 50 W for the rest, which you can change), the energy it's used today and this month (`energy.today`, `energy.month`), and what that costs at your price per kWh (`energy.cost.today`, `energy.cost.month`, such as *£0.48*).
- It's counted while Impious Bonum runs, needs the sensor service, and is kept across restarts. Today's total starts again at midnight, and the month's on the 1st.
- Turn on *Energy* → *Grid carbon intensity* for how clean Great Britain's electricity is now (`grid.carbon`, `grid.carbon.index`) and when it'll be cleanest in the next 24 hours (`grid.carbon.best`, such as *Lowest around 03:30*), from NESO's Carbon Intensity API every 30 minutes. Give the first half of your postcode for your region's figures. It's off until you turn it on, and nothing about you is sent but that half.

**Microphone and camera**
- Whether your microphone is muted (`mic.muted`) and its name (`mic.name`), and which apps are using the microphone or the camera (`mic.inuse`, `mic.apps`, `camera.inuse`, `camera.apps`, e.g. *Discord, OBS*). Give a text or icon widget a warning colour on one of them for an *on air* light, or a reminder that you're muted. Windows doesn't ask permission or show its microphone icon for any of it: Impious Bonum never uses the microphone or camera itself.
- A *Buttons* action, `mic-mute`, mutes and unmutes the microphone. With the new `mic` icon, the button shows `mic-off` while it's muted.
- `mic` and `mic-off` icons, for buttons and the *Icon and value* widget.

**Device batteries**
- A `batteries` widget for how much charge your wireless devices have left, as Windows' Bluetooth settings show it: a Bluetooth headset, mouse, keyboard or game controller, or an Xbox controller on its wireless adapter or a cable. Each has a bar, which turns the critical colour under 20 % (with a ◆ too, with *Warning symbols* on), and the widget follows devices as they connect and disconnect.
- Readings any widget can show, such as `battery.headset` (*72 %*), and `battery.low`, the name of the one running low (*Mouse*). That has no reading while none is, so a warning can show only when it's needed.
- Read every minute from what Windows already knows: Impious Bonum never connects to a device itself. Nothing shows on PCs without such devices.

**Buttons that send web requests**
- A *Buttons* action, `web`, sends a web request when you tap it: run a Home Assistant script or scene, switch a smart light, start a Node-RED flow or send a notification to your phone with ntfy. Each button has its address, a method (`GET`, `POST` or `PUT`), and if it needs them a body, such as JSON, and a header, such as `Authorization: Bearer` and a token. The guide has examples for Home Assistant and ntfy.
- The button lights up for a moment in the accent colour when the request worked, or the critical colour when it didn't, and `dashboard.log` says why. A tablet allowed to press buttons can press these too.
- Importing a layout, from a file or the gallery, lists where its web buttons send and asks before keeping them, as it does for buttons that open programs. Exporting one leaves their headers out, since they're usually a password or token.

**A layout of its own on each tablet**
- A tablet can show a different saved layout from the main dashboard: pick it under *Layout for the link* on the *Tablet and USB screen* page, then open that link or scan its QR code on the tablet. It keeps to that layout, in its own theme, whatever game layouts and the schedule do to the main dashboard.
- With *Let tablets press buttons* on, taps press the buttons on the layout that tablet is showing.
- A layout is only drawn while a tablet is showing it, and let go a few minutes after the last one closes, so it costs nothing the rest of the time.
- Renaming or deleting a layout stops links to it working: the tablet says there's no layout of that name, and the page gives you a new link.

**Readings for other apps**
- *Share readings with other apps* on the *Tablet and USB screen* page (off by default) lets Home Assistant, Rainmeter or a script of your own read every reading as JSON while the tablet view is on: its name, unit, value and the value as the dashboard shows it. They need the tablet link's secret, and *New link* stops old ones working.
- The [guide](docs/guide.md#readings-for-other-apps) has a Home Assistant sensor and a Rainmeter skin to start from.

**Backing up your settings**
- *General* → *Back up…* saves your whole setup as one `.ibbackup` file: your settings, every saved layout with its fonts and pictures, and your saved themes. Keep it somewhere safe, or take it to another PC. It has private things in it, such as your calendar links and the tablet link, so keep it to yourself.
- *Restore…* shows what's in a backup (when it was made, by which version, how many layouts and themes) and asks before replacing anything. It saves your setup as it is first, in `%AppData%\ImpiousBonum backups`, so you can go back, then Impious Bonum starts again with the backup's. A backup from a newer version says so first.
- Logs and the list of game sessions aren't in a backup, and restoring leaves yours as they are.

**Safer shared layouts**
- A layout from someone else can no longer fetch a font from another PC. Its theme, or one of its widgets, could name a font by an address such as `file://server/fonts/#Name`, which would have Windows reach out to that PC as soon as the layout was drawn, and send it your sign-in. Importing a layout, or picking one in the gallery, now leaves such a font out and says so, and one already in a saved layout is drawn in the theme's font (or Segoe UI) instead and listed as a problem in the editor.

## 0.15.0

Alerts that do something when they go off, the next event from just one calendar, and more about your network and battery.

**Alert actions**
- Alerts can do more when they go off: play a sound (a Windows sound or a `.wav` file), switch to another layout, run a program or script (which gets the alert's text and reading in environment variables), or send the alert to a web address, as JSON for Home Assistant's webhooks or as plain text for ntfy notifications on your phone.

**Calendars**
- Each calendar has its own next-event metrics as well as the ones for all your calendars, so a text widget can show the next event in just one: `{calendar.work.next.title}` at `{calendar.work.next.time}`. The metric picker lists them under *Calendar: Work* and so on.

**Network and battery**
- Each network adapter has its own download and upload rates, link speed (*1 Gbps*) and IP address, such as `{net.ethernet.down}` or `{net.wi-fi.speed}`, listed in the metric picker under *Network: Ethernet* and so on.
- Wi-Fi signal strength as a percentage (`wifi.signal`) and in dBm (`wifi.rssi`), and the network's name (`wifi.ssid`), on PCs with Wi-Fi. They're read without location access, so Windows doesn't ask for it or show its location icon.
- The battery's time left (`sys.battery.time`), on laptops and with a USB UPS that Windows sees as a battery.

## 0.14.0

Much more weather (more readings, air quality and pollen, severe weather warnings, more places and a choice of details), news tickers for each feed, fonts you can see before you pick, text that shrinks to fit, and dragging to reorder in the editor.

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

**Text that fits, whatever the font**
- Changing a widget's font no longer cuts its text off. The clock (other places too), timer, stat tile, gauge (its label too), graph, multi-line graph, bars, rows, drives, *Top apps*, *Now playing*, buttons, weather, *Claude usage*, *Agenda* and *News* now have *Shrink to fit*, on by default as on the text and *Icon and value* widgets. Text that doesn't fit gets smaller evenly until it does, and text that fits keeps its size.
- *Shrink to fit* minds the height too, so a big font, or one with tall letters, shrinks instead of losing the top or bottom of its text. A line that's only a little taller than its widget, with nothing actually cut off, is left as it is.
- A label and its value on one line, as in graphs, bars, rows and drives, get smaller together rather than run into each other.
- *Top apps* names still end with … to make room for the CPU and memory, down to their first few letters; past that the rows get smaller.
- *Agenda* and *News* leave out the rows at the bottom that would be cut off, rather than shrink the whole list, and a first row too tall for the widget on its own, such as a ticker's headline, gets smaller. Their headings and the news's source and age end with … when they're too long, like the titles.
- The weather gets smaller as a whole, so a wide font no longer squeezes the forecast into a jumble.

**A news ticker for each feed, and feeds in the order you choose**
- The *News* widget's new *tickers* style gives each feed a ticker of its own, a line each, taking turns to move on so they don't all change at once. *Headlines* is how many each one goes through, and a story two feeds share shows in both. It uses the headlines already fetched, so there are no extra requests.
- *Feeds* in the editor is a list of your feeds to tick, rather than names to type: untick *All feeds*, which leaves each feed ticked, then untick the ones you don't want and drag the rest into order by their handles (or Alt+Up and Alt+Down). The tickers go in that order, or with *All feeds*, in the *News feeds* page's. The last feed ticked stays until you tick another, so swapping one for another doesn't go back to all of them. A feed you've since removed or renamed stays ticked, marked *not found*, so you can untick it. Layouts that already name their feeds keep working as they are.
- Since commas separate the names a widget shows, and *all* means every one, a new feed or calendar can't have a comma in its name or be called *all*. A feed from before that does is greyed out in *Feeds* and only shows with *All feeds*; to choose it on its own, remove it and add it again with another name.

**Air quality and pollen**
- Turn on *Weather* → *Air quality and pollen* for readings of the air at your weather's location: an air quality index with its level, from *Good* to *Extremely poor* on the European index or *Good* to *Hazardous* on the US one, and fine particles (PM2.5), PM10, ozone and nitrogen dioxide. *Air quality index* chooses which; *Automatic* uses the European index in Europe and the US index elsewhere.
- Grass, tree and weed pollen, each with its level (*None* to *Very high*), and the worst of them in a few words such as *Grass high*. Pollen is only counted in Europe, in season.
- They're readings for any widget, like the weather's: `{weather.aqi.level}` or `{weather.pollen}` in a text widget, say, or a gauge of `weather.aqi`. The *Weather* page's location line shows them too.
- It's off until you turn it on. Then the place's coordinates go to Open-Meteo once an hour, and nothing else is sent. Air quality data by CAMS (Copernicus), via Open-Meteo.com.

**Severe weather warnings**
- Turn on *Weather* → *Severe weather warnings* for warnings of storms, floods, snow, ice, heat and the like where you are: from the Met Office in the UK, and the National Weather Service in the US. Elsewhere, the *Weather* page says they aren't available there yet.
- The *Weather* widget shows the most serious one along the bottom, with *and 2 more* if there are others (only where it shows the weather now, so a forecast beside it doesn't repeat it): yellow and amber warnings in the theme's warning colour, red ones in its critical colour. Tap it to read the warning on the Met Office's or the NWS's site. Without any warnings the widget looks as it always has, and *Warnings* in the editor leaves them out.
- A Windows notification when a warning comes out or gets more serious; click it to read the warning. Each comes up once, and *Notify me* turns them off.
- Readings for any widget: `{weather.warning}` for the most serious one's headline, `{weather.warning.level}` for its level (*Yellow*, *Amber* or *Red*, or the NWS's *Minor* to *Extreme*) and `weather.warnings` for how many there are.
- In the UK, *Automatic* picks the Met Office area your location is in, and *Met Office area* chooses another if you're near the edge of one. Places you pick now remember their country, which decides whose warnings they get.
- It's off until you turn it on. Then every 10 minutes, for a UK place only its Met Office area is sent to the Met Office, and for a US place its coordinates go to the National Weather Service. A place anywhere else is sent nowhere. Warnings from the Met Office (© Crown copyright) and the US National Weather Service.

**Fonts you can see before you pick**
- The editor's font list shows each font's name in that font, as Word does. Symbol and icon fonts like *Wingdings* show their name as usual, with a few of their symbols beside it.
- No more clicking through fonts one at a time: with the list open, the font under the mouse (or the one you move to with the arrow keys) shows on the widget straight away. Click it or press Enter to keep it; press Esc or click elsewhere and the font you had comes back. Looking through the list doesn't fill up undo or count as a change to save.
- With the list closed, Up and Down change to the next font straight away, and a single undo goes back to the font you started with.
- A widget's own *Font* has *Theme font* at the top of the list, to go back to the layout theme's font.
- The list opens quickly however many fonts you have, as only the fonts in view are loaded.

**Weather for more places**
- Save other places under *Weather* → *More places*, such as a holiday destination or family abroad, and the *Weather* widget can move between them and your location: tick them under *Places* in the editor, in the order you like. It shows one at a time with its name, moving on every 10 seconds (*Seconds per place* changes that), and a tap on the widget moves it on straight away, along with any weather widget beside it showing the same places. The tablet view moves on by itself.
- On the *Weather* page each place shows its weather now. Drag them into order, remove them, or *Make main* to swap one with your location, say when you get there (changing your location to one of them takes it off the list instead). Your location keeps the weather readings, air quality and warnings, so a warning only shows while it's on show.
- Each place's coordinates go to Open-Meteo every 15 minutes for its forecast, like your location's, and nothing else is sent for them. Without more places nothing more is fetched, and a weather widget with no places ticked looks just as it did.

**Choose the weather widget's details**
- *Details to show* on the *Weather* widget chooses what goes beside the temperature, in the order you drag them into: the conditions, today's high and low, feels like, wind (*12 km/h SW*), gusts, humidity, pressure with an arrow for rising, falling or steady (*1012 hPa ↓*), UV with its level (*UV 3 Moderate*), today's chance of rain and rainfall, sunrise, sunset, daylight, dew point, visibility and cloud cover, and with air quality and pollen on, those too. They go a few to a line, each with a short label where it needs one, and a busy widget gets smaller as a whole to fit.
- *Forecast details* chooses what each day or hour shows under its temperature: its chance of rain (as before), rainfall, wind, gusts and UV.
- On another of your places, the details are that place's own. Your weather widgets look just as they did until you choose others, and *Details* still turns them all off.
- They're all from the forecast already fetched, so nothing more is sent.

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
