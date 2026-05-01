[中文](/README.md) | [English](/README-en.md)

# Bakabase

Bakabase helps you manage your local media — anime, comics, audio dramas, doujinshi, movies, image collections — organized by properties you define, with automatic metadata fetching and a comprehensive toolkit for resource and file operations.

![Bakabase](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/hero.png)

## Table of Contents

- [Download](#download)
- [Beta Features](#beta-features)
  - [Data Cards](#data-cards)
  - [Customizable Resource Detail Layout](#customizable-resource-detail-layout)
  - [Steam, DLsite, ExHentai Integration](#steam-dlsite-exhentai-integration)
  - [Tampermonkey Scripts](#tampermonkey-scripts)
  - [AI Features](#ai-features)
  - [Resource Health Score](#resource-health-score)
  - [Other Improvements](#other-improvements)
- [Stable Features](#stable-features)
  - [Resource Management](#resource-management)
  - [Metadata Enhancement](#metadata-enhancement)
  - [Media Library Configuration](#media-library-configuration)
  - [Built-in Player](#built-in-player)
  - [Tools](#tools)
  - [Data Management](#data-management)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [Star History](#star-history)

## Download

- [Stable](https://github.com/anobaka/Bakabase/releases/latest)
- [Beta](https://github.com/anobaka/Bakabase/releases) — includes new features that aren't fully stable yet

> ## ✅ AppData loss risk fully resolved (2.3.0-beta.75)
>
> Starting with `2.3.0-beta.75`, the Windows default AppData directory moved from `%LocalAppData%\Bakabase` (which coincided with the Velopack install root) to `%LocalAppData%\Bakabase.AppData`. **Upgrades, uninstalls, and installer "Repair" no longer touch user data.** **Most users need to do nothing.**
>
> **Edge case**: if you ran any of `2.3.0-beta.69` ~ `2.3.0-beta.74` and **did not** customise the app data path in Settings, after upgrading to `≥ 2.3.0-beta.75` the app will appear "freshly installed" — your data is intact but no longer auto-loaded. To recover: type `%LocalAppData%\Bakabase` into the Windows Explorer address bar, press Enter, copy the expanded full path from the address bar (looks like `C:\Users\<you>\AppData\Local\Bakabase`), then paste it into Bakabase **Settings → System Information → App Data Path → Modify** and restart.
>
> Versions older than `2.3.0-beta.69` still carry the original in-app upgrade data loss risk; back up before upgrading per [#1070](https://github.com/anobaka/Bakabase/issues/1070). macOS and Linux are unaffected by either issue.

## Beta Features

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
- **macOS Support**: Available in beta; Linux is still in development.

<details open>
<summary>Screenshots</summary>

![Enhancer Property Config](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/enhancer-property-config.png)

| Configure Presets | Context Menu |
|:---:|:---:|
| ![Config](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-config.png) | ![Context Menu](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/quick-set-context-menu.png) |

![UI Customization](https://github.com/anobaka/Bakabase.Docs/blob/main/imgs/v23/ui-customization.png)

</details>

## Stable Features

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
