"use client";

"use strict";
import type { CSSProperties } from "react";
import type { ValueRendererProps } from "../models";
import type { MultilevelData } from "../../models";

import { useEffect } from "react";
import { useTranslation } from "react-i18next";

import MultilevelValueEditor from "../../ValueEditor/Editors/MultilevelValueEditor";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip, Card, CardBody, Button } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/NotSet";
import { autoBackgroundColor } from "@/components/utils";

type MultilevelValueRendererProps = ValueRendererProps<string[][], string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<MultilevelData<string>[]>;
  valueAttributes?: { color?: string }[][];
};
const MultilevelValueRenderer = ({
  value,
  editor,
  variant,
  getDataSource,
  multiple,
  defaultEditing,
  valueAttributes,
  ...props
}: MultilevelValueRendererProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  console.log("[MultilevelValueRenderer]", value, variant);

  useEffect(() => {
    if (defaultEditing) {
      showEditor();
    }
  }, []);

  const showEditor = () => {
    createPortal(MultilevelValueEditor<string>, {
      getDataSource: getDataSource,
      onValueChange: editor?.onValueChange,
      multiple,
      value: editor?.value,
    });
  };

  if (value == undefined || value.length == 0) {
    return <NotSet onClick={editor && showEditor} />;
  }

  // console.log(editor);

  if (variant == "light") {
    const label: any[] = [];

    if (value) {
      for (let i = 0; i < value.length; i++) {
        const arr = value[i];

        if (arr) {
          if (i != 0) {
            label.push(";");
          }
          for (let j = 0; j < arr.length; j++) {
            if (j != 0) {
              label.push("/");
            }
            const style: CSSProperties = {};
            const color = valueAttributes?.[i]?.[j]?.color;

            if (color) {
              style.color = color;
              style.backgroundColor = autoBackgroundColor(color);
            }
            label.push(<span style={style}>{arr[j]}</span>);
          }
        }
      }
    }

    return (
      <Button
        radius={"sm"}
        size={"sm"}
        variant={"light"}
        onPress={editor ? showEditor : undefined}
      >
        {label}
      </Button>
    );
  } else {
    return (
      <Card isPressable={!!editor} onPress={editor ? showEditor : undefined}>
        <CardBody className={"flex flex-wrap gap-1"}>
          {value?.map((v, i) => {
            const label: any[] = [];

            for (let j = 0; j < v.length; j++) {
              if (j != 0) {
                label.push("/");
              }
              const style: CSSProperties = {};
              const color = valueAttributes?.[i]?.[j]?.color;

              if (color) {
                style.color = color;
                style.backgroundColor = autoBackgroundColor(color);
              }
              label.push(<span style={style}>{v[j]}</span>);
            }

            return (
              <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                {label}
              </Chip>
            );
          })}
        </CardBody>
      </Card>
    );
  }
};

MultilevelValueRenderer.displayName = "MultilevelValueRenderer";

export default MultilevelValueRenderer;
