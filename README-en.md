[中文](/README.md) | [English](/README-en.md)

# Bakabase

Bakabase helps you manage your local media — anime, comics, audio dramas, doujinshi, movies, image collections — organized by properties you define, with automatic metadata fetching and a comprehensive toolkit for resource and file operations.

![Bakabase](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/hero.png)

## Table of Contents

- [Installation and Updates](#installation-and-updates)
- [Features](#features)
  - [Data Cards](#data-cards)
  - [Customizable Resource Detail Layout](#customizable-resource-detail-layout)
  - [Steam, DLsite, ExHentai Integration](#steam-dlsite-exhentai-integration)
  - [Tampermonkey Scripts](#tampermonkey-scripts)
  - [AI Features](#ai-features)
  - [Resource Health Score](#resource-health-score)
  - [Other Improvements](#other-improvements)
  - [Resource Management](#resource-management)
  - [Metadata Enhancement](#metadata-enhancement)
  - [Media Library Configuration](#media-library-configuration)
  - [Built-in Player](#built-in-player)
  - [Tools](#tools)
  - [Data Management](#data-management)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [Star History](#star-history)

## Installation and Updates

- [Stable downloads](https://github.com/anobaka/Bakabase/releases/latest): recommended for everyday use.
- [Beta downloads](https://github.com/anobaka/Bakabase/releases): choose a `2.4.0-beta` release to try the [features in development](#features-in-development) below. Beta features and data structures may change; keep a complete data backup before upgrading.

### Desktop App

The desktop app includes its own server and window, and can also manage other paired servers. Open the release page and use its download links or **Assets** section to choose your platform:

| Platform | Recommended installer | Alternative |
| --- | --- | --- |
| Windows x64 | `Bakabase-<version>-win-x64-Setup.exe` | `…-Portable.zip`: no installation; update by replacing program files manually |
| macOS Apple Silicon (M series) | `Bakabase-<version>-osx-arm64-Setup.pkg` | — |
| macOS Intel | `Bakabase-<version>-osx-x64-Setup.pkg` | — |
| Linux / NAS | Use the Docker server below | — |

Run the installer and follow its instructions. `.nupkg` update packages and GitHub **Source code** archives are not installers. On its first launch, the 2.4 beta opens Setup: choose a data directory and initialize empty data or import from another stopped Bakabase installation. External media files are not copied with application data; see [Setup and data import](docs/setup.md). Existing users keep their data directory when upgrading.

**Updating an installed desktop app:**

1. Open **System → Configuration → System information → Current version** and click **Check for updates**.
2. Enable **Receive beta updates** to get 2.4 betas. Disabling it checks only stable releases; it does not downgrade your existing data to an older version.
3. Follow the page's prompts to download an available update. If downloading has already started automatically, wait until it is ready, then select **Restart to update**. Updating replaces program files while retaining your data directory, workspaces, device pairings and path mappings.

Portable and source builds do not support installer updates. Exit normally, replace the program manually, and keep the original data directory. If an installed app fails to update, you can also download a newer installer for the same platform from the release page.

**Upgrading from before 2.3:** Version 2.3 introduced a new installer that the old updater cannot reach. Download an installer and install the new app once. First back up your complete old data and exit the old app. In the 2.4 beta, choose **Import existing data** during initial Setup, or use **System → Configuration → Workspaces → Current workspace data directory → Import existing data**. For the older 2.3 stable release, follow its release-specific migration instructions. Keep data outside the program installation directory, and never let two instances write to the same data directory.

### Docker Server

The Docker edition includes a server and web interface for NAS devices or always-on hosts. The instructions below apply to the 2.4 beta, which provides Linux AMD64 and ARM64 images; Apple Silicon can run ARM64 directly. Choose an explicit published version from [Docker Hub](https://hub.docker.com/r/anobaka/bakabase/tags); **there is no `latest` tag**. Check the release notes for older tags' capabilities and architectures.

To use the repository's Compose helpers, install Docker Compose, Git, Bash and Python 3 (Windows users can run them in WSL):

```sh
git clone https://github.com/anobaka/Bakabase.git
cd Bakabase
cp docker/.env.example docker/.env
```

**Configure storage and mounts before starting.** A container cannot automatically access every folder on your computer or NAS:

| Purpose | Host location (`source`) | Path inside Bakabase (`target`) |
| --- | --- | --- |
| Application data: databases, settings, covers and device connections; required | `BAKABASE_DATA_DIR` in `.env` | `/data` |
| Media files; optional | `/absolute/path/to/media` | `/media` |
| Downloads; optional | `/absolute/path/to/downloads` | `/downloads` |

Media, download and import directories need separate mounts; mounting `/data` alone does not make them accessible. Mount NAS shares on the host first and confirm they work.

Edit `docker/.env`, replacing the version and path below. Create your chosen data directory first, and select a version tag already published on Docker Hub:

```dotenv
BAKABASE_IMAGE=anobaka/bakabase:<version>
BAKABASE_DATA_DIR=/absolute/path/to/bakabase-data
```

Create `docker/compose.local.yaml` to mount the folders you want to use, for example:

```yaml
services:
  server:
    volumes:
      - type: bind
        source: /absolute/path/to/media
        target: /media
        bind:
          create_host_path: false
      - type: bind
        source: /absolute/path/to/downloads
        target: /downloads
        bind:
          create_host_path: false
```

`source` is an **existing absolute host path**; `target` is the container path you select inside Bakabase. This example uses `/media` for libraries and `/downloads` for downloads. Replace the placeholder paths and remove unwanted mounts; see the [mount examples](docker/compose.local.example.yaml) for more options. To import old data, separately mount a copy from a stopped installation read-only at `/import`; do not share a writable `/data` between instances.

Start the server:

```sh
./docker/image.sh pull
./docker/image.sh up -d --no-build
./docker/image.sh logs -f server
```

On the first launch, open the full link after `Server setup:` in the logs, preserving its token, and complete Setup. Then visit `http://localhost:34567`. Access is local-only by default; see the [Docker installation guide](docs/docker-installation.md) for LAN access, pairing and import instructions. After adding or changing a mount, run `./docker/image.sh up -d --no-build --force-recreate` to recreate the container; `restart` alone does not apply mount changes.

**Updating:** The web interface's update check does not replace a Docker image. Stop the service normally, then back up the complete host application data directory to a separate location. Do not copy live SQLite files while the service is running:

```sh
./docker/image.sh stop server
```

After backing up, preserve the other `.env` settings, all mounts and the data directory. Change only `BAKABASE_IMAGE` to the target version, then run:

```sh
./docker/image.sh pull
./docker/image.sh up -d --no-build --force-recreate
```

Check the displayed version, libraries and directory access afterward. Do not use `down -v` to clean up the old service: it deletes data in named volumes.

**Testing source changes:** In the same checkout, set `BAKABASE_IMAGE` in `.env` to `bakabase:local`, run `git submodule update --init --recursive`, then `./docker/source.sh` to build and start inside Docker. To return to an official image, change `BAKABASE_IMAGE` back to a published tag and run the image-update commands above. Both modes share project `bakabase`, service `server` and the same local configuration, so they can replace and upgrade each other without starting a second stack. Keep data versions compatible; reverting only the image does not downgrade a database. Restore a complete backup for the matching version when rolling back.

## Features

### Data Cards

Abstract cross-resource entities like actors, authors, brands, IPs, and characters into cards. Each card has its own properties (name, nationality, notable works…), and is automatically associated with resources through the property values you specify.

- **Auto-association**: Pick a set of "match properties" on a card type. Any resource whose values on those properties match a card's values gets associated automatically. Two modes are supported: match-any and match-all.
- **Auto-create / update**: When external data arrives (via enhancers, sync, etc.), the system looks up existing cards by the "identity properties" you defined — matches are updated, unmatched ones can be newly created.
- **Bulk-generate from existing resources**: Scan property values already used by resources and one-click create the corresponding set of cards.
- **Visible in resource detail**: The resource detail panel lists all cards associated with that resource.
- **Search resources from a card**: Click the search button on a card to open a new search tab on the resource page, pre-filtered by the card's match properties.

<details open>
<summary>Screenshots</summary>

![Data Card 0](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-0.png)
![Data Card 1](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-1.png)
![Data Card 2](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-2.png)
![Data Card 3](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/data-card-3.png)

</details>

### Customizable Resource Detail Layout

Rearrange the resource detail panel to your taste — choose which sections appear, where they sit, and how big they are.

- **11 built-in sections**: Cover, Rating, Actions, Basic Info, Hierarchy, Introduction, Played At, Properties, Related Data Cards, Media Libraries, and Profiles — each can be shown or hidden individually.
- **Grid-based drag layout**: Drag to reorder on a 4 / 6 / 8 / 12-column grid; resize column- and row-span by dragging section edges; configurable gap.
- **Modal sizing**: Adjust the detail modal's width and height between 50% and 95% of the viewport, with a live preview alongside the layout editor.
- **Hide without losing config**: Hidden sections keep their original position and size, ready to be restored exactly where they were; hover an empty cell to quickly re-add a previously hidden section.
- **Dynamic-height hints**: Sections whose height is content-driven are visually flagged in the designer; the runtime stacks them column-by-column as a waterfall so nothing overlaps.
- **One-click reset**: Revert to the default layout anytime; settings apply globally to every resource detail modal.

<details open>
<summary>Screenshots</summary>

![Layout Designer 1](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/resource-detail-layout-1.png)
![Layout Designer 2](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/resource-detail-layout-2.png)

</details>

### Steam, DLsite, ExHentai Integration

Browse and manage content from these platforms directly inside Bakabase.

<details open>
<summary>Screenshots</summary>

| Steam | DLsite | ExHentai |
|:---:|:---:|:---:|
| ![Steam](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/platform-steam.png) | ![DLsite](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/platform-dlsite.png) | ![ExHentai](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/platform-exhentai.png) |

</details>

### Tampermonkey Scripts

A new userscript that lets you create download tasks and grab download URLs in one click from external sites. Some sites also support automatic cookie import after logging in.

<details open>
<summary>Screenshots</summary>

| ExHentai | DLsite |
|:---:|:---:|
| ![ExHentai](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/tampermonkey-exhentai.png) | ![DLsite](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/tampermonkey-dlsite.png) |

</details>

### AI Features

- **AI Enhancer**: Let AI analyze resources and extract or fill in properties automatically.
- **AI Translation**: Translate property values to your target language after enhancement.
- **AI File Analysis**: Multiple modes including structure analysis and similarity grouping.
- **AI Agent**: Chat with the agent to run simple tasks.

<details open>
<summary>Screenshots</summary>

![AI Config](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ai-config.png)
![AI File Analysis](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ai-fs-analysis.jpg)
![AI Agent](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ai-agent.png)

</details>

### Resource Health Score

Score your resources by configurable rules so you can spot at a glance which ones are missing covers, missing subtitles, have abnormal file sizes, or have lost their root directory.

- **Scoring profiles**: Each profile pairs a *membership filter* (which resources participate) with a set of *scoring rules* (a delta is applied to the base score when a rule matches). When multiple profiles match the same resource, the final health score is the lowest score among the highest-priority matching profiles.
- **File predicates**: Built-in conditions for file-type count / size, file-name regex count, file size out of range, has cover image, root path exists, etc.
- **Property predicates**: Reuse the existing resource filter — match on any property value.
- **Resource card badge**: ≥70 green, 40–70 yellow, &lt;40 red; can be hidden via display settings.
- **Diagnosis modal**: Click the badge to see exactly which rules fired and how much each contributed.
- **Background scoring**: Runs as a BTask with cancel / retry / progress; profile edits clear the relevant cache and trigger a re-score automatically.

<details open>
<summary>Screenshots</summary>

| Rules Configuration | Resource List & Score Diagnosis |
|:---:|:---:|
| ![Rules Configuration](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/health-score-rules.png) | ![Score Diagnosis](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/health-score-diagnosis.png) |

</details>

### Other Improvements

- **Unified Enhancer Property Config**: Manage all enhancer-to-property mappings in one place; see at a glance where each property's data comes from.
- **Quick Property Setting via Context Menu**: Set or batch-set property values from the right-click menu, with configurable presets.
- **UI Customization**: Adjust resource cover size and other display details.
- **Cross-platform**: macOS is now supported; Linux is still in development.

<details open>
<summary>Screenshots</summary>

![Enhancer Property Config](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/enhancer-property-config.png)

| Configure Presets | Context Menu |
|:---:|:---:|
| ![Config](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-config.png) | ![Context Menu](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-context-menu.png) |

![UI Customization](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ui-customization.png)

</details>

### Resource Management

A flexible property system covering anime, comics, audio dramas, doujinshi, movies, image collections, and more. Search and filter resources by their properties.

<details open>
<summary>Screenshots</summary>

![Resource List](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-list-v22.png)
![Resource List](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-list.jpg)
![Resource Filter](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-filter.jpg)
![Resource Detail](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-detail.jpg)
![Multi-value Properties](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-multiple-scope-property-values.jpg)

</details>

### Metadata Enhancement

Fetch covers, tags, and other metadata from sources like ExHentai, Bangumi, DLsite, and TMDB.

<details open>
<summary>Screenshots</summary>

![Enhancer List](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/4-enhancer-list.jpg)
![Enhancer Detail](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/4-enhancer-detail.jpg)
![Enhancement Records](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/4-enhancer-record.jpg)

</details>

### Media Library Configuration

A template-based media library system. Customize file-locating rules, property extraction rules, and display names per template.

<details open>
<summary>Screenshots</summary>

![Media Library List](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/2-media-library-list.jpg)
![Player Settings](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/2-media-library-player.jpg)
![Template Detail](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-detail-1.jpg)
![Template Detail](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-detail-2.jpg)
![Template List](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-list.jpg)
![Property Locator](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-property-locator.jpg)
![Display Name](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-resource-display-name.jpg)
![Resource Locator](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/3-media-library-template-resource-locator.jpg)

</details>

### Built-in Player

Preview and play images, videos, and other media directly.

<details>
<summary>Screenshots (NSFW)</summary>

![Built-in Player](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/1-resource-media-player.png)

</details>

### Tools

Downloaders (Bilibili, ExHentai, Pixiv, etc.), file mover, file renamer, and other utilities.

<details open>
<summary>Screenshots</summary>

![Bilibili Downloader](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader-bilibili.jpg)
![ExHentai Downloader](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader-exhentai.jpg)
![Pixiv Downloader](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader-pixiv.jpg)
![Downloader](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-downloader.jpg)
![File Mover](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-file-mover.jpg)
![File Renamer](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/6-tool-file-name-modifier.jpg)

</details>

### Data Management

Aliases, cache, custom properties, property conversion, extensions, sync options, and more.

<details open>
<summary>Screenshots</summary>

![Alias Management](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-alias.jpg)
![Cache Management](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-cache.jpg)
![Property Conversion](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-custom-property-conversion.jpg)
![Custom Properties](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-custom-property-detail.jpg)
![Property List](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-custom-property.jpg)
![Extensions](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-extensions-group.jpg)
![Sync Options](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/5-data-synchronization-options.jpg)

</details>

## Documentation

Visit the [project homepage](https://bakabase.anobaka.com/) for more.

## Contributing

Contributions are welcome — see the [development docs](https://bakabase.anobaka.com/#/dev/dev).

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=anobaka/Bakabase&type=Date)](https://www.star-history.com/#anobaka/Bakabase&Date)
