# Licensing

GoTweaks has a layered licensing situation. Please read this before
redistributing source or binaries.

## GoTweaks' own source code — MIT

The original GoTweaks source code is licensed under the **MIT License**
(see [`LICENSE`](LICENSE)). Individual source files authored for GoTweaks
remain available under MIT terms.

## Distributed binaries (the combined work) — GPL-3.0

GoTweaks' shipped builds **link `libviiper.dll`**, which is built from a fork of
**VIIPER** (by Alia5) and is licensed under the **GNU General Public License,
version 3.0**. Linking a GPL-3.0 library produces a *combined work*.

Accordingly, **GoTweaks binary distributions (the combined work), and any
distribution that links `libviiper.dll`, are conveyed under the GPL-3.0**
(see [`COPYING`](COPYING)). The MIT grant above continues to apply to the
GoTweaks-original source files individually, but the aggregate work as
distributed is GPL-3.0. In practice this means redistributed GoTweaks builds
carry the GPL-3.0 freedoms, including the right to complete corresponding
source.

## Complete corresponding source (GPL-3.0)

- GoTweaks source: this repository.
- VIIPER fork that produces `libviiper.dll`: `https://github.com/corando98/VIIPER`
  (GPL-3.0; see its `LICENSE.txt` and `NOTICE.md`).

See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the VIIPER notice and
the written offer of source.

## Note on VIIPER client libraries

VIIPER additionally offers its generated *client libraries* under MIT. Those MIT
terms apply only to the thin socket clients, **not** to `libviiper.dll` (which
statically links the GPL-3.0 VIIPER core). GoTweaks distributes the GPL-3.0
`libviiper.dll`, so the combined-work terms above apply.
