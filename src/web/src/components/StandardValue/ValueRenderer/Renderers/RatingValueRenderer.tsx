"use client";

import type { ValueRendererProps } from "../models";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import NumberValueEditor from "../../ValueEditor/Editors/NumberValueEditor";

import { Rating } from "@/components/bakaui";
import NotSet, { LightText } from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";
import { buildLogger } from "@/components/utils";

type RatingValueRendererProps = ValueRendererProps<number, number> & {
  allowHalf?: boolean;
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("RatingValueRenderer");
const RatingValueRenderer = (props: RatingValueRendererProps) => {
  const { value: propsValue, editor, variant, allowHalf = true, size, isEditing, isReadonly: propsIsReadonly, defaultEditing } = props;
  const [value, setValue] = useState(propsValue);
  const { t } = useTranslation();

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  // For light variant, track editing state for NumberValueEditor
  const [lightEditing, setLightEditing] = useState(defaultEditing && !isReadonly && !!editor && variant === "light");

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  // Handle value change for Rating component
  const handleRatingChange = (r: number | undefined) => {
    if (isReadonly || !editor) return;
    setValue(r);
    editor.onValueChange?.(r, r);
  };

  // For light variant only: editing mode controls
  const startLightEditing = !isReadonly && editor && isEditing !== false && variant === "light" ? () => setLightEditing(true) : undefined;
  const completeLightEditing = (v: number | undefined) => {
    setValue(v);
    setLightEditing(false);
    editor?.onValueChange?.(v, v);
  };

  // Light variant handling
  if (variant === "light") {
    // Direct editing mode (controlled by isEditing prop)
    if (isEditing && !isReadonly && editor) {
      return (
        <NumberValueEditor
          placeholder={t<string>("Set rating")}
          value={value}
          size={size}
          onValueChange={(dv) => {
            setValue(dv);
            editor.onValueChange?.(dv, dv);
          }}
        />
      );
    }

    // Internal editing mode
    if (lightEditing) {
      return (
        <NumberValueEditor
          placeholder={t<string>("Set rating")}
          value={value}
          size={size}
          onValueChange={(dv) => completeLightEditing(dv)}
        />
      );
    }

    // Display mode
    if (value != undefined && value > 0) {
      return (
        <LightText size={size} onClick={startLightEditing}>
          {value}
        </LightText>
      );
    } else {
      return <NotSet size={size} onClick={startLightEditing} />;
    }
  }

  // Non-light variants: Rating component is always interactive (if not readonly)
  const canEdit = !isReadonly && editor;

  return (
    <Rating
      allowHalf={allowHalf}
      value={value}
      size={size}
      disabled={!canEdit}
      onChange={canEdit ? handleRatingChange : undefined}
    />
  );
};

RatingValueRenderer.displayName = "RatingValueRenderer";

export default RatingValueRenderer;
