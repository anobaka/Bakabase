"use client";

"use strict";

import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import type { IProperty, ChoiceOption, TagOption } from "@/components/Property/models";
import type { LinkValue, TagValue } from "@/components/StandardValue/models";

import { useTranslation } from "react-i18next";
import React from "react";
import _ from "lodash";

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
  findNodeChainInMultilevelData,
  serializeStandardValue,
} from "@/components/StandardValue";
import { buildLogger } from "@/components/utils";
import ParentResourceValueRenderer from "@/components/ResourceFilter/components/ParentResourceValueRenderer";

export type DataPool = {};

export type Props = {
  property: IProperty;
  /**
   * Serialized
   */
  onValueChange?: (dbValue?: string, bizValue?: string) => any;
  /**
   * Serialized
   */
  bizValue?: string;
  /**
   * Serialized
   */
  dbValue?: string;
  variant?: "default" | "light";
  defaultEditing?: boolean;
  size?: "sm" | "md" | "lg";
  isReadonly?: boolean;
  /**
   * When true, always show the editing UI without toggle
   */
  isEditing?: boolean;
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

          return onValueChange(sdv, sbv);
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
      const typedDv = dv as string;

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

      // console.log(editor, property);

      const typedBv =
        (bv as string) ??
        (property.options?.choices ?? []).find((x: ChoiceOption) => x.value == typedDv)?.label;
      const vas: ChoiceOption[] = _.sortBy(
        property.options?.choices?.filter((o: ChoiceOption) => dv?.includes(o.value)) ?? [],
        (x: ChoiceOption) => x.value == typedDv,
      );

      return (
        <ChoiceValueRenderer
          defaultEditing={defaultEditing}
          editor={editor}
          getDataSource={async () => {
            return property.options?.choices ?? [];
          }}
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
      const typedDv = dv as string[];
      const typedBv =
        (bv as string[]) ??
        (property.options?.choices ?? [])
          .filter((x: ChoiceOption) => typedDv?.includes(x.value))
          .map((x: ChoiceOption) => x.label);
      const vas: ChoiceOption[] = _.sortBy(
        property.options?.choices?.filter((o: ChoiceOption) => dv?.includes(o.value)) ?? [],
        (x: ChoiceOption) => typedDv?.findIndex((d) => d == x.value),
      );

      return (
        <ChoiceValueRenderer
          multiple
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () => {
            return property.options?.choices ?? [];
          }}
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

      return (
        <AttachmentValueRenderer
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
      const typedDv = dv as string[];
      const tbv = typedDv
        ?.map((v) => findNodeChainInMultilevelData(property?.options?.data || [], v))
        .filter((x) => x != undefined);
      const typedBv = (bv as string[][]) ?? tbv?.map((x) => x!.map((y) => y.label));
      const vas = tbv?.map((v) => v!.map((x) => ({ color: x.color })));

      // log(tbv, bv, vas);

      return (
        <MultilevelValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.data || [];
          }}
          isEditing={isEditing}
          isReadonly={isReadonly}
          multiple={property?.options?.valueIsSingleton ?? true}
          size={size}
          value={typedBv}
          valueAttributes={vas}
          variant={variant}
        />
      );
    }
    case PropertyType.Tags: {
      const typedDv = dv as string[];
      const tags = (property.options?.tags || []) as TagOption[];

      const typedBv =
        (bv as TagValue[]) ??
        tags
          .filter((x: TagOption) => dv?.includes(x.value))
          .map((x: TagOption) => ({
            group: x.group,
            name: x.name,
          }));
      const vas = _.sortBy(
        tags.filter((o: TagOption) => typedDv?.includes(o.value)),
        (x: TagOption) => typedDv?.findIndex((d) => d == x.value),
      );

      return (
        <TagsValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.tags || [];
          }}
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
