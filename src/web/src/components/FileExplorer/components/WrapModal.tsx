"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import _ from "lodash";

import FileSystemEntryChangeExampleMiscellaneousItem from "./FileSystemEntryChangeExampleMiscellaneousItem";

import BApi from "@/sdk/BApi";
import { Modal } from "@/components/bakaui";
import BusinessConstants from "@/components/BusinessConstants";
import FileSystemEntryChangeExampleItem from "./FileSystemEntryChangeExampleItem";

type Props = { entries: Entry[] } & DestroyableProps;
const WrapModal = ({ entries = [], onDestroyed }: Props) => {
  const { t } = useTranslation();
  const groupsRef = useRef(_.groupBy(entries, (e) => e.parent?.path));
  const [newParentNames, setNewParentNames] = useState(
    _.mapValues(groupsRef.current, (g) => g[0]!.meaningfulName),
  );

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: `${t<string>("Wrap")}(Enter)`,
          autoFocus: true,
          disabled: _.values(newParentNames).some((x) => !x || x.length == 0),
        },
      }}
      size={"xl"}
      title={t<string>("Wrapping {{count}} file entries", {
        count: entries.length,
      })}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await Promise.all(
          _.keys(groupsRef.current).map(async (p) => {
            const innerEntries = groupsRef.current[p]!;
            const parentEntry = innerEntries[0]!.parent!;
            const d = [parentEntry.path, newParentNames[p]].join(
              BusinessConstants.pathSeparator,
            );

            await BApi.file.moveEntries({
              destDir: d,
              entryPaths: innerEntries.map((e) => e.path),
            });
          }),
        );
      }}
    >
      <div className={"flex flex-col gap-1"}>
        {Object.keys(groupsRef.current).map((parent) => {
          const innerEntries = groupsRef.current[parent] ?? [];
          const newParentName = newParentNames[parent] ?? "";

          return (
            <React.Fragment key={parent}>
              <FileSystemEntryChangeExampleItem
                isDirectory
                text={parent ?? "."}
                type={"default"}
              />
              <FileSystemEntryChangeExampleItem
                editable
                isDirectory
                layer={1}
                text={newParentName}
                type={"added"}
                onChange={(v) => {
                  setNewParentNames((old) => ({ ...old, [parent]: v }));
                }}
              />
              {innerEntries.map((e) => {
                return (
                  <FileSystemEntryChangeExampleItem
                    key={`${e.path}-added`}
                    isDirectory={e.isDirectory}
                    layer={2}
                    path={e.path}
                    text={e.name}
                    type={"added"}
                  />
                );
              })}
              {innerEntries.map((e) => {
                return (
                  <FileSystemEntryChangeExampleItem
                    key={`${e.path}-deleted`}
                    isDirectory={e.isDirectory}
                    layer={1}
                    path={e.path}
                    text={e.name}
                    type={"deleted"}
                  />
                );
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

WrapModal.displayName = "WrapModal";

export default WrapModal;
