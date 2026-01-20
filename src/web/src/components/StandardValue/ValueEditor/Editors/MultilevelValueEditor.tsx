"use client";

import type { CSSProperties } from "react";
import type { ValueEditorProps } from "../models";
import type { MultilevelData } from "@/components/StandardValue/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import React, { useCallback, useEffect, useMemo, useState } from "react";
import { RightOutlined, SearchOutlined } from "@ant-design/icons";
import { useUpdateEffect } from "react-use";

import { Button, Input, Modal } from "@/components/bakaui";
import {
  filterMultilevelData,
  findNodeChainInMultilevelData,
} from "@/components/StandardValue/helpers";
import { autoBackgroundColor, buildLogger } from "@/components/utils";

type Selectable<V> = (
  data: MultilevelData<V>,
  depth: number,
  index: number,
) => boolean;

interface MultilevelValueEditorProps<V>
  extends ValueEditorProps<V[], string[][]>,
    DestroyableProps {
  getDataSource?: () => Promise<MultilevelData<V>[] | undefined>;
  selectable?: Selectable<V>;
  multiple?: boolean;
}

const buildDefaultSelectable: <V>() => Selectable<V> = () => {
  return (data, depth, index) =>
    data.children == undefined || data.children.length == 0;
};

const log = buildLogger("MultilevelValueEditor");
const MultilevelValueEditor = <V = string,>(
  props: MultilevelValueEditorProps<V>,
) => {
  const { t } = useTranslation();

  const {
    getDataSource,
    selectable = buildDefaultSelectable<V>(),
    value: propsValue,
    onValueChange,
    onCancel,
    multiple,
  } = props;

  log(props);

  const [dataSource, setDataSource] = useState<MultilevelData<V>[]>([]);
  const [keyword, setKeyword] = useState("");
  const [value, setValue] = useState<V[]>(propsValue ?? []);
  // Track expanded path: array of values representing expanded items at each level
  const [expandedPath, setExpandedPath] = useState<V[]>([]);

  useEffect(() => {
    loadData();
  }, []);

  useUpdateEffect(() => {
    loadData();
  }, [getDataSource]);

  useEffect(() => {
    setValue(propsValue ?? []);
  }, [propsValue]);

  const loadData = async () => {
    const data = (await getDataSource?.()) ?? [];

    if (data.length > 0) {
      setDataSource(data);
    }
  };

  // Get children at each level based on expanded path
  const getColumnsData = useCallback(() => {
    const columns: { items: MultilevelData<V>[]; depth: number }[] = [];
    let currentItems = filterMultilevelData(dataSource, keyword);
    columns.push({ items: currentItems, depth: 0 });

    for (let i = 0; i < expandedPath.length; i++) {
      const expandedValue = expandedPath[i];
      const expandedItem = currentItems.find((item) => item.value === expandedValue);
      if (expandedItem?.children && expandedItem.children.length > 0) {
        currentItems = filterMultilevelData(expandedItem.children, keyword);
        columns.push({ items: currentItems, depth: i + 1 });
      } else {
        break;
      }
    }

    return columns;
  }, [dataSource, expandedPath, keyword]);

  const columns = getColumnsData();

  const handleItemClick = (item: MultilevelData<V>, depth: number) => {
    const hasChildren = item.children && item.children.length > 0;

    if (hasChildren) {
      // Expand this item - truncate expanded path to this level and add this item
      setExpandedPath([...expandedPath.slice(0, depth), item.value]);
    } else {
      // Leaf node - toggle selection
      if (multiple) {
        if (value.includes(item.value)) {
          setValue(value.filter((v) => v !== item.value));
        } else {
          setValue([...value, item.value]);
        }
      } else {
        setValue([item.value]);
      }
    }
  };

  const renderColumn = (items: MultilevelData<V>[], depth: number) => {
    return (
      <div
        className="flex flex-col gap-1 min-w-[150px] max-w-[200px] p-2 rounded overflow-y-auto"
        style={{ background: "var(--bakaui-overlap-background)" }}
      >
        {items.map((item, idx) => {
          const hasChildren = item.children && item.children.length > 0;
          const isSelected = value.includes(item.value);
          const isExpanded = expandedPath[depth] === item.value;
          const style: CSSProperties = {};

          if (item.color) {
            style.color = item.color;
            if (!isSelected && !isExpanded) {
              style.backgroundColor = autoBackgroundColor(item.color);
            }
          }

          return (
            <Button
              key={idx}
              color={isSelected ? "primary" : isExpanded ? "secondary" : "default"}
              size="sm"
              variant={isExpanded && !isSelected ? "flat" : "solid"}
              style={style}
              className="justify-between"
              onPress={() => handleItemClick(item, depth)}
            >
              <span className="truncate">{item.label}</span>
              {hasChildren && <RightOutlined className="text-xs flex-shrink-0" />}
            </Button>
          );
        })}
      </div>
    );
  };

  // Get selected items display
  const selectedLabels = useMemo(() => {
    return value
      .map((v) => {
        const chain = findNodeChainInMultilevelData(dataSource, v);
        return chain?.map((x) => x.label).join(" / ");
      })
      .filter((x) => x);
  }, [value, dataSource]);

  return (
    <Modal
      defaultVisible
      size={dataSource.length > 10 ? "xl" : "lg"}
      title={t<string>("Select data")}
      onClose={onCancel}
      onOk={async () => {
        const bv = value
          .map((v) =>
            findNodeChainInMultilevelData(dataSource, v)?.map((x) => x.label),
          )
          .filter((x) => x) as string[][];

        onValueChange?.(value, bv);
      }}
    >
      <div className="flex flex-col gap-3 max-h-full min-h-0">
        <div>
          <Input
            size="sm"
            placeholder={t<string>("common.placeholder.search")}
            startContent={<SearchOutlined className="text-small" />}
            value={keyword}
            onValueChange={(v) => setKeyword(v)}
          />
        </div>

        {/* Selected items display */}
        {selectedLabels.length > 0 && (
          <div className="flex flex-wrap gap-1 p-2 rounded bg-default-100">
            <span className="text-sm text-default-500 mr-1">{t<string>("Selected")}:</span>
            {selectedLabels.map((label, i) => (
              <span key={i} className="text-sm bg-primary-100 text-primary-700 px-2 py-0.5 rounded">
                {label}
              </span>
            ))}
          </div>
        )}

        {/* Cascader columns */}
        <div className="flex gap-2 min-h-0 overflow-x-auto overflow-y-hidden">
          {columns.length > 0 && columns[0].items.length > 0 ? (
            columns.map((column, i) => (
              <React.Fragment key={i}>
                {renderColumn(column.items, column.depth)}
              </React.Fragment>
            ))
          ) : (
            <div className="m-2 flex items-center justify-center text-default-400">
              {t<string>("common.state.noData")}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
};

MultilevelValueEditor.displayName = "MultilevelValueEditor";

export default MultilevelValueEditor;
