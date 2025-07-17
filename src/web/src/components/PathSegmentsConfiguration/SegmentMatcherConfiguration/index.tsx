"use client";

import type { IPscProperty } from "../models/PscProperty";
import type { ChipProps } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useState } from "react";
import { useUpdateEffect } from "react-use";
import { useTranslation } from "react-i18next";

import { PscMatcherValue } from "../models/PscMatcherValue";

import ByLayer from "./ByLayer";
import ByRegex from "./ByRegex";

import { ResourceMatcherValueType, ResourceProperty } from "@/sdk/constants";
import { Tooltip } from "@/components/bakaui";
import { Chip, Modal } from "@/components/bakaui";

interface IValue {
  layer?: number;
  regex?: string;
}

export class SegmentMatcherConfigurationModesData {
  layers: number[] = [];
  regex?: {
    text: string;
  };

  constructor(init?: Partial<SegmentMatcherConfigurationModesData>) {
    Object.assign(this, init);
  }

  get isConfigurable(): boolean {
    return this.layers.length > 0 || this.regex?.text != undefined;
  }

  get isFullConfigurable(): boolean {
    return this.layers.length > 0 && this.regex?.text != undefined;
  }
}

export type SegmentMatcherConfigurationProps = DestroyableProps & {
  defaultValue?: IValue;
  modesData?: SegmentMatcherConfigurationModesData;
  onSubmit: (value: PscMatcherValue) => void;
  property: IPscProperty;
  segments: string[];
  segmentMarkers: Record<number, SegmentIndexMarker>;
};

type SegmentIndexMarker = "root" | "resource" | "current";

const getDefaultValue = (
  modesData: SegmentMatcherConfigurationProps["modesData"],
): IValue | undefined => {
  if (modesData) {
    if (modesData.layers.length > 0) {
      return {
        layer: modesData.layers[0],
      };
    }
  }

  return;
};

const PathSegmentRenderOptions: Record<
  SegmentIndexMarker,
  { color: ChipProps["color"]; tip: string }
> = {
  root: {
    color: "warning",
    tip: "Segment of root path",
  },
  resource: {
    color: "warning",
    tip: "Segment of resource",
  },
  current: {
    color: "primary",
    tip: "Current segment",
  },
};

export default ({
  defaultValue,
  modesData = new SegmentMatcherConfigurationModesData(),
  onSubmit,
  property,
  segments,
  segmentMarkers,
  ...props
}: SegmentMatcherConfigurationProps) => {
  const { t } = useTranslation();
  const defaultMode =
    modesData.layers.length > 0
      ? "layer"
      : (Object.keys(modesData)[0] as "layer" | "regex");
  const [mode, setMode] = useState(defaultMode);
  const [value, setValue] = useState<IValue | undefined>(
    defaultValue ?? getDefaultValue(modesData),
  );

  // console.log(modesData);

  useUpdateEffect(() => {
    switch (mode) {
      case "layer":
        break;
      case "regex":
        setValue({
          ...(value || {}),
          layer: undefined,
        });
        break;
    }
  }, [mode]);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled:
            !value ||
            (mode == "layer" && !value.layer) ||
            (mode == "regex" && !value.regex),
        },
      }}
      size={"lg"}
      title={t<string>("Configure [{{property}}] property for path segment", {
        property: property.name,
      })}
      onDestroyed={props.onDestroyed}
      onOk={async () => {
        onSubmit?.(
          new PscMatcherValue({
            ...value,
            valueType:
              mode == "layer"
                ? ResourceMatcherValueType.Layer
                : ResourceMatcherValueType.Regex,
          }),
        );
      }}
    >
      <div className={""}>
        <div className={"font-bold"}>{t<string>("Path segment")}</div>
        <div className={"flex items-center gap-1 mb-2"}>
          {segments.map((segment, index) => {
            const marker: SegmentIndexMarker | undefined =
              segmentMarkers[index];
            const options = PathSegmentRenderOptions[marker];

            return (
              <>
                {options != undefined ? (
                  <Tooltip content={t<string>(options.tip)}>
                    <Chip color={options.color} variant={"light"}>
                      {segment}
                    </Chip>
                  </Tooltip>
                ) : (
                  <span>{segment}</span>
                )}
                {index != segments.length - 1 && <span className={""}>/</span>}
              </>
            );
          })}
        </div>
        <ByLayer
          layers={modesData.layers}
          modeIsSelected={mode == "layer"}
          selectedLayer={value?.layer}
          onSelectLayer={(layer) => {
            if (mode != "layer") {
              setMode("layer");
            }
            setValue({
              ...value,
              layer,
            });
          }}
          onSelectMode={() => {
            setMode("layer");
          }}
        />
        <ByRegex
          isResourceProperty={
            property.id == ResourceProperty.Resource && !property.isCustom
          }
          modeIsSelected={mode == "regex"}
          regex={value?.regex}
          textToBeMatched={modesData.regex?.text}
          onRegexChange={(v) => {
            setValue({
              ...value,
              regex: v,
            });
          }}
          onSelectMode={() => {
            setMode("regex");
          }}
        />
      </div>
    </Modal>
  );
};
