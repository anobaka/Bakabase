# Bilibili Favorites Downloader

Downloads a user's Bilibili favorites folder through the API (no external tool). Every
protocol decision is a pure, fixture-tested rule; IO code only chains them.

## Where things live

| Concern | Location |
|---|---|
| API client (cookie, 1 req/s, the single `GetApiAsync` choke point) | `src/modules/Bakabase.Modules.ThirdParty/ThirdParties/Bilibili/BilibiliClient.cs` |
| Wire models (Newtonsoft, tolerant, nullable collections) | `.../Bilibili/Models/` |
| URLs (incl. the frozen naming URL) | `.../Bilibili/Models/Constants/BiliBiliApiUrls.cs` |
| Decision tables and helpers (pure) | `.../Bilibili/Protocol/` |
| CDN transfer, page orchestration | `.../Bilibili/Download/` |
| Favorites loop, checkpoint, naming, notices | `src/legacy/.../Downloader/Components/Downloaders/Bilibili/BilibiliDownloader.cs` |
| Fixtures (scrubbed, hand-built) | `src/tests/Bakabase.Modules.ThirdParty.Tests/Fixtures/Bilibili/` |
| Rule and client tests | `src/tests/Bakabase.Modules.ThirdParty.Tests/Bilibili/` |
| Live contract test (`[Ignore]`, manual) | `.../Bilibili/BilibiliLiveContractTests.cs` |
| Downloader end to end (real DI, faked HTTP clients, stub merger) | `src/tests/Bakabase.Tests/Bilibili/` |
| The one wire fake (API, CDN, merger), linked into both test projects | `src/tests/Bakabase.Modules.ThirdParty.Tests/Bilibili/Shared/` |

## Error classes (`BilibiliApiCodes`)

| code | class | effect |
|---|---|---|
| non-empty `data.v_voucher` (any code); -352, -412, -509, -799, -401; HTTP 412; HTTP 403 from an **API** endpoint | RiskControl | `BilibiliTemporarilyUnavailableException` (transient) |
| -500, -503, -504, -8888, -112, -702; HTTP 408/429/5xx | ServiceBusy | same, transient |
| -101 | NotLoggedIn | `BilibiliNotLoggedInException` (fatal) |
| 62002, 62004, 62012, 87008, -404, -10403, -403 (view only), -400 (playurl on a PGC redirect only) | ContentState | the decision tables → a `BilibiliSkip` |
| anything else | Unknown | `BilibiliApiException` (fatal; the checkpoint does not advance) |

**A skip that depends on the account** (`BilibiliSkipReasons.DependsOnLogin`: supporter-only, preview,
member-only, PGC episode, access denied) asks myinfo first, however recently it was asked: an expired
login answers like an account without access, and must fail the task instead of being checkpointed past.

**Never add a catch-all skip.** A skip advances the checkpoint and a completed run records
`{firstId}-`, so an unrecognised code that became a skip would silently mark a whole library
done. Unknown means stop.

## Decision tables (`Protocol/`)

- **Favorites item** (`BilibiliFavoriteItemClassifier`, no request, the title is never read):
  `attr & 1` → InvalidItem; type 24 (id is an **ep_id**) / 12 (audio id) / 21 → unsupported;
  type 0 (field missing) → video; other types → UnsupportedItemType; video with `attr & 16` →
  InteractiveVideo; attr 2 and 4 are valid videos.
- **view** (`BilibiliArchiveRules.DecideView`): pages → proceed; empty pages + `forward` →
  follow once; 62002/62012/62004/87008/-10403 → skip; -404 → pagelist (listed = hidden/region,
  else deleted); -403 → pagelist fallback (attr-4 legacy archives).
- **Access gate** (`GateAccess`): `is_upower_exclusive && !is_upower_play` → supporter-only
  (preview or not). `rights.pay` is not gated. Evaluate it only after the existing-file check.
- **playurl** (`BilibiliPlayUrlRules`): 87008 → SupporterOnly; -404/-400 on a PGC redirect →
  PgcEpisodeNotSupported; -404 otherwise → Unavailable; -10403 → region (message has 地区) or
  PgcMemberOrPaid; code 0: DASH video → Dash; preview → skip; durl (every segment with a URL) →
  Durl; else NoStreams (fatal protocol error, never a skip).
- **Preview**: no usable DASH video, durl present, `timelength > 0` and
  `Σ durl.length < timelength × 0.99 − 50`. Never compare with view's duration, never look for 试看.
