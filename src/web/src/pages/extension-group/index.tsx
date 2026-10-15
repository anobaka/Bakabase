"use client";

import { useTranslation } from "react-i18next";
import React, { useCallback, useEffect, useState } from "react";
import { DeleteOutlined, EditOutlined } from "@ant-design/icons";
import toast from "react-hot-toast";

import {
  Button,
  Chip,
  Input,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Textarea,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  DataSyncHeaderLink,
  DefinitionSyncRow,
  useDefinitionSync,
} from "@/features/data-sync/components/DefinitionsPageSync";
import {
  useOwnedCatalog,
  catalogItemKey,
  catalogItemApi,
  canEditCatalogItem,
  CatalogOwnerMark,
  CatalogSourceStatus,
  type OwnedCatalogItem,
} from "@/features/management/ownedCatalog";
import BApi from "@/sdk/BApi";

export type ExtensionGroup = {
  id: number;
  name: string;
  extensions?: string[];
};

// const testGroups: Group[] = [
const ExtensionGroupPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [groups, setGroups] = useState<ExtensionGroup[]>([]);
  const [editingGroup, setEditingGroup] = useState<OwnedCatalogItem<ExtensionGroup> | undefined>(
    undefined,
  );
  const catalog = useOwnedCatalog(groups, (api) => api.extensionGroup.getAllExtensionGroups());
  const allGroups = [...catalog.items].sort(
    (a, b) => a.name.localeCompare(b.name) || catalogItemKey(a).localeCompare(catalogItemKey(b)),
  );
  const editingOwner =
    editingGroup?.owner && !editingGroup.owner.isLocal
      ? (catalog.sources.find(
          (source) =>
            source.serverId === editingGroup.owner!.serverId &&
            source.libraryEpoch === editingGroup.owner!.libraryEpoch,
        ) ?? { ...editingGroup.owner, available: false })
      : editingGroup?.owner;
  const canSave = canEditCatalogItem({ owner: editingOwner });
  const [editingExtensionsText, setEditingExtensionsText] = useState<string>("");

  const loadGroups = useCallback(() => {
    BApi.extensionGroup.getAllExtensionGroups().then((r) => {
      setGroups(r.data || []);
    });
  }, []);

  useEffect(() => {
    loadGroups();
  }, [loadGroups]);
  const dataSync = useDefinitionSync("extensionGroup", loadGroups);

  return (
    <div>
      <Modal
        title={t<string>("extensionGroup.action.editGroup")}
        visible={!!editingGroup}
        footer={{ actions: ["ok", "cancel"], okProps: { isDisabled: !canSave } }}
        onClose={() => setEditingGroup(undefined)}
        onOk={async () => {
          if (!editingGroup || !canSave) throw new Error(t("management.catalog.ownerUnavailable"));
          const api = catalogItemApi({ owner: editingOwner });
          const body = { name: editingGroup.name, extensions: editingGroup.extensions ?? [] };
          const r =
            editingGroup.id === 0
              ? await api.extensionGroup.addExtensionGroup(body)
              : await api.extensionGroup.putExtensionGroup(editingGroup.id, body);

          if (r.code) {
            toast.error(r.message!);
            throw new Error(r.message);
          }
          loadGroups();
          catalog.refresh();
          setEditingGroup(undefined);
        }}
      >
        <Input
          isRequired
          label={t<string>("common.label.name")}
          value={editingGroup?.name}
          onValueChange={(v) => {
            setEditingGroup({
              ...editingGroup!,
              name: v,
            });
          }}
        />
        <div>
          <Textarea
            fullWidth
            label={t<string>("extensionGroup.label.extensions")}
            minRows={4}
            placeholder={t<string>("extensionGroup.input.separatePlaceholder")}
            value={editingExtensionsText}
            onValueChange={(v) => {
              setEditingExtensionsText(v);
              setEditingGroup({
                ...editingGroup!,
                extensions: extractExtensions(v)!,
              });
            }}
          />
          <div className={"mt-2 flex flex-wrap gap-1"}>
            {editingGroup?.extensions?.map((ext, i) => {
              return (
                <Chip key={i} radius={"sm"} size={"sm"} variant={"flat"}>
                  {ext}
                </Chip>
              );
            })}
          </div>
        </div>
      </Modal>
      {dataSync.host}
      <CatalogSourceStatus {...catalog} />
      <div className={"flex flex-wrap items-center gap-2"}>
        <Button
          color={"primary"}
          size={"sm"}
          onPress={() => {
            setEditingExtensionsText("");
            setEditingGroup({
              id: 0,
              name: "",
              extensions: [],
            });
          }}
        >
          {t<string>("extensionGroup.action.addGroup")}
        </Button>
        <DataSyncHeaderLink />
      </div>
      <Table isStriped removeWrapper className={"mt-2"}>
        <TableHeader>
          <TableColumn>{t<string>("common.label.name")}</TableColumn>
          <TableColumn>{t<string>("extensionGroup.label.extensions")}</TableColumn>
          <TableColumn>{t<string>("common.label.operations")}</TableColumn>
        </TableHeader>
        <TableBody>
          {allGroups.map((eg) => {
            return (
              <TableRow key={catalogItemKey(eg)}>
                <TableCell>
                  <div className={"flex flex-wrap items-center gap-2"}>
                    {eg.name}
                    <CatalogOwnerMark owner={eg.owner} />
                    {(!eg.owner || eg.owner.isLocal) && (
                      <DefinitionSyncRow
                        kind="extensionGroup"
                        localKey={String(eg.id)}
                        menu={false}
                        name={eg.name}
                        sync={dataSync}
                      />
                    )}
                  </div>
                </TableCell>
                <TableCell>
                  {eg.extensions?.map((ext, j) => {
                    return (
                      <Chip
                        key={j}
                        isCloseable={canEditCatalogItem(eg)}
                        size={"sm"}
                        variant={"flat"}
                        onClose={async () => {
                          await catalogItemApi(eg).extensionGroup.putExtensionGroup(eg.id, {
                            name: eg.name,
                            extensions: eg.extensions?.filter((_, index) => index !== j) ?? [],
                          });
                          loadGroups();
                          catalog.refresh();
                        }}
                      >
                        {ext}
                      </Chip>
                    );
                  })}
                </TableCell>
                <TableCell>
                  <div className={"flex items-center gap-1"}>
                    <Button
                      isIconOnly
                      color={"primary"}
                      isDisabled={!canEditCatalogItem(eg)}
                      size={"sm"}
                      variant={"light"}
                      onPress={() => {
                        setEditingExtensionsText((eg.extensions ?? []).join(" "));
                        setEditingGroup(JSON.parse(JSON.stringify(eg)));
                      }}
                    >
                      <EditOutlined className={"text-base"} />
                    </Button>
                    <Button
                      isIconOnly
                      color={"danger"}
                      isDisabled={!canEditCatalogItem(eg)}
                      size={"sm"}
                      variant={"light"}
                      onPress={() => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t<string>("extensionGroup.confirm.deleteTitle"),
                          children: t<string>("common.confirm.irreversibleWarning"),
                          onOk: async () => {
                            await catalogItemApi(eg).extensionGroup.deleteExtensionGroup(eg.id);
                            loadGroups();
                            catalog.refresh();
                          },
                        });
                      }}
                    >
                      <DeleteOutlined className={"text-base"} />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
};

ExtensionGroupPage.displayName = "ExtensionGroupPage";
//   {
//     id: 1,
//     name: 'Group 1',
//     extensions: ['.jpg', '.png'],
//   },
//   {
//     id: 2,
//     name: 'Group 2',
//     extensions: ['.mp4', '.avi'],
//   },
//   {
//     id: 3,
//     name: 'Group 3',
//     extensions: ['.docx', '.pdf'],
//   },
//   {
//     id: 4,
//     name: 'Group 4',
//     extensions: ['.xlsx', '.csv'],
//   },
//   {
//     id: 5,
//     name: 'Group 5',
//     extensions: ['.pptx', '.txt'],
//   },
// ];

function extractExtensions(text: string): string[] {
  const extensions = text
    .replace(/\n/g, " ")
    .split(" ")
    .map((x) => x.trim().replace(/^\.+|\.+$/g, ""))
    .filter((x) => x.length > 0)
    .map((x) => `.${x}`);

  return Array.from(new Set(extensions));
}

export default ExtensionGroupPage;
