# Third-Party Notices

GoTweaks is distributed under the MIT License (see `LICENSE`). It also
distributes the following third-party components under their own terms.

---

## VIIPER (`libviiper.dll`) — GNU General Public License v3.0

GoTweaks distributes `libviiper.dll`, which is built from a **fork of VIIPER**
(originally by **Alia5**, https://github.com/Alia5/VIIPER) and is licensed under
the **GNU General Public License, version 3.0**.

- Full license text: [`licenses/VIIPER-GPLv3.txt`](licenses/VIIPER-GPLv3.txt)
- Copyright © the VIIPER authors (Alia5 and contributors)

`libviiper.dll` is built from VIIPER's `./clib/` package, which statically links
the GPL-3.0 core (device backends and USBIP server). It is therefore a GPL-3.0
work — the MIT terms VIIPER offers for its *generated client libraries* do not
apply to this DLL.

### Written offer of source (GPL-3.0 §6)

The complete corresponding source for the version of `libviiper.dll` shipped
with this release of GoTweaks — including the fork's modifications and the
build scripts used to produce it — is publicly available at:

  https://github.com/corando98/VIIPER

For a specific released build, see the matching tag/commit referenced in that
repository's release notes.

> Compliance: GoTweaks resolves issue #84 via full GPL-3.0 compliance. Because
> the shipped binaries link this GPL-3.0 `libviiper.dll`, the **distributed
> combined work is conveyed under GPL-3.0** (see `COPYING` and `LICENSING.md`).
> GoTweaks' own source files remain individually MIT-licensed; the aggregate
> distribution is GPL-3.0, with complete corresponding source available from
> this repository and the VIIPER fork above.
