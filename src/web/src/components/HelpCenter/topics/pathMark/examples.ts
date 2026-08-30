import type { TreeLine, TreeMarkType } from "./DirectoryTree";

const node = (key: string) => `helpCenter.pathMark.node.${key}`;
const badge = (key: string) => `helpCenter.pathMark.badge.${key}`;

export type PathMarkAbility =
  | "layer"
  | "anyLevel"
  | "regex"
  | "dynamicProperty"
  | "dynamicMediaLibrary"
  | "filter"
  | "boundary"
  | "scope"
  | "priority"
  | "multiMark"
  | "schedule"
  | "identity";

export interface PathMarkExample {
  id: string;
  abilities: PathMarkAbility[];
  /**
   * Mark types configured in this example, in display order. The i-th entry's
   * description is `helpCenter.pathMark.examples.{id}.mark{i+1}`.
   */
  markTypes: TreeMarkType[];
  tree: TreeLine[];
}

/**
 * The example gallery ("recipes"). Each entry demonstrates one or two
 * capabilities using regular movie / anime / manga / music collections.
 */
export const pathMarkExamples: PathMarkExample[] = [
  {
    id: "movieBasic",
    abilities: ["layer"],
    markTypes: ["resource", "mediaLibrary"],
    tree: [
      {
        depth: 0,
        kind: "dir",
        nameKey: node("movies"),
        mark: "mediaLibrary",
        badgeKey: badge("libraryMovies"),
      },
      { depth: 1, kind: "dir", nameKey: node("interstellar"), mark: "resource" },
      { depth: 2, kind: "file", literal: "movie.mkv", muted: true },
      { depth: 1, kind: "dir", nameKey: node("inception"), mark: "resource" },
      { depth: 2, kind: "file", literal: "movie.mkv", muted: true },
    ],
  },
  {
    id: "movieGenre",
    abilities: ["anyLevel", "dynamicProperty"],
    markTypes: ["resource", "property"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("movies") },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("scifi"),
        mark: "property",
        badgeKey: badge("genreDynamic"),
      },
      { depth: 2, kind: "dir", nameKey: node("interstellar"), mark: "resource" },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("drama"),
        mark: "property",
        badgeKey: badge("genreDynamic"),
      },
      { depth: 2, kind: "dir", nameKey: node("shawshank"), mark: "resource" },
    ],
  },
  {
    id: "animeSeason",
    abilities: ["dynamicProperty", "layer"],
    markTypes: ["resource", "property"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("anime") },
      {
        depth: 1,
        kind: "dir",
        literal: "2024-04",
        mark: "property",
        badgeKey: badge("seasonDynamic"),
      },
      { depth: 2, kind: "dir", nameKey: node("frieren"), mark: "resource" },
      { depth: 3, kind: "file", nameKey: node("ep01"), muted: true },
      { depth: 3, kind: "file", nameKey: node("ep02"), muted: true },
    ],
  },
  {
    id: "mangaAuthor",
    abilities: ["regex", "dynamicProperty"],
    markTypes: ["resource", "property"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("manga") },
      { depth: 1, kind: "dir", nameKey: node("onePiece"), mark: "resource" },
      { depth: 1, kind: "dir", nameKey: node("slamDunk"), mark: "resource" },
    ],
  },
  {
    id: "musicLibrary",
    abilities: ["anyLevel", "dynamicProperty", "filter"],
    markTypes: ["resource", "property"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("music") },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("hisaishi"),
        mark: "property",
        badgeKey: badge("artistDynamic"),
      },
      { depth: 2, kind: "dir", nameKey: node("laputaOst"), mark: "resource" },
      { depth: 3, kind: "file", literal: "01.flac", muted: true },
      { depth: 3, kind: "file", literal: "02.flac", muted: true },
    ],
  },
  {
    id: "autoLibraries",
    abilities: ["dynamicMediaLibrary"],
    markTypes: ["mediaLibrary", "resource"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("collections") },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("movies"),
        mark: "mediaLibrary",
        badgeKey: badge("libraryAuto"),
      },
      { depth: 2, kind: "dir", literal: "…", muted: true },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("anime"),
        mark: "mediaLibrary",
        badgeKey: badge("libraryAuto"),
      },
      { depth: 2, kind: "dir", literal: "…", muted: true },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("manga"),
        mark: "mediaLibrary",
        badgeKey: badge("libraryAuto"),
      },
    ],
  },
  {
    id: "multiMarks",
    abilities: ["multiMark", "priority"],
    markTypes: ["mediaLibrary", "property", "property", "resource"],
    tree: [
      {
        depth: 0,
        kind: "dir",
        nameKey: node("anime"),
        mark: "mediaLibrary",
        badgeKey: badge("libraryAnime"),
      },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("ongoing"),
        mark: "property",
        badgeKey: badge("statusOngoing"),
      },
      {
        depth: 2,
        kind: "dir",
        literal: "2024-04",
        mark: "property",
        badgeKey: badge("seasonDynamic"),
      },
      { depth: 3, kind: "dir", nameKey: node("frieren"), mark: "resource" },
    ],
  },
  {
    id: "extensionFilter",
    abilities: ["filter"],
    markTypes: ["resource"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("movies") },
      { depth: 1, kind: "dir", nameKey: node("interstellar") },
      { depth: 2, kind: "file", literal: "movie.mkv", mark: "resource" },
      { depth: 2, kind: "file", literal: "movie.srt", muted: true },
      { depth: 2, kind: "file", literal: "poster.jpg", muted: true },
      { depth: 2, kind: "file", literal: "movie.nfo", muted: true },
    ],
  },
  {
    id: "resourceBoundary",
    abilities: ["boundary"],
    markTypes: ["resource"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("movies") },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("avatarBd"),
        mark: "resource",
        badgeKey: badge("boundary"),
      },
      { depth: 2, kind: "dir", literal: "BDMV", muted: true },
      { depth: 3, kind: "dir", literal: "STREAM", muted: true },
      { depth: 4, kind: "file", literal: "00001.m2ts", muted: true },
    ],
  },
  {
    id: "scopeTag",
    abilities: ["scope"],
    markTypes: ["property"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("manga") },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("completed"),
        mark: "property",
        badgeKey: badge("statusCompleted"),
      },
      { depth: 2, kind: "dir", nameKey: node("slamDunk"), mark: "resource" },
      { depth: 2, kind: "dir", nameKey: node("dragonBall"), mark: "resource" },
    ],
  },
  {
    id: "scheduledSync",
    abilities: ["schedule"],
    markTypes: ["resource"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("downloads") },
      { depth: 1, kind: "dir", nameKey: node("frieren"), mark: "resource" },
      {
        depth: 1,
        kind: "dir",
        nameKey: node("newDownload"),
        mark: "resource",
        badgeKey: badge("autoAdded"),
      },
    ],
  },
  {
    id: "keepIdentity",
    abilities: ["identity"],
    markTypes: ["resource"],
    tree: [
      { depth: 0, kind: "dir", nameKey: node("movies") },
      { depth: 1, kind: "dir", nameKey: node("interstellar"), mark: "resource" },
      { depth: 2, kind: "file", literal: "movie.mkv", muted: true },
      { depth: 2, kind: "file", literal: "bakabase.json", badgeKey: badge("identityFile") },
    ],
  },
];
