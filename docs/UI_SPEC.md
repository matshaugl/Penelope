# UI specification

Canonical document for screens, copy, design tokens, and the brand mark. Product requirements behind these screens are in the [product specification](product-specification.md). Plugin page mounting and UI safety are in [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#plugin-ui-bundles). Why React is used is in [ADR-004](ADR/ADR-004-React-Frontend.md).

The visual language comes from the Odysseus Chat page saved in `Demo/`. That folder is a reference. Penelope does not ship its scripts.

## Principles

1. One primary action on each screen.
2. Status is a word. Color supports the word.
3. Every long job shows a progress bar and a live log on that screen.
4. A failed install or update names the cause and offers buttons. It never offers a command.
5. Every external tool shows its version, and Update when a newer release exists.
6. A plugin with no custom pages still has a complete generic page.

## Shell

The demo's structure is a fixed sidebar and a full-height workspace. Penelope keeps that. The demo's full chat product stays in Odysseus. Penelope's Chat page only checks that a local model answers.

```text
+------------------+----------------------------------------------+
| Penelope         | Ollama is running                 [ Stop ]   |
|                  | Ollama 0.12.1 · Update available             |
| Home             |                                              |
| Chat             |                                              |
| Models           |  CPU 12%      RAM 9 / 32 GB     GPU 40%      |
| Add a model      |                                              |
| This computer    |  Models in memory                            |
| Network          |  General  ·  4.7 GB  ·  In memory   [ Stop ] |
| Tools            |                                              |
|                  |                                              |
| theme      Quit  |                                              |
+------------------+----------------------------------------------+
```

| Chrome | Rule |
| --- | --- |
| Sidebar | 240px, `--panel`, 1px `--border` on the right. Brand "Penelope" at the top. |
| Nav row | 8px padding, 4px radius. Active row: accent mixed about 12% into the panel. |
| Header | Status sentence on the left. The service Start or Stop on the right. |
| Workspace | `--bg`, content column max 960px, 24px page padding. |
| Radius | 4px rows, 6px buttons and fields, 8px cards and menus. |
| Space | 4, 8, 12, 16, 24. |
| Hover | Foreground mixed about 6% into the surface. |
| Focus | 2px `--accent` outline, 2px offset. |
| Scrollbar | Thin, thumb `--accent`, track `--panel`. |
| Motion | 150ms. Turned off when the system asks for reduced motion. |
| Primary button | Fill `--accent`, near-black label, weight 600. |
| Secondary button | Transparent, 1px `--border`, label `--fg`. |
| Destructive | Text `--color-error`, no fill. Confirm before removing a model. |

Home, Chat, This computer, Tools, and Diagnostics belong to the shell. Models, Add a model, and Network are sidebar entries from the Ollama plugin manifest. A later plugin adds its own entries the same way. Quit sits at the bottom of the sidebar, next to the theme control.

## Screens

| Screen | Primary action | What the person sees |
| --- | --- | --- |
| Welcome | Continue | One sentence: Penelope installs and runs local models on this computer. |
| Install | Install Ollama | What will happen, then a progress bar and a live log. |
| Home | The one next step | Status, version, three meters, models currently in memory. The button is Install, Start, Update, or Add a model, depending on state. |
| Chat | Send | The active model, Chat or Agent, and the current tab's conversation. Agent can search the web. |
| Models | Start or Stop on a row | Name, size, On disk or In memory. Empty state asks them to add a model. |
| Add a model | Download on one card | A few recommended cards. Other computers see the same catalog filtered by this computer's memory. A text field accepts another tag. |
| This computer | None | CPU, RAM, each GPU, VRAM, disk free on the models volume. |
| Network | The listen toggle | "Only this computer" or "Other devices on my network", the address, and the warning. |
| Tools | Open a tool | One card per discovered plugin. A plugin without custom pages opens the generic page: version, Install, Update, Start, Stop. |
| Diagnostics | None | Recent diagnostic log lines and recent events, for troubleshooting. Added with the infrastructure refactor. |
| Job | Cancel, while the download is still in progress | Progress bar, percent when known, and the log underneath. |
| Failure | The first fix | Summary, cause, buttons, log scrolled to the error. |

## First visit

1. Welcome.
2. Install, or "Ollama is already installed" and continue.
3. Optional pick of a starter model. Skip is allowed. The home page then carries Add a model as its primary action.

Three steps is the ceiling. There is no account step.

## Copy

| Situation | Heading | Button |
| --- | --- | --- |
| Ollama missing | Ollama is not installed | Install Ollama |
| Installed, stopped | Ollama is stopped | Start Ollama |
| Running, no models | Ollama is running | Add a model |
| Download in progress | Downloading {name} | Cancel |
| Model on disk | On disk | Start |
| Model in memory | In memory | Stop |
| Remove | Remove this model from the computer? | Remove |
| LAN off | Only this computer can use Ollama | Other devices on my network |
| LAN on | Other devices on your network can use Ollama. There is no password. | Only this computer |
| Update available | Ollama {installed} is installed. {latest} is available. | Update Ollama |
| Updating | Updating Ollama | Cancel, until the installer itself starts |
| Install failed | Ollama didn't install | Try again |
| Permission cancelled | Windows asked for permission and the request was cancelled. | Try again |
| Plugin page crashed (planned) | This tool's page stopped working. | Try again, then the generic page below |

Helper line under a model's Stop: "This frees memory. It does not delete the model."

If a download is large, the card states the size and the free space before Download enables. If VRAM is below the catalog hint, the card says the model can run in system memory and will be slower.

A failure card is the shape of every install and update error. "There is not enough memory to start this model. Stop another model, or choose a smaller one." is the shape of an action error. A stack trace is never the only message, and a command line is never the fix.

Raw job states (`pending`, `running`, ...) are never shown as copy. The job panel shows the job's `message` line and, at the end, "Finished.", "Cancelled.", or the failure card.

## Accessibility

Every action is reachable from the keyboard. The status line is text, not only a colored dot. Meters include the percent as text. The accent focus ring is always visible.

## Design tokens

Taken from `Demo/Odysseus Chat_files/style.css` and the inline theme on `Demo/Odysseus Chat.html`. Implemented in `web/src/design/tokens.css`.

| Token | Dark (default) | Light |
| --- | --- | --- |
| `--bg` | `#282c34` | `#f5f5f5` |
| `--fg` | `#9cdef2` | `#2b2b2b` |
| `--panel` | `#111111` | `#ffffff` |
| `--border` | `#355a66` | `#bbbbbb` |
| `--accent` | `#e06c75` | `#e06c75` |
| `--green` | `#50fa7b` | `#2e7d32` |
| `--warn` | `#f0ad4e` | `#f0ad4e` |
| `--color-error` | `#ff4444` | `#c0392b` |
| `--color-subheader` | `#6b8a94` | `#4a6168` |
| `--sidebar-bg` | `#111111` | `#ffffff` |
| Font | Fira Code | Fira Code |

Fira Code is the demo's voice. Bundle the SIL Open Font License files under `web/src/design/fonts/`. They are not in the Demo folder today. Root size is 14px. Instructions use `--fg`. Secondary labels may use `--color-subheader`. `#888`, which the demo uses as `--color-muted`, is too faint for text a person must read, so Penelope does not use it for copy.

Light theme variables ship in the first stylesheet. A sidebar control switches them. Dark is the default, matching the reference.

Plugin pages consume these tokens. They do not restyle the shell.

## Brand mark

The mark is the dark rounded tile and the white P in `branding/penelope.svg`. That file is the only logo source, and it does not contain the status dot. `scripts/BrandExport` draws it into `src/Penelope.Host/Assets/penelope.ico` and `web/public/favicon.ico`. The program icon is that file, so the taskbar, shortcuts, Add/Remove Programs, the setup wizard, and the uninstaller show the clean mark. The browser tab uses `favicon.svg`. The sidebar, welcome flow, loading state, empty states, and the locked page use the same clean mark. The colored dot is added only where a live state is shown: the dashboard header, a tool card, a tool page, and the tray icon. Its position is `data-status-cx`, `data-status-cy`, and `data-status-r` on the source SVG. Green means running, yellow means working, and red means stopped.

The status dot describes whether a tool is running, working, or stopped, not how busy the graphics card is. The tray icon is the only Windows icon that changes color.
