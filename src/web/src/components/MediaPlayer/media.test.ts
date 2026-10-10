import { describe, expect, it } from "vitest";
import { getEntryMediaType, selectInitialMediaIndex } from "./media";
import { IwFsType, MediaType } from "@/sdk/constants";
import en from "@/locales/en/components/mediaPlayer.json";
import cn from "@/locales/cn/components/mediaPlayer.json";
const entry = (path: string) => ({
  path,
  type: IwFsType.Unknown,
  name: path.split("/").pop()!,
  passwordsForDecompressing: [],
});
describe("browser player file selection", () => {
  it("honors a located playable file before alphabetically earlier subtitles, NFO and covers", () => {
    const files = [
      entry("/film/a.ass"),
      entry("/film/a.nfo"),
      entry("/film/cover.jpg"),
      entry("/film/sample.mkv"),
      entry("/film/main.mkv"),
    ];
    expect(selectInitialMediaIndex(files, ["/film/main.mkv"])).toBe(4);
    expect(selectInitialMediaIndex(files)).toBe(3);
    expect(selectInitialMediaIndex(files, ["/film/a.nfo"])).toBe(1);
  });
  it("matches Windows path separators/case and does not conflate case-sensitive Linux paths", () => {
    expect(
      selectInitialMediaIndex(
        [entry("C:\\Film\\side.nfo"), entry("C:\\Film\\Main.mkv")],
        ["c:/film/main.MKV"],
      ),
    ).toBe(1);
    expect(
      selectInitialMediaIndex(
        [entry("/Film/notes.nfo"), entry("/Film/main.mkv")],
        ["/film/notes.nfo"],
      ),
    ).toBe(1);
  });
  it("recognizes uppercase subtitle and NFO files as text", () => {
    for (const extension of ["ASS", "SSA", "NFO", "SRT", "VTT"])
      expect(getEntryMediaType(entry(`/film/info.${extension}`))).toBe(MediaType.Text);
  });
  it("supplies the same nonempty translated player/window/text keys in both languages", () => {
    expect(Object.keys(cn).sort()).toEqual(Object.keys(en).sort());
    for (const [key, text] of Object.entries(cn)) {
      expect(key).toMatch(/^mediaPlayer\./);
      expect(text).not.toBe(key);
      expect(text.trim()).not.toBe("");
    }
  });
});
