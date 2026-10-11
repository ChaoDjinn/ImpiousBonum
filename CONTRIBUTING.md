# Contributing

Thanks for helping. Bug reports, ideas, layouts for the gallery and pull requests are all welcome.

- **Something wrong, or an idea?** [Open an issue](https://github.com/ChaoDjinn/ImpiousBonum/issues/new/choose) and pick *Something's wrong* or *An idea*. Tray → *Help* → *About Impious Bonum* → *Copy details* gives the version details a bug report asks for.
- **A layout to share?** See [Sharing a layout](gallery/README.md#sharing-a-layout).
- **Security problem?** Please report it privately: see [SECURITY.md](SECURITY.md).

## Changing the code

[Developing Impious Bonum](docs/development.md) covers building, the tests, the screenshots and how the code is laid out. In short:

1. You need Windows 10 or 11 and the .NET 10 SDK.
2. `dotnet test ImpiousBonum.slnx -c Release` builds everything and runs the tests. Warnings are errors.
3. `dotnet format whitespace ImpiousBonum.slnx` lays the code out the way `.editorconfig` says; CI checks it.
4. If a widget's settings change, regenerate the widget reference (a test fails until you do):
   `dotnet run --project src/ImpiousBonum.App -c Release -- --widget-docs docs/widgets.md`.
5. Say what's new for people using the app under *Unreleased* in [CHANGELOG.md](CHANGELOG.md), and update the
   [user guide](docs/guide.md) if a feature or a tray path changed.

Some habits the code follows, which reviews will look for:

- Write for the person using it. Messages, tooltips and the guide use plain words and say what to do next
  (*tray → Weather → Set location…*), not what went wrong inside.
- Keep the dashboard light. It redraws once a second and runs all day, so no work per tick that could be done once,
  and nothing that keeps the graphics card or the network busy when it doesn't need to.
- Catch only the exceptions you expect (`catch (Exception ex) when (ex is IOException or …)`), and keep running: a
  status display is more use carrying on after one bad reading than stopping.
- Comments say why, not what. Every public type and member has a `<summary>`.
- New behaviour comes with tests; anything that draws can be tested off screen (see the widget tests).
