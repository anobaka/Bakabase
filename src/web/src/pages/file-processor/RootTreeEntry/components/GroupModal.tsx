"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel } from "@/sdk/Api";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Modal, Spinner } from "@/components/bakaui";
import FileSystemEntryChangeExampleItem from "@/pages/file-processor/RootTreeEntry/components/FileSystemEntryChangeExampleItem";
import FileSystemEntryChangeExampleMiscellaneousItem from "@/pages/file-processor/RootTreeEntry/components/FileSystemEntryChangeExampleMiscellaneousItem";

type Props = {
  entries: Entry[];
  groupInternal: boolean;
} & DestroyableProps;

type Group = { directoryName: string; filenames: string[] };

export default ({ entries = [], groupInternal, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [preview, setPreview] =
    useState<BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel[]>();

  useEffect(() => {
    BApi.file
      .previewFileSystemEntriesGroupResult({
        paths: entries.map((e) => e.path),
        groupInternal,
      })
      .then((x) => {
        setPreview(x.data);
      });
  }, []);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: `${t<string>("Group")}(Enter)`,
          autoFocus: true,
          disabled: !preview || preview.length == 0,
        },
      }}
      size={"xl"}
      title={t<string>(
        groupInternal ? "Group internal items" : "Group {{count}} items",
        { count: entries.length },
      )}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await BApi.file.mergeFileSystemEntries({
          paths: entries.map((e) => e.path),
          groupInternal,
        });
      }}
    >
      {preview ? (
        preview.map((p) => {
          return (
            <div className={"flex flex-col gap-1"}>
              <FileSystemEntryChangeExampleItem
                isDirectory
                text={p.rootPath}
                type={"default"}
              />
              {p.groups && p.groups.length > 0 ? (
                p.groups.map((g) => {
                  return (
                    <>
                      <FileSystemEntryChangeExampleItem
                        isDirectory
                        layer={1}
                        text={g.directoryName}
                        type={"added"}
                      />
                      {g.filenames.map((f) => {
                        return (
                          <FileSystemEntryChangeExampleItem
                            layer={2}
                            text={f}
                            type={"added"}
                          />
                        );
                      })}
                      {g.filenames.map((f) => {
                        return (
                          <FileSystemEntryChangeExampleItem
                            layer={1}
                            text={f}
                            type={"deleted"}
                          />
                        );
                      })}
                    </>
                  );
                })
              ) : (
                <FileSystemEntryChangeExampleItem
                  layer={1}
                  text={t<string>(
                    "Nothing to group. Please check if the folder contains any files; subfolders cannot be grouped",
                  )}
                  type={"error"}
                />
              )}
              <FileSystemEntryChangeExampleMiscellaneousItem
                indent={1}
                parent={p.rootPath}
              />
            </div>
          );
        })
      ) : (
        <div className={"flex items-center gap-2"}>
          <Spinner />
          {t<string>("Calculating changes...")}
        </div>
      )}
    </Modal>
  );
};
