"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel } from "@/sdk/Api";

import React, { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Slider } from "@heroui/react";

import BApi from "@/sdk/BApi";
import { Modal, Spinner } from "@/components/bakaui";
import FileSystemEntryChangeExampleItem from "./FileSystemEntryChangeExampleItem";
import FileSystemEntryChangeExampleMiscellaneousItem from "./FileSystemEntryChangeExampleMiscellaneousItem";
import { useUpdate } from "react-use";

type Props = {
  entries: Entry[];
  groupInternal: boolean;
} & DestroyableProps;

type Group = { directoryName: string; filenames: string[] };
const GroupModal = ({ entries = [], groupInternal, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [preview, setPreview] =
    useState<BakabaseServiceModelsViewFileSystemEntryGroupResultViewModel[]>();

  const forceUpdate = useUpdate();

  const similarityThresholdRef = useRef(1.0);

  const [caculating, setCaculating] = useState(true);

  const caculate = () => {
    setCaculating(true);
    BApi.file
      .previewFileSystemEntriesGroupResult({
        paths: entries.map((e) => e.path),
        groupInternal,
        similarityThreshold: similarityThresholdRef.current,
      })
      .then((x) => {
        setPreview(x.data);
      })
      .finally(() => {
        setCaculating(false);
      });
  };

  useEffect(() => {
    caculate();
  }, []);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: `${t<string>("fileExplorer.groupModal.okButton")}(Enter)`,
          autoFocus: true,
          disabled: !preview || preview.length == 0,
        },
      }}
      size={"xl"}
      title={t<string>(groupInternal ? "fileExplorer.groupModal.titleInternal" : "fileExplorer.groupModal.title", {
        count: entries.length,
      })}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await BApi.file.groupFileSystemEntries({
          paths: entries.map((e) => e.path),
          groupInternal,
          similarityThreshold: similarityThresholdRef.current,
        });
      }}
    >
      <div className="flex flex-col gap-1">
        <Slider
          defaultValue={similarityThresholdRef.current}
          isDisabled={caculating}
          maxValue={1}
          minValue={0}
          size="sm"
          step={0.01}
          onChange={v => {
            similarityThresholdRef.current = v as number;
            forceUpdate();
          }}
          onChangeEnd={(v) => {
            caculate();
          }}
          renderValue={v => {
            const p = Math.round(similarityThresholdRef.current * 100);
            if (p == 0) {
              return `${t<string>("fileExplorer.groupModal.allItemsOneFolder")}`;
            }
            if (p == 100) {
              return `${t<string>("fileExplorer.groupModal.exactSameNameFolder")}`;
            }
            return `${p}%`;
          }}
          label={t<string>("fileExplorer.groupModal.similarityThreshold")}
        />
        {preview ? (
          preview.map((p) => {
            return (
              <div className={"flex flex-col gap-1"}>
                <FileSystemEntryChangeExampleItem isDirectory text={p.rootPath} type={"default"} />
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
                            <FileSystemEntryChangeExampleItem layer={2} text={f} type={"added"} />
                          );
                        })}
                        {g.filenames.map((f) => {
                          return (
                            <FileSystemEntryChangeExampleItem layer={1} text={f} type={"deleted"} />
                          );
                        })}
                      </>
                    );
                  })
                ) : (
                  <FileSystemEntryChangeExampleItem
                    layer={1}
                    text={t<string>("fileExplorer.groupModal.nothingToGroup")}
                    type={"error"}
                  />
                )}
                <FileSystemEntryChangeExampleMiscellaneousItem indent={1} parent={p.rootPath} />
              </div>
            );
          })
        ) : (
          <div className={"flex items-center gap-2"}>
            <Spinner />
            {t<string>("fileExplorer.groupModal.calculating")}
          </div>
        )}
      </div>
    </Modal>
  );
};

GroupModal.displayName = "GroupModal";

export default GroupModal;
