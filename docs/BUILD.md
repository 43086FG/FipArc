# Building FipArc / 构建说明

FipArc is built with the C# compiler that ships with Windows — **no Visual Studio,
no MSBuild, no NuGet**. The whole build is a single `.cmd` file.

FipArc 使用 Windows 自带的 C# 编译器构建，**不需要 Visual Studio / MSBuild / NuGet**，
整个构建过程就是一个 `.cmd` 文件。

---

## Quick start / 快速开始

```bat
git clone https://github.com/<you>/FipArc.git
cd FipArc

:: 完整版 -> build\FipArcSetup.exe
build.cmd

:: SE 版 -> build_se\FipArcSetup.exe
build.cmd se
```

Both commands produce a single self-contained `FipArcSetup.exe`.

---

## Requirements / 环境要求

| | |
|---|---|
| OS | Windows 7 SP1 or later (x64) |
| Compiler | `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |
| .NET | Framework 4.8 (reference assemblies: `System.dll`, `System.Drawing.dll`, `System.Windows.Forms.dll`, `System.Xaml.dll`, WPF assemblies) |

On Windows 10 1903+ and Windows 11 the .NET Framework 4.8 runtime is already present.
On older systems install it from Microsoft first.

---

## C# language level — **important**

The bundled compiler is the **legacy** one, so the sources must stay at **C# 5**:

| ❌ Not available | ✅ Use instead |
|---|---|
| `$"…{x}…"` string interpolation | `"…" + x + "…"` or `string.Format` |
| `?.` / `?.()` null-conditional | `if (x != null)` |
| `=>` expression-bodied members | `{ return …; }` |
| auto-property initialisers `{ get; set; } = 1` | set it in the constructor |
| `nameof(x)` | `"x"` |
| `out var x` | declare `int x;` first |

Anything newer will fail to compile.

---

## What `build.cmd` does

1. Compiles `src/*.cs` into `FipArc.exe` (`/target:winexe`).
2. Compiles `installer/*.cs` + `src/Shell.cs` + `src/Lang.cs` twice:
   - once as `Uninstall.exe` (`/main:FipArcSetup.UninstallMain`)
   - once as `FipArcSetup.exe` (`/main:FipArcSetup.Program`)
3. Embeds resources into the setup executable:

```
/resource:assets\app.ico,app.ico
/resource:assets\ico\audio.ico,audio.ico        (only when the media icon set is used)
/resource:vendor\full\7z.exe,7z.exe
/resource:vendor\full\7z.dll,7z.dll
/resource:vendor\full\License.txt,License-7zip.txt
/resource:vendor\lav\lav.zip,LAVFilters.zip
/resource:<out>\Uninstall.exe,Uninstall.exe
/resource:<out>\FipArc.exe,FipArc.exe
```

`/define:SE` selects the SE edition (no legacy `f*` formats, curated association set).

---

## Third-party binaries you must supply

These are **not** committed (see `.gitignore`) — download them yourself:

| Path | What | Where |
|---|---|---|
| `vendor/full/7z.exe` | 7-Zip console | <https://www.7-zip.org/download.html> (`7z*-extra.7z`) |
| `vendor/full/7z.dll` | 7-Zip engine | same archive |
| `vendor/full/License.txt` | 7-Zip license | same archive |
| `vendor/lav/lav.zip` | LAV Filters x64 (optional codecs) | <https://github.com/Nevcairiel/LAVFilters/releases> |

If `vendor/full/7z.*` is missing, the build still succeeds but `.7z` / `.rar`
support is disabled at runtime.

If `vendor/lav/lav.zip` is missing, remove the corresponding `/resource:` line
from `build.cmd`; the app simply won't offer the built-in codec installer.

---

## Icons

`assets/app.ico` and `assets/ico/*.ico` are **multi-size ICO files with BMP/DIB frames**,
not PNG-compressed ones. `System.Drawing.Icon` cannot decode PNG-compressed ICO frames —
it produces noise, which looks like a mosaic in the installer banner.

If you regenerate them, keep BMP frames for every size:
16, 20, 24, 32, 40, 48, 64, 128, 256.

---

## Tests / 测试

```bat
build.cmd
build\CoreTest.exe
```

Headless assertions cover the non-UI core:

- `.fip` round-trip (pack → list → extract → byte compare)
- ZipCrypto encrypt/decrypt
- TAR and GZ packing
- tolerant extraction (one bad entry must not abort the rest)
- password detection and wrong-password handling
- path sanitisation (`../` escaping)

---

## Manual verification checklist

Before releasing:

1. Install to a clean profile, **without** administrator rights.
2. Right-click a folder → *Compress to* → each format.
3. Double-click the archive, expand the folder tree, drag a file **and a folder** out.
4. Encrypted archive: drag-out must prompt for the password.
5. Play an `.mp4` and an `.ogg`; click the video area to pause.
6. Switch the language in **Tools → Language** and confirm the UI reloads translated.
7. Uninstall; confirm the install directory, the `.fip` association and the context
   menus are all gone and the original icons are restored.
