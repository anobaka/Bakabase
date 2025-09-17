"use client";

"use strict";

import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import type { IChoice, IProperty } from "@/components/Property/models";
import type { LinkValue, TagValue } from "@/components/StandardValue/models";

import { useTranslation } from "react-i18next";
import React from "react";
import _ from "lodash";

import { PropertyType, StandardValueType } from "@/sdk/constants";
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
} from "@/components/StandardValue";
import {
  deserializeStandardValue,
  findNodeChainInMultilevelData,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";
import { buildLogger } from "@/components/utils";

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
  } = props;
  const { t } = useTranslation();

  let bv = deserializeStandardValue(bizValue ?? null, property.bizValueType);
  const dv = deserializeStandardValue(dbValue ?? null, property.dbValueType);

  log(props, bv, dv);

  const simpleOnValueChange:
    | ((dbValue?: any, bizValue?: any) => any)
    | undefined = onValueChange
    ? (dv, bv) => {
        const sdv = serializeStandardValue(dv ?? null, property.dbValueType);
        const sbv = serializeStandardValue(bv ?? null, property.bizValueType);

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

  switch (property.type!) {
    case PropertyType.SingleLineText: {
      const typedDv = dv as string;
      const typedBv = (bv as string) ?? typedDv;

      return (
        <StringValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
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
                  ? serializeStandardValue(
                      bizValue[0],
                      StandardValueType.String,
                    )
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
        (property.options?.choices ?? []).find((x) => x.value == typedDv)
          ?.label;
      const vas: IChoice[] = _.sortBy(
        property.options?.choices?.filter((o) => dv?.includes(o.value)) ?? [],
        (x) => x.value == typedDv,
      );

      return (
        <ChoiceValueRenderer
          defaultEditing={defaultEditing}
          editor={editor}
          getDataSource={async () => {
            return property.options?.choices ?? [];
          }}
          value={typedBv == undefined ? undefined : [typedBv]}
          valueAttributes={vas}
          variant={variant}
          size={size}
        />
      );
    }
    case PropertyType.MultipleChoice: {
      const typedDv = dv as string[];
      const typedBv =
        (bv as string[]) ??
        (property.options?.choices ?? [])
          .filter((x) => typedDv?.includes(x.value))
          .map((x) => x.label);
      const vas: IChoice[] = _.sortBy(
        property.options?.choices?.filter((o) => dv?.includes(o.value)) ?? [],
        (x) => typedDv?.findIndex((d) => d == x.value),
      );

      return (
        <ChoiceValueRenderer
          multiple
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () => {
            return property.options?.choices ?? [];
          }}
          value={typedBv}
          valueAttributes={vas}
          variant={variant}
          size={size}
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
          suffix={"%"}
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
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
          value={typedBv}
          variant={variant}
          size={size}
        />
      );
    }
    case PropertyType.Multilevel: {
      const typedDv = dv as string[];
      const tbv = typedDv
        ?.map((v) =>
          findNodeChainInMultilevelData(property?.options?.data || [], v),
        )
        .filter((x) => x != undefined);
      const typedBv =
        (bv as string[][]) ?? tbv?.map((x) => x!.map((y) => y.label));
      const vas = tbv?.map((v) => v!.map((x) => ({ color: x.color })));

      // log(tbv, bv, vas);

      return (
        <MultilevelValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.data || [];
          }}
          multiple={property?.options?.valueIsSingleton ?? true}
          value={typedBv}
          valueAttributes={vas}
          variant={variant}
          size={size}
        />
      );
    }
    case PropertyType.Tags: {
      const typedDv = dv as string[];

      const typedBv =
        (bv as TagValue[]) ??
        (property.options?.tags || [])
          .filter((x) => dv?.includes(x.value))
          .map((x) => ({
            group: x.group,
            name: x.name,
          }));
      const vas = _.sortBy(
        property.options?.tags?.filter((o) => typedDv?.includes(o.value)) ?? [],
        (x) => typedDv?.findIndex((d) => d == x.value),
      );

      return (
        <TagsValueRenderer
          defaultEditing={defaultEditing}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.tags || [];
          }}
          value={typedBv}
          valueAttributes={vas}
          variant={variant}
          size={size}
        />
      );
    }
  }
};

PropertyValueRenderer.displayName = "PropertyValueRenderer";

export default PropertyValueRenderer;
