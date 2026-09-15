import { describe, expect, it } from "vitest";

import { DownloaderResultReadyTriggerUI as trigger } from "../Triggers/DownloaderResultReady";
import { DownloaderFetchTorrentResultUI as fetchResult } from "../Activities/DownloaderFetchTorrentResult";
import { DownloaderPrepareResourceUI as prepareResource } from "../Activities/DownloaderPrepareResource";
import { AcquisitionFetchExHentaiUI as fetchExHentai } from "../Activities/AcquisitionFetchExHentai";
import { AcquisitionFetchResultTorrentUI as fetchResultTorrent } from "../Activities/AcquisitionFetchResultTorrent";

import cn from "@/locales/cn/pages/workflow.json";
import en from "@/locales/en/pages/workflow.json";

describe("download result workflow configuration", () => {
  it("preserves result kind filters and rejects unsupported values", () => {
    expect(trigger.parseFilter(null)).toEqual({ kinds: [] });
    expect(trigger.parseFilter("invalid")).toEqual({ kinds: [] });
    expect(trigger.parseFilter("null")).toEqual({ kinds: [] });
    expect(trigger.parseFilter('{"kinds":"1"}')).toEqual({ kinds: [] });
    expect(trigger.serializeFilter(trigger.defaultFilter())).toBeNull();
    const filter = trigger.parseFilter('{"kinds":[1,2]}');

    expect(trigger.isValid(filter)).toBe(true);
    expect(trigger.parseFilter(trigger.serializeFilter(filter))).toEqual(filter);
    expect(trigger.isValid({ kinds: [3] })).toBe(false);
    expect(trigger.resolveOutputItemType(filter)).toBe("item.downloader.result");
  });

  it.each([fetchResult, fetchResultTorrent])("keeps a bounded timeout on $kind", (activity) => {
    expect(activity.parseConfig("{}")).toEqual({ timeoutMinutes: 240 });
    expect(activity.parseConfig("invalid")).toEqual({ timeoutMinutes: 240 });
    expect(activity.parseConfig('{"timeoutMinutes":60}')).toEqual({ timeoutMinutes: 60 });
    expect(activity.isValid({ timeoutMinutes: 1 })).toBe(true);
    expect(activity.isValid({ timeoutMinutes: 0 })).toBe(false);
    expect(activity.isValid({ timeoutMinutes: 43201 })).toBe(false);
  });

  it.each([prepareResource, fetchExHentai])(
    "has no opaque JSON configuration on $kind",
    (activity) => {
      expect(activity.parseConfig("{}")).toEqual({});
      expect(activity.serializeConfig(activity.defaultConfig())).toBe("{}");
    },
  );

  it("translates all download-result metadata in both languages", () => {
    const keys = [
      trigger.displayNameKey!,
      fetchResult.displayNameKey!,
      prepareResource.displayNameKey!,
      fetchExHentai.displayNameKey!,
      fetchResultTorrent.displayNameKey!,
      "workflow.trigger.downloaderResultReady.description",
      "workflow.activity.downloaderFetchTorrentResult.description",
      "workflow.activity.downloaderPrepareResource.description",
      "workflow.acquisition.fetchExHentai.description",
      "workflow.acquisition.fetchResultTorrent.description",
      "workflow.itemType.item.downloader.result.displayName",
      "workflow.recipe.downloadTorrentContents.name",
      "workflow.recipe.downloadTorrentContents.description",
    ];

    for (const dictionary of [cn, en]) {
      for (const key of keys) expect((dictionary as Record<string, string>)[key]).toBeTruthy();
    }
  });
});