- **Streams** (`BilibiliStreamSelector`): best `dash.video[].id` (Dolby Vision 126 only when
  nothing else exists), then codec AVC(7) > HEVC(12) > AV1(13) by `codecid`, then bandwidth;
  audio FLAC (`dash.flac.audio`, an object) > Dolby (`dash.dolby.audio`, an array) > best AAC.
  Same stream after a refresh = same `IdentityKey` (durl: order **and** file name).
- **CDN URLs** (`BilibiliCdnUrls`): rank `upos-*` > `*.bilivideo.com` > other > non-default
  port / PCDN. A URL is abandoned only after it actually failed; never skip one because its
  `deadline` looks near by the local clock. An error status is judged by the status alone (408/429/5xx
  are retried even with an HTML page); a 2xx whose type is `text/*` or JSON/XML is a dead URL; an empty
  body is never a complete stream.
- **Captions** (`BilibiliCaptions`): `{name}.srt` = a human zh track (else the first human);
  other human languages `{name}.{lan}.srt`; without human tracks one AI track, primary only when
  the body's `lang` confirms its label, else `{name}.ai-{lang}.srt`. Danmaku XML is raw deflate
  (`BilibiliTextDecoder`); subtitle JSON is gzip.

## File-name compatibility (never break)

Existing libraries are recognised by `File.Exists` on the name built from the naming fields.
`QualityName` = `support_formats.MaxBy(quality).new_description` of the **legacy** answer
`BiliBiliApiUrls.LegacyNamingPlayUrl` (`fnval=16`, `qn=16`). **Never change that URL, the
`VideoQuality.Description` binding (`new_description`) or `BilibiliQualityNaming.LegacyQualityName`.**
Do not derive the name from the 4048 answer until the live parity test has proved it.

## Runs, checkpoint and retries (`BilibiliDownloader`)

- Every skip becomes a notice (`AbstractDownloader.AddNotice`); a completed task's `Message` is the
  notes block (summary line, `- ` lines, footer), shown by the UI as a warning row. Notes survive a failure
  or a stop when the next start resumes from the checkpoint (in memory only: an app restart loses them, the
  log keeps every skip). A failure the user can act on puts its own (localized) text on the first line.
- A definite skip advances the checkpoint. `Unavailable`/`CdnUnavailable` do not until a later item
  settles; 3 in a row stop the run (`BilibiliProtocolException`), and a run ending on them does not
  record `{firstId}-`, so the next run asks again.
- `{firstId}-` is written only when `has_more` is false or a finished range is reached; empty pages
  with `has_more` are normal. Paging past `ceil(media_count / 20) + 5` fails.
- Re-runs: risk control after 10 then 30 min (the wait is shown and refreshed every minute), anything
  else transient after 30 s / 2 min / 5 min. List pages fetched in one start are reused by its re-runs.
- ffmpeg is checked before any request; "installing" is transient, "not installed" fails.
- Work folders live in `{DownloadPath}/temp/{favoritesId}/{cid}` and survive re-runs (partials
  resume); folders untouched for 7 days are pruned at the start of a task.

## Secrets

- The API client carries the cookie; CDN, danmaku, subtitle and cover requests must not.
- Signed URLs carry the user's IP (`oi`), `mid` and signatures: logs, exception messages and
  task messages contain URLs only through `BilibiliCdnUrls.Redact`, never a response body
  (`BilibiliDiagnostics.RedactJson` for Debug diagnostics only). JSON errors are wrapped
  without their inner exception (its message quotes the value).
- Exception messages carry endpoint names (`view`, `playurl`, …) and codes.

## Fixtures

Hand-built from the research shapes; **never paste a raw response**. Hosts
`upos-sz-example.bilivideo.com`, `cn-example-ct-01-01.bilivideo.com`,
`xy0x0x0x0xy.mcdn.bilivideo.cn:8082`, `b-example.edge.mountaintoys.cn:4483`; query
`?deadline=1900000000&gen=playurlv3&os=upos&upsig=0…0&uparams=e,deadline&bw=1`; subtitles
`…?auth_key=1-0-0-0`; fake mids. `FixturesAreScrubbedTests` fails on `oi=`, `mid=`, IPv4
addresses, real `upsig`/`auth_key`, `trid`, `SESSDATA`, `bili_jct`, `buvid`.

## Before blaming Bilibili

Run `BilibiliLiveContractTests` (remove `[Ignore]` locally): it replays the evidence samples
(durl-only BV1nx411u79K, DASH + subtitles BV1GJ411x7h7, login-only av7903418, supporter
preview BV1HxXwYEEqt, 87008 BV1bS4cekEFy, deleted av959106569, region av70867620, PGC redirect
av710444604 p2) and the naming parity check. WBI signing / `dm_img_*` parameters, if ever
needed, go into `BilibiliClient.GetApiAsync` only.
