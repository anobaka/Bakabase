"use client";

import type { Key, ReactNode } from "react";
import type { MultilevelPropertyOptions } from "@/components/Property/models";

import { DeleteOutlined, EditOutlined, PlusOutlined } from "@ant-design/icons";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import ReferenceValueUsage from "../ReferenceValueUsage";
import { ReferenceColor } from "../ChoiceList/ReferenceItemTools";

import { Button, Input, Modal, Tree } from "@/components/bakaui";
import { buildUntitledLabel, uuidv4 } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  options?: MultilevelPropertyOptions;
  onChange?: (value: MultilevelPropertyOptions) => void;
};

type Node = {
  value: string;
  label?: string;
  color?: string;
  children?: Node[];
};
type TreeData = { title: ReactNode; key: string; children?: TreeData[]; selectable: false };

const MultilevelData = ({ options: propOptions, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const options = propOptions ?? {};
  const [editingKey, setEditingKey] = useState<string>();
  const editingInputRef = useRef<HTMLInputElement>(null);
  const [expandedKeys, setExpandedKeys] = useState<Key[]>(
    options.data?.map((node) => node.value) ?? [],
  );
  const allKeys: string[] = [];
  const collectKeys = (nodes: Node[]) =>
    nodes.forEach((node) => {
      allKeys.push(node.value);
      collectKeys(node.children ?? []);
    });

  collectKeys(options.data ?? []);

  useEffect(() => {
    if (editingKey) editingInputRef.current?.focus();
  }, [editingKey]);

  const patchOptions = (patches: Partial<MultilevelPropertyOptions>) =>
    onChange?.({ ...options, ...patches });

  const createNode = (siblings: Node[]): Node => ({
    value: uuidv4(),
    label: buildUntitledLabel(
      t("property.referenceEditor.tree.untitled"),
      siblings.map((node) => node.label),
    ),
  });

  const buildTreeData = (siblings: Node[]): TreeData[] =>
    siblings.map((node) => ({
      key: node.value,
      selectable: false,
      children: node.children?.length ? buildTreeData(node.children) : undefined,
      title: (
        <div className="my-0.5 flex min-w-0 flex-wrap items-center gap-1.5 rounded-lg bg-default-50 px-2 py-1.5">
          <ReferenceColor
            color={node.color}
            onChange={(color) => {
              node.color = color;
              patchOptions({ ...options });
            }}
          />
          {editingKey === node.value ? (
            <Input
              ref={editingInputRef}
              aria-label={t("property.referenceEditor.tree.name")}
              className="min-w-[6rem] flex-1"
              classNames={{ inputWrapper: "bg-default-100 shadow-none" }}
              size="sm"
              value={node.label ?? ""}
              onBlur={() => setEditingKey(undefined)}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === "Escape") setEditingKey(undefined);
              }}
              onValueChange={(label) => {
                node.label = label;
                patchOptions({ ...options });
              }}
            />
          ) : (
            <Button
              aria-label={t("property.referenceEditor.tree.rename", { name: node.label })}
              className="h-auto min-h-8 min-w-[6rem] flex-1 justify-start whitespace-normal break-words px-1 text-left text-sm"
              endContent={<EditOutlined className="shrink-0 text-xs text-default-400" />}
              size="sm"
              variant="light"
              onPress={() => setEditingKey(node.value)}
            >
              <span className="min-w-0 break-words">
                {node.label || t("property.referenceEditor.tree.untitled")}
              </span>
            </Button>
          )}
          <div className="ml-auto flex shrink-0 items-center gap-0.5">
            <ReferenceValueUsage label={node.label} value={node.value} />
            <Button
              isIconOnly
              aria-label={t("property.referenceEditor.tree.addChild")}
              size="sm"
              title={t("property.referenceEditor.tree.addChild")}
              variant="light"
              onPress={() => {
                const child = createNode(node.children ?? []);

                node.children = [...(node.children ?? []), child];
                patchOptions({ ...options });
                setExpandedKeys((keys) => [...new Set([...keys, node.value])]);
                setEditingKey(child.value);
              }}
            >
              <PlusOutlined />
            </Button>
            <Button
              isIconOnly
              aria-label={t("property.referenceEditor.delete")}
              className="text-default-400 hover:text-danger"
              color="danger"
              size="sm"
              title={t("property.referenceEditor.delete")}
              variant="light"
              onPress={() =>
                createPortal(Modal, {
                  defaultVisible: true,
                  size: "sm",
                  title: t("property.referenceEditor.tree.deleteTitle"),
                  children: t("property.referenceEditor.tree.deleteDescription"),
                  onOk: () => {
                    siblings.splice(siblings.indexOf(node), 1);
                    patchOptions({ ...options });
                  },
                })
              }
            >
              <DeleteOutlined />
            </Button>
          </div>
        </div>
      ),
    }));

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="text-sm font-medium">{t("property.referenceEditor.tree.title")}</div>
          <p className="mt-1 text-xs leading-relaxed text-default-500">
            {t("property.referenceEditor.tree.help")}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <Button
            color="primary"
            size="sm"
            startContent={<PlusOutlined />}
            variant="flat"
            onPress={() => {
              const node = createNode(options.data ?? []);

              patchOptions({ data: [...(options.data ?? []), node] });
              setEditingKey(node.value);
            }}
          >
            {t("property.referenceEditor.tree.addRoot")}
          </Button>
          <Button
            isDisabled={allKeys.length === 0}
            size="sm"
            variant="light"
            onPress={() => setExpandedKeys(allKeys)}
          >
            {t("property.referenceEditor.tree.expandAll")}
          </Button>
          <Button
            isDisabled={expandedKeys.length === 0}
            size="sm"
            variant="light"
            onPress={() => setExpandedKeys([])}
          >
            {t("property.referenceEditor.tree.collapseAll")}
          </Button>
        </div>
      </div>
      {allKeys.length === 0 ? (
        <div className="rounded-xl bg-default-50 px-4 py-6 text-center text-sm text-default-400">
          {t("property.referenceEditor.tree.empty")}
        </div>
      ) : (
        <div className="max-h-[24rem] min-w-0 overflow-auto">
          <Tree
            blockNode
            checkable={false}
            className="!bg-transparent [&_.ant-tree-treenode]:w-full [&_.ant-tree-node-content-wrapper]:min-w-0 [&_.ant-tree-title]:block [&_.ant-tree-indent-unit]:!w-4"
            expandedKeys={expandedKeys}
            selectable={false}
            treeData={buildTreeData(options.data ?? [])}
            virtual={false}
            onExpand={setExpandedKeys}
          />
        </div>
      )}
    </div>
  );
};

export default MultilevelData;
