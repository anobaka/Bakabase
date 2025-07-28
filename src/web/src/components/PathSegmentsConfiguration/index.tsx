"use client";

import type { PscContext } from "./models/PscContext";
import type { PscPropertyType } from "./models/PscPropertyType";
import type PscProperty from "./models/PscProperty";
import type { IPscPropertyMatcherValue } from "./models/PscPropertyMatcherValue";

import React, { useEffect, useImperativeHandle, useRef, useState } from "react";
import { useUpdate, useUpdateEffect } from "react-use";
import { useTranslation } from "react-i18next";

import Tips from "./Tips";
import Errors from "./Errors";
import Segments from "./Segments";
import { allMatchers } from "./matchers";
import { BuildPscContext } from "./helpers";
import FileExtensionLoader from "./FileExtensionLoader";
import GlobalMatches from "./GlobalMatches";
import BottomOperations from "./BottomOperations";

import { buildLogger, useTraceUpdate } from "@/components/utils";
import { Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

export class PathSegmentConfigurationPropsMatcherOptions {
  propertyType: PscPropertyType;
  readonly = false;

  constructor(init?: Partial<PathSegmentConfigurationPropsMatcherOptions>) {
    Object.assign(this, init);
  }
}

interface IPathSegmentsConfigurationProps {
  segments: string[];
  isDirectory: boolean;
  // type - readonly
  matchers?: PathSegmentConfigurationPropsMatcherOptions[];
  defaultValue?: IPscPropertyMatcherValue[];
  onChange?: (value: IPscPropertyMatcherValue[]) => void;
  // onError?: (hasError: boolean) => void;
}

export interface IPathSegmentConfigurationRef {
  readonly context: PscContext;
}

const PathSegmentsConfigurationInner = React.forwardRef(
  (props: IPathSegmentsConfigurationProps, ref) => {
    useTraceUpdate(props, "PathSegmentsConfiguration");

    const {
      segments = [],
      isDirectory,
      matchers = [],
      defaultValue,
      onChange = (v) => {},
    } = props;

    const log = buildLogger("PathSegmentsConfiguration");

    log("Rendering with props", props);

    const { t } = useTranslation();
    const forceUpdate = useUpdate();
    const { createPortal } = useBakabaseContext();

    const [value, setValue] = useState<IPscPropertyMatcherValue[]>(
      defaultValue ?? [],
    );
    const valueRef = useRef(value);
    const visibleMatchers = matchers
      .map((a) => allMatchers.find((b) => b.propertyType == a.propertyType)!)
      .filter((x) => x)
      .sort((a, b) => a.checkOrder - b.checkOrder);
    const configurableMatchers = matchers
      .filter((a) => !a.readonly)
      .map(
        (a) => visibleMatchers.find((b) => b.propertyType == a.propertyType)!,
      );

    const [customPropertyNameMap, setCustomPropertyNameMap] = useState<
      Record<number, string>
    >({});

    useEffect(() => {
      valueRef.current = value;
      log("Changed:", JSON.parse(JSON.stringify(value)));
      onChange(value);
    }, [value]);

    useUpdateEffect(() => {
      value.forEach((v) => {
        if (v.property.isCustom) {
          v.property.name ??= customPropertyNameMap[v.property.id];
        }
      });
      forceUpdate();
    }, [customPropertyNameMap]);

    useEffect(() => {
      BApi.customProperty.getAllCustomProperties().then((x) => {
        const data = x.data ?? [];
        const pnMap = data.reduce<Record<number, string>>((s, t) => {
          s[t.id!] = t.name!;

          return s;
        }, {});

        setCustomPropertyNameMap(pnMap);
      });
    }, []);

    useImperativeHandle(
      ref,
      (): IPathSegmentConfigurationRef => ({
        get context(): PscContext {
          return BuildPscContext(
            segments,
            value,
            visibleMatchers,
            configurableMatchers,
            t,
            log,
          );
        },
      }),
      [segments, value, visibleMatchers, configurableMatchers],
    );

    const onDeleteMatcherValue = (
      property: PscProperty,
      valueIndex: number | undefined = 0,
    ) => {
      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>("Sure to delete this property?"),
        onOk: () => {
          const bad = value?.filter((v) => v.property.equals(property))?.[
            valueIndex ?? 0
          ];

          // console.log(bad, value.filter(v => v != bad), property);
          setValue(value.filter((v) => v != bad));
        },
      });
    };

    const ctx = BuildPscContext(
      segments,
      value,
      visibleMatchers,
      configurableMatchers,
      t,
      log,
    );

    return (
      <div className={"path-segments-configuration flex flex-col gap-1"}>
        <Tips />
        <Errors
          errors={ctx.globalErrors}
          value={value}
          onDeleteMatcherValue={onDeleteMatcherValue}
        />
        <Segments
          isDirectory={isDirectory}
          segments={ctx.segments}
          value={value}
          visibleMatchers={visibleMatchers}
          onChange={setValue}
          onDeleteMatcherValue={onDeleteMatcherValue}
        />
        <FileExtensionLoader
          isDirectory={isDirectory}
          matchers={matchers}
          segments={segments}
          value={value}
          onChange={(v) => setValue(v)}
        />
        <GlobalMatches
          matches={ctx.globalMatches}
          value={value}
          onDeleteMatcherValue={onDeleteMatcherValue}
        />
        <BottomOperations hasError={ctx.preventSubmitting} value={value} />
      </div>
    );
  },
);
const PathSegmentsConfiguration = React.memo(PathSegmentsConfigurationInner);

PathSegmentsConfiguration.displayName = "PathSegmentsConfiguration";

export default PathSegmentsConfiguration;
