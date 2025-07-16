"use client";

import type { ValueRendererProps } from "../models";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import NumberValueEditor from "../../ValueEditor/Editors/NumberValueEditor";

import { Rating } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/NotSet";
import { buildLogger } from "@/components/utils";
type RatingValueRendererProps = ValueRendererProps<number, number> & {
  allowHalf?: boolean;
};

const log = buildLogger("RatingValueRenderer");

export default (props: RatingValueRendererProps) => {
  const { value: propsValue, editor, variant, allowHalf = true } = props;
  const [value, setValue] = useState(propsValue);
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);

  // log(props);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const startEditing = editor ? () => setEditing(true) : undefined;
  const changeValue = (v: number) => {
    setValue(v);
    setEditing(false);
    // console.log('changeValue', v);
    editor?.onValueChange?.(v, v);
  };

  if (editing) {
    return (
      <NumberValueEditor
        placeholder={t<string>("Set rating")}
        value={value}
        onValueChange={changeValue}
      />
    );
  } else {
    if (variant == "light") {
      if (value != undefined && value > 0) {
        return <span onClick={startEditing}>{value}</span>;
      } else {
        return <NotSet onClick={startEditing} />;
      }
    } else {
      return (
        <div className={"flex gap-1"}>
          <Rating
            allowHalf={allowHalf}
            value={value}
            onChange={(r) => {
              if (value == undefined) {
                setValue(r);
              }
              setEditing(true);
            }}
          />
          {value}
        </div>
      );
    }
  }
};
