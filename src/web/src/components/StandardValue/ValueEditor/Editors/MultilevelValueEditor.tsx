"use client";

import type { CSSProperties } from "react";
import type { ValueEditorProps } from "../models";
import type { MultilevelData } from "@/components/StandardValue/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import React, { useCallback, useEffect, useState } from "react";
import { DoubleRightOutlined, SearchOutlined } from "@ant-design/icons";
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

export default <V = string,>(props: MultilevelValueEditorProps<V>) => {
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

  const renderTreeNodes = useCallback(
    (data: MultilevelData<V>[]) => {
      const leaves = data.filter((d) => !d.children || d.children.length == 0);
      const branches = data.filter((d) => d.children && d.children.length > 0);

      return (
        <div
          className={"flex flex-col gap-1 rounded p-2 grow"}
          style={{ background: "var(--bakaui-overlap-background)" }}
        >
          {leaves.length > 0 && (
            <div className={"flex flex-wrap gap-1"}>
              {leaves.map(({ value: nodeValue, color, label }, idx) => {
                const isSelected = value.includes(nodeValue);
                const style: CSSProperties = {};

                if (color) {
                  style.color = color;
                  if (!isSelected) {
                    style.backgroundColor = autoBackgroundColor(color);
                  }
                }

                return (
                  <Button
                    key={idx}
                    color={isSelected ? "primary" : "default"}
                    size={"sm"}
                    style={style}
                    onPress={() => {
                      if (multiple) {
                        if (isSelected) {
                          setValue(value.filter((v) => v !== nodeValue));
                        } else {
                          setValue([...value, nodeValue]);
                        }
                      } else {
                        setValue([nodeValue]);
                      }
                    }}
                  >
                    {label}
                  </Button>
                );
              })}
            </div>
          )}
          {branches.length > 0 && (
            <div
              className={"grid items-center gap-1 grow"}
              style={{ gridTemplateColumns: "auto auto minmax(0, 1fr)" }}
            >
              {branches.map(({ value, label, color, children }, idx) => {
                const style: CSSProperties = {};

                if (color) {
                  style.color = color;
                }

                return (
                  <React.Fragment key={idx}>
                    <div className={"flex justify-end"} style={style}>
                      {label}
                    </div>
                    <DoubleRightOutlined className={"text-small"} />
                    {renderTreeNodes(children!)}
                  </React.Fragment>
                );
              })}
            </div>
          )}
        </div>
      );
    },
    [value],
  );

  const filteredData = filterMultilevelData(dataSource, keyword);

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
      <div className={"flex flex-col gap-1 max-h-full min-h-0"}>
        <div>
          <Input
            size={"sm"}
            startContent={<SearchOutlined className={"text-small"} />}
            value={keyword}
            onValueChange={(v) => {
              setKeyword(v);
            }}
          />
        </div>
        <div className={"flex flex-wrap gap-1 w-full min-h-0 overflow-y-auto"}>
          {filteredData.length > 0 ? (
            renderTreeNodes(filteredData)
          ) : (
            <div className={"m-2 flex items-center justify-center"}>
              {t<string>("No data")}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
};
