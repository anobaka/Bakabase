"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import _ from "lodash";

import BApi from "@/sdk/BApi";
import { Modal } from "@/components/bakaui";
import FileSystemEntryChangeExampleItem from "./FileSystemEntryChangeExampleItem";
import FileSystemEntryChangeExampleMiscellaneousItem from "./FileSystemEntryChangeExampleMiscellaneousItem";

type Props = { entries: Entry[] } & DestroyableProps;
const ExtractModal = ({ entries = [], onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [targetEntries, setTargetEntries] = useState<Entry[]>([]);

  useEffect(() => {
    const directoryEntries = _.sortBy(
      _.sortBy(
        entries.filter((entry) => entry.isDirectory),
        (e) => e.path,
      ),
      (x) => x.path.length,
    );
    const outerDirectoryEntries: Entry[] = [];
    const outerDirectoryPaths: Set<string> = new Set();

    for (const de of directoryEntries) {
      let skip = false;

      for (const odp of outerDirectoryPaths) {
        if (de.path.startsWith(odp)) {
          skip = true;
        }
      }
      if (!skip) {
        outerDirectoryPaths.add(`${de.path}/`);
        outerDirectoryEntries.push(de);
      }
    }
    setTargetEntries(outerDirectoryEntries);
  }, [entries]);

  // const { parent } = entries[0]!;
  const groups = _.groupBy(targetEntries, (e) => e.parent?.path);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: `${t<string>("fileExplorer.extractModal.okButton")}(Enter)`,
          autoFocus: true,
        },
      }}
      size={"xl"}
      title={t<string>("fileExplorer.extractModal.title", {
        count: entries.length,
      })}
      onDestroyed={onDestroyed}
      onOk={async () => {
        for (const e of targetEntries) {
          await BApi.file.extractAndRemoveDirectory({ directory: e.path });
        }
      }}
    >
      <div className={"flex flex-col gap-1"}>
        {Object.keys(groups).map((parent) => {
          const innerEntries = groups[parent] ?? [];

          return (
            <React.Fragment key={parent}>
              <FileSystemEntryChangeExampleItem
                isDirectory
                text={parent ?? "."}
                type={"default"}
              />
              {innerEntries.map((e) => {
                if (e.isDirectoryOrDrive) {
                  return (
                    <React.Fragment key={e.path}>
                      <FileSystemEntryChangeExampleItem
                        isDirectory={e.isDirectory}
                        layer={1}
                        path={e.path}
                        text={e.name}
                        type={"deleted"}
                      />
                      <FileSystemEntryChangeExampleItem
                        isDirectory={false}
                        layer={2}
                        text={`${t<string>("fileExplorer.extractModal.otherFiles")}...`}
                        type={"deleted"}
                      />
                      <FileSystemEntryChangeExampleItem
                        isDirectory={false}
                        layer={1}
                        text={`${t<string>("fileExplorer.extractModal.otherFilesIn", { parent: e.name })}...`}
                        type={"added"}
                      />
                    </React.Fragment>
                  );
                } else {
                  return (
                    <FileSystemEntryChangeExampleItem
                      key={e.path}
                      isDirectory={e.isDirectory}
                      layer={1}
                      path={e.path}
                      text={e.name}
                      type={"default"}
                    />
                  );
                }
              })}
              <FileSystemEntryChangeExampleMiscellaneousItem
                indent={1}
                parent={parent}
              />
            </React.Fragment>
          );
        })}
      </div>
    </Modal>
  );
};

ExtractModal.displayName = "ExtractModal";

export default ExtractModal;
