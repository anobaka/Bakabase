"use client";

"use strict";

import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import type {
  IProperty,
  AttachmentPropertyOptions,
  SingleChoicePropertyOptions,
  MultipleChoicePropertyOptions,
  TypedMultilevelPropertyOptions,
  TypedTagsPropertyOptions,
} from "@/components/Property/models";
import type { LinkValue, TagValue } from "@/components/StandardValue/models";
import type { ValueRendererSize } from "@/components/StandardValue/ValueRenderer/models";
import type { SerializedStandardValue } from "@/components/StandardValue";

import { useTranslation } from "react-i18next";
import React from "react";

import { PropertyType, StandardValueType, PropertyPool, InternalProperty } from "@/sdk/constants";
import { getDbValueType, getBizValueType } from "@/components/Property/PropertySystem";
import {
  AttachmentValueRenderer,
  BooleanValueRenderer,
  ChoiceValueRenderer,
  DateTimeValueRenderer,
  FormulaValueRenderer,
  LinkValueRenderer,
  MultilevelValueRenderer,
  NumberValueRenderer,
  RatingValueRenderer,
  StringValueRenderer,
  TagsValueRenderer,
  TimeValueRenderer,
  deserializeStandardValue,
  findNodeChainByLabels,
  findNodeChainInMultilevelData,
  serializeStandardValue,
} from "@/components/StandardValue";
import { buildLogger } from "@/components/utils";
import ParentResourceValueRenderer from "@/components/ResourceFilter/components/ParentResourceValueRenderer";

export type DataPool = {};

export type Props = {
  property: IProperty;
  /**
   * Both arguments are serialized (wire-format) strings, not raw values.
   */
  onValueChange?: (
    dbValue?: SerializedStandardValue,
    bizValue?: SerializedStandardValue,
  ) => any;
  /**
   * Serialized (wire-format) biz value.
   */
  bizValue?: SerializedStandardValue;
  /**
   * Serialized (wire-format) db value.
   */
  dbValue?: SerializedStandardValue;
  variant?: "default" | "light";
  defaultEditing?: boolean;
  size?: ValueRendererSize;
  isReadonly?: boolean;
  /**
   * When true, always show the editing UI without toggle
   */
  isEditing?: boolean;
  /**
   * Attachment-specific renderer options. Only applied when the property
   * is of type Attachment.
   */
  attachmentPropertyValueRendererProps?: {
    fill?: boolean;
  };
};

