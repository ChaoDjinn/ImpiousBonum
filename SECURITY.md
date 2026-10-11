# Security

## Reporting a problem

Please don't open a public issue for a security problem. Report it privately through
[GitHub's security advisories](https://github.com/ChaoDjinn/ImpiousBonum/security/advisories/new) instead, with what
you found, how to reproduce it, and what someone could do with it.

Fixes go into the latest release; there are no older versions to patch, since installed copies update themselves.

## What's worth knowing

- **Updates** are signed: installed copies only take a release whose package matches its signature, made with a key
  that never leaves the release environment ([Update signing](docs/development.md#update-signing)). The installer
  itself isn't Authenticode-signed yet.
- **The sensor service** runs as LocalSystem, to read hardware sensors and count frames. It only sends readings out over
  a local named pipe, which can't be written to.
- **The tablet view** is plain HTTP on your local network, guarded by a long random secret in its link (compared in
  constant time), and refuses connections from outside private address ranges. Anyone with the link can see the
  dashboard; with *Let tablets press buttons* on, they can also press its buttons. *New link* revokes old links.
- **Custom data** runs the commands and fetches the addresses you set up, from `settings.json` only. Layouts, including
  imported and gallery ones, can't add sources or commands.
- **Buttons** in a layout can open programs and send keys. Importing a layout lists any buttons that open programs, files
  or web addresses, and offers to clear what they open.
