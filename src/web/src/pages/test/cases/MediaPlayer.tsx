"use client";
import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaPlayer from "@/components/MediaPlayer";
import { selectInitialMediaIndex } from "@/components/MediaPlayer/media";
import { IwFsType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";

/** Development fixture: a chosen temporary directory, without recording play history. */
const MediaPlayerTest = () => {
  const { t } = useTranslation();
  const params = new URLSearchParams(window.location.search);
  const [directory, setDirectory] = useState(params.get("mediaPlayerRoot") || "");
  const paths = params.get("mediaPlayerFiles")?.split("|") ?? [];
  const toEntry = (path: string): BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry => ({
    path,
    name: path.split(/[/\\]/).pop() || path,
    type: IwFsType.Unknown,
    passwordsForDecompressing: [],
  });
  const [entries, setEntries] = useState(paths.map(toEntry));
  const [loading, setLoading] = useState(false);
  const { createWindow } = useBakabaseContext();
  return (
    <div className="flex h-full min-h-0 flex-col gap-3 p-4">
      <div className="flex items-end gap-3">
        <Input label={t("mediaPlayer.directory")} value={directory} onValueChange={setDirectory} />
        <Button
          isLoading={loading}
          isDisabled={!directory}
          onPress={async () => {
            setLoading(true);
            try {
              const result = await BApi.file.getAllFiles(
                { path: directory },
                { signal: AbortSignal.timeout(15_000) },
              );
              if (result.code || !result.data)
                toast.danger(result.message || t("mediaPlayer.filesFailed"));
              else setEntries(result.data.map(toEntry));
            } catch {
              toast.danger(t("mediaPlayer.filesFailed"));
            } finally {
              setLoading(false);
            }
          }}
        >
          {t("mediaPlayer.loadFiles")}
        </Button>
        <Button
          isDisabled={!entries.length}
          onPress={() =>
            createWindow(
              MediaPlayer,
              { entries, defaultActiveIndex: selectInitialMediaIndex(entries) },
              { title: t("mediaPlayer.title"), persistent: true },
            )
          }
        >
          {t("mediaPlayer.openWindow")}
        </Button>
      </div>
      <div
        className="overflow-hidden rounded-xl border border-default-200"
        style={{ height: "min(70vh, 760px)", minHeight: 320 }}
      >
        <MediaPlayer entries={entries} />
      </div>
    </div>
  );
};
export default MediaPlayerTest;
