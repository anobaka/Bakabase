"use client";

"use strict";
import type { ValueRendererProps } from "../models";

import { type CSSProperties, useEffect } from "react";
import { useTranslation } from "react-i18next";

import ChoiceValueEditor from "../../ValueEditor/Editors/ChoiceValueEditor";

import NotSet from "./components/NotSet";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip } from "@/components/bakaui";
import { autoBackgroundColor, buildLogger } from "@/components/utils";

type Data = { label: string; value: string; color?: string };

type ChoiceValueRendererProps = ValueRendererProps<string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<Data[]>;
  valueAttributes?: { color?: string }[];
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("ChoiceValueRenderer");
const ChoiceValueRenderer = (props: ChoiceValueRendererProps) => {
  const { value, editor, variant, getDataSource, multiple, valueAttributes, size } =
    props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  log(props);

  const onClickValues = editor
    ? async () => {
        // console.log(editor, getDataSource);
        createPortal(ChoiceValueEditor, {
          value: editor?.value,
          getDataSource: getDataSource ?? (async () => []),
          onValueChange: editor?.onValueChange,
          multiple: multiple ?? false,
        });
      }
    : undefined;

  useEffect(() => {
    if (props.defaultEditing) {
      onClickValues?.();
    }
  }, []);

  const validValues = value?.map((v) => v != undefined) || [];

  if (validValues.length == 0) {
    return <NotSet onClick={onClickValues} />;
  }

  if (variant == "light") {
    return (
      <span onClick={onClickValues}>
        {value?.map((v, i) => {
          const styles: CSSProperties = {};

          styles.color = valueAttributes?.[i]?.color;
          if (styles.color) {
            styles.backgroundColor = autoBackgroundColor(styles.color);
          }

          return (
            <>
              {i != 0 && ","}
              <span style={styles}>{v}</span>
            </>
          );
        })}
      </span>
    );
  } else {
    return (
      <div className={"flex flex-wrap gap-1"} onClick={onClickValues}>
        {value?.map((v, i) => {
          const styles: CSSProperties = {};

          styles.color = valueAttributes?.[i]?.color;
          if (styles.color) {
            styles.backgroundColor = autoBackgroundColor(styles.color);
          }

          return (
            <Chip
              className={"h-auto whitespace-break-spaces py-1"}
              radius={"sm"}
              size={size}
              style={styles}
            >
              {v}
            </Chip>
          );
        })}
      </div>
    );
  }
};

ChoiceValueRenderer.displayName = "ChoiceValueRenderer";

export default ChoiceValueRenderer;
