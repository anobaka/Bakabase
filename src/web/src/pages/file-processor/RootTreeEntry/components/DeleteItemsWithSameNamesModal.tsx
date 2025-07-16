"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Entry } from "@/core/models/FileExplorer/Entry";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { Modal, Spinner } from "@/components/bakaui";
import FileSystemEntryChangeExampleItem from "@/pages/file-processor/RootTreeEntry/components/FileSystemEntryChangeExampleItem";

type Props = {
  entries: Entry[];
  workingDirectory: string;
  onDeleted?: (paths: string[]) => void;
} & DestroyableProps;

type Item = { name: string; isDirectory: boolean; path: string };

export default ({
  entries,
  workingDirectory,
  onDestroyed,
  ...dialogProps
}: Props) => {
  const [deletingAllPaths, setDeletingAllPaths] = useState<Item[]>();

  const { t } = useTranslation();

  useEffect(() => {
    BApi.file
      .getSameNameEntriesInWorkingDirectory({
        workingDir: workingDirectory,
        entryPaths: entries.map((e) => e.path),
      })
      .then((t) => {
        setDeletingAllPaths(t.data || []);
      });

    return () => {
      // console.log(19282, 'exiting');
    };
  }, []);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: `${t<string>("Delete")}(Enter)`,
          color: "danger",
          autoFocus: true,
          disabled: !deletingAllPaths || deletingAllPaths.length == 0,
        },
      }}
      size={"xl"}
      title={t<string>("Delete items with the same names")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        // console.log(deletingAllPaths);
        if (!deletingAllPaths || deletingAllPaths.length == 0) {
          toast.error("Nothing to delete");

          return false;
        } else {
          const rsp = await BApi.file.removeSameNameEntryInWorkingDirectory({
            workingDir: workingDirectory,
            entryPaths: entries.map((e) => e.path),
          });

          return rsp;
        }
      }}
    >
      <div className={"mb-2"}>
        {t<string>(
          "Removing all filesystem entries in {{workingDirectory}} that have the same names as the {{count}} selected filesystem entries",
          {
            count: entries.length,
            workingDirectory,
          },
        )}
      </div>
      {deletingAllPaths ? (
        <>
          <div className={"flex flex-col gap-1"}>
            <FileSystemEntryChangeExampleItem
              text={workingDirectory}
              type={"default"}
            />
            {deletingAllPaths.map((d, i) => {
              return (
                <FileSystemEntryChangeExampleItem
                  isDirectory={d.isDirectory}
                  layer={1}
                  path={d.path}
                  text={d.name}
                  type={"deleted"}
                />
              );
            })}
          </div>
        </>
      ) : (
        <div className={"flex items-center gap-2"}>
          <Spinner />
          {t<string>("Discovering files with same name")}
        </div>
      )}
    </Modal>
  );
};
