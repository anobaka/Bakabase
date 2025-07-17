"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import _ from "lodash";

import BApi from "@/sdk/BApi";
import { Modal } from "@/components/bakaui";
import FileSystemEntryChangeExampleItem from "@/pages/file-processor/RootTreeEntry/components/FileSystemEntryChangeExampleItem";
import FileSystemEntryChangeExampleMiscellaneousItem from "@/pages/file-processor/RootTreeEntry/components/FileSystemEntryChangeExampleMiscellaneousItem";

type Props = { entries: Entry[] } & DestroyableProps;

export default ({ entries = [], onDestroyed }: Props) => {
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
          children: `${t<string>("Extract")}(Enter)`,
          autoFocus: true,
        },
      }}
      size={"xl"}
      title={t<string>("Extract {{count}} directories", {
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
            <>
              <FileSystemEntryChangeExampleItem
                isDirectory
                text={parent ?? "."}
                type={"default"}
              />
              {innerEntries.map((e, i) => {
                if (e.isDirectoryOrDrive) {
                  return (
                    <>
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
                        text={`${t<string>("Other files")}...`}
                        type={"deleted"}
                      />
                      <FileSystemEntryChangeExampleItem
                        isDirectory={false}
                        layer={1}
                        text={`${t<string>("Other files in {{parent}}", { parent: e.name })}...`}
                        type={"added"}
                      />
                    </>
                  );
                } else {
                  return (
                    <FileSystemEntryChangeExampleItem
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
            </>
          );
        })}
      </div>
    </Modal>
  );
};