const log = buildLogger("PropertyValueRenderer");
const PropertyValueRenderer = (props: Props) => {
  const {
    property,
    variant = "default",
    onValueChange,
    dbValue,
    bizValue,
    defaultEditing,
    size = "md",
    isReadonly: isReadonlyProp,
    isEditing,
    attachmentPropertyValueRendererProps,
  } = props;
  const { t } = useTranslation();

  // Default isReadonly to false
  const isReadonly = isReadonlyProp ?? false;

  // Use PropertySystem for type-safe value type access
  const dbValueType = getDbValueType(property.type);
  const bizValueType = getBizValueType(property.type);

  let bv = deserializeStandardValue(bizValue ?? null, bizValueType);
  const dv = deserializeStandardValue(dbValue ?? null, dbValueType);

  log(props, bv, dv);

  // Use isReadonly to determine if editing is allowed
  const simpleOnValueChange: ((dbValue?: any, bizValue?: any) => any) | undefined =
    !isReadonly && onValueChange
      ? (dv, bv) => {
          const sdv = serializeStandardValue(dv ?? null, dbValueType);
          const sbv = serializeStandardValue(bv ?? null, bizValueType);

          log("OnValueChange:Serialization:dv", dv, sdv);
          log("OnValueChange:Serialization:bv", bv, sbv);

          // Empty serialized payload (cleared text / empty list / etc.)
          // is sent as undefined so the Manual scope record becomes
          // "no value" and the scope priority fallback kicks in.
          return onValueChange(sdv === "" ? undefined : sdv, sbv === "" ? undefined : sbv);
        }
      : undefined;

  const simpleEditor = simpleOnValueChange
    ? {
        value: dv,
        onValueChange: simpleOnValueChange,
      }
    : undefined;

  // Special handling for ParentResource internal property
  if (property.pool === PropertyPool.Internal && property.id === InternalProperty.ParentResource) {
    return (
      <ParentResourceValueRenderer
        bizValue={bizValue}
        dbValue={dbValue}
        defaultEditing={defaultEditing}
        isReadonly={isReadonly}
        property={property}
        size={size}
        variant={variant}
        onValueChange={onValueChange}
      />
    );
  }

  switch (property.type!) {
    case PropertyType.SingleLineText: {
      const typedDv = dv as string;
      const typedBv = (bv as string) ?? typedDv;

      return (
        <StringValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.MultilineText: {
      const typedDv = dv as string;
      const typedBv = (bv as string) ?? typedDv;

      bv ??= dv;

      return (
        <StringValueRenderer
          multiline
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.SingleChoice: {
      const options = property.options as SingleChoicePropertyOptions | undefined;
      const choices = options?.choices ?? [];
      const typedDv = dv as string | undefined;

      const oc =
        onValueChange == undefined
          ? undefined
          : (dbValue?: string[], bizValue?: string[]) => {
              onValueChange(
                dbValue && dbValue.length > 0
                  ? serializeStandardValue(dbValue[0], StandardValueType.String)
                  : undefined,
                bizValue && bizValue.length > 0
                  ? serializeStandardValue(bizValue[0], StandardValueType.String)
                  : undefined,
              );
            };

      const editor = oc
        ? {
            value: typedDv == undefined ? undefined : [typedDv],
            onValueChange: oc,
          }
        : undefined;

      // Strict equality: dv is a single choice id, and substring matching
      // would false-positive on ids that prefix each other. Label and color
      // come from the same lookup so they can never misalign.
      const matchedChoice = choices.find((c) => c.value === typedDv);
      const typedBv = (bv as string) ?? matchedChoice?.label;
      const vas = matchedChoice ? [{ color: matchedChoice.color }] : undefined;

      return (
        <ChoiceValueRenderer
          defaultEditing={defaultEditing}
          editor={editor}
          getDataSource={async () =>
            choices.map((c) => ({ value: c.value, label: c.label ?? "", color: c.color }))
          }
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv == undefined ? undefined : [typedBv]}
          valueAttributes={vas}
          variant={variant}
        />
      );
    }
    case PropertyType.MultipleChoice: {
      const options = property.options as MultipleChoicePropertyOptions | undefined;
      const choices = options?.choices ?? [];
      const typedDv = dv as string[] | undefined;
      const serverBv = bv as string[] | undefined;

      // Derive labels and colors from one pass over the same source array so
      // the two index-parallel props can never misalign — the server-provided
      // biz value can drop entries the client-side dv lookup would keep.
      const entries =
        serverBv != null
          ? serverBv.map((label) => ({
              label,
              color: choices.find((c) => c.label === label)?.color,
            }))
          : (typedDv ?? [])
              .map((v) => choices.find((c) => c.value === v))
              .filter((c) => c?.label != null)
              .map((c) => ({ label: c!.label!, color: c!.color }));
      const typedBv = entries.length > 0 ? entries.map((e) => e.label) : undefined;
      const vas = entries.map((e) => ({ color: e.color }));

      return (
        <ChoiceValueRenderer
          multiple
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () =>
            choices.map((c) => ({ value: c.value, label: c.label ?? "", color: c.color }))
          }
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          valueAttributes={vas}
          variant={variant}
        />
      );
    }
    case PropertyType.Number: {
      const typedDv = dv as number;
      const typedBv = (bv as number) ?? typedDv;

      return (
        <NumberValueRenderer
          as={"number"}
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Percentage: {
      const typedDv = dv as number;
      const typedBv = (bv as number) ?? typedDv;

      return (
        <NumberValueRenderer
          as={"progress"}
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          suffix={"%"}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Rating: {
      const typedDv = dv as number;
      const typedBv = (bv as number) ?? typedDv;

      return (
        <RatingValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Boolean: {
      const typedDv = dv as boolean;
      const typedBv = (bv as boolean) ?? typedDv;

      return (
        <BooleanValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Link: {
      const typedDv = dv as LinkValue;
      const typedBv = (bv as LinkValue) ?? typedDv;

      return (
        <LinkValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Attachment: {
      const typedDv = dv as string[];
      const typedBv = (bv as string[]) ?? typedDv;
      const options = property.options as AttachmentPropertyOptions | undefined;

      return (
        <AttachmentValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          fill={attachmentPropertyValueRendererProps?.fill}
          isEditing={isEditing}
          isReadonly={isReadonly}
          layout={options?.layout}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Date:
    case PropertyType.DateTime: {
      const typedDv = dv as Dayjs;
      const typedBv = (bv as Dayjs) ?? typedDv;

      return (
        <DateTimeValueRenderer
          as={property.type == PropertyType.DateTime ? "datetime" : "date"}
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Time: {
      const typedDv = dv as Duration;
      const typedBv = (bv as Duration) ?? typedDv;

      return (
        <TimeValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Formula: {
      const typedDv = dv as string;
      const typedBv = (bv as string) ?? typedDv;

      return (
        <FormulaValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          variant={variant}
        />
      );
    }
    case PropertyType.Multilevel: {
      const options = property.options as TypedMultilevelPropertyOptions | undefined;
      const data = options?.data ?? [];
      const typedDv = dv as string[] | undefined;
      const serverBv = bv as string[][] | undefined;

      // Label chains and their node colors are derived together: from the
      // server-provided label chains (colors looked up by label per level), or
      // from the client-side id lookup — never one from each.
      const entries =
        serverBv != null
          ? serverBv.map((chain) => {
              const nodeChain = findNodeChainByLabels(data, chain);

              return {
                labels: chain,
                colors: nodeChain
                  ? nodeChain.map((n) => ({ color: n.color }))
                  : chain.map(() => ({})),
              };
            })
          : (typedDv ?? [])
              .map((v) => findNodeChainInMultilevelData(data, v))
              .filter((chain) => chain != undefined)
              .map((chain) => ({
                labels: chain!.map((n) => n.label ?? ""),
                colors: chain!.map((n) => ({ color: n.color })),
              }));
      const typedBv = entries.length > 0 ? entries.map((e) => e.labels) : undefined;
      const vas = entries.map((e) => e.colors);

      return (
        <MultilevelValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () => data}
          isEditing={isEditing}
          isReadonly={isReadonly}
          multiple={!(options?.valueIsSingleton ?? false)}
          size={size}
          value={typedBv}
          valueAttributes={vas}
          variant={variant}
        />
      );
    }
    case PropertyType.Tags: {
      const options = property.options as TypedTagsPropertyOptions | undefined;
      const tags = options?.tags ?? [];
      const typedDv = dv as string[] | undefined;
      const serverBv = bv as TagValue[] | undefined;

      // Tag values and their colors are derived together so the index-parallel
      // props can never misalign (see MultipleChoice above).
      const entries =
        serverBv != null
          ? serverBv.map((tv) => ({
              tag: tv,
              color: tags.find((t) => (t.group ?? "") === (tv.group ?? "") && t.name === tv.name)
                ?.color,
            }))
          : (typedDv ?? [])
              .map((v) => tags.find((t) => t.value === v))
              .filter((t) => t?.name != null)
              .map((t) => ({ tag: { group: t!.group, name: t!.name! }, color: t!.color }));
      const typedBv = entries.length > 0 ? entries.map((e) => e.tag) : undefined;
      const vas = entries.map((e) => ({ color: e.color }));

      return (
        <TagsValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () =>
            tags.map((t) => ({
              value: t.value,
              name: t.name ?? "",
              group: t.group,
              color: t.color,
            }))
          }
          isEditing={isEditing}
          isReadonly={isReadonly}
          size={size}
          value={typedBv}
          valueAttributes={vas}
          variant={variant}
        />
      );
    }
  }
};

PropertyValueRenderer.displayName = "PropertyValueRenderer";

export default PropertyValueRenderer;
