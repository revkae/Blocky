# Changelog

All notable changes to this package are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.1.0] — 2026-09-29

First release as a Unity package.

### Added
- The package: runtime (`Blocky.Data`, `Blocky.Compiler`, `Blocky.Localization`, `Blocky.Runtime`, `Blocky.Editor`, `Blocky.Game`), editor tools (`Blocky.Tooling`), and their EditMode tests.
- **GameObject › Blocky › Workspace** and the `Blocky Workspace` prefab it adds: the in-game workspace with its panel settings and stylesheets already wired.
- The package's `link.xml` is handed to the linker for every build, so IL2CPP keeps the reflection-only block ops however the package was installed.

### Changed
- The Program Editor window saves a new program to `Assets/Blocky/Programs` (it was `Assets/Demo/Programs`).
