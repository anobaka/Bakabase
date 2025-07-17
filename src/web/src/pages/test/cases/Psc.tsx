"use client";

import type { IPscPropertyMatcherValue } from "@/components/PathSegmentsConfiguration/models/PscPropertyMatcherValue";

import React, { useCallback, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import ReactJson from "react-json-view";

import { ResourceMatcherValueType } from "@/sdk/constants";
import PathSegmentsConfiguration, {
  PathSegmentConfigurationPropsMatcherOptions,
} from "@/components/PathSegmentsConfiguration";
import SegmentMatcherConfiguration, {
  SegmentMatcherConfigurationModesData,
} from "@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration";
import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  convertToPathConfigurationDtoFromPscValue,
  convertToPscValueFromPathConfigurationDto,
} from "@/components/PathSegmentsConfiguration/helpers";
import { PscPropertyType } from "@/components/PathSegmentsConfiguration/models/PscPropertyType";

const testData = {
  path: "D:/test",
  rpmValues: [
    {
      layer: 1,
      propertyId: 3,
      isReservedProperty: true,
      valueType: ResourceMatcherValueType.Layer,
    },
    // {
    //   layer: 3,
    //   propertyId: 3,
    //   isReservedProperty: true,
    //   valueType: ResourceMatcherValueType.Layer,
    // },
    // {
    //   regex: '^[^\\/]+\\/[^\\/]+\\/[^\\/]+\\/[^\\/]+$',
    //   propertyId: 2,
    //   isReservedProperty: true,
    //   valueType: ResourceMatcherValueType.Regex,
    // },
  ],
};

export default () => {
  const { t } = useTranslation();
  const [samplePath, setSamplePath] = useState(
    "D:\\test\\new-media-library-path-configuration\\a",
  );
  const [value, setValue] = useState<IPscPropertyMatcherValue[]>(
    convertToPscValueFromPathConfigurationDto(testData),
  );
  const segmentsRef = useRef(samplePath.split("\\"));
  const { createPortal } = useBakabaseContext();

  const simpleMatchers = {
    [PscPropertyType.Resource]: false,
    [PscPropertyType.RootPath]: false,
    [PscPropertyType.ParentResource]: false,
    [PscPropertyType.Rating]: false,
    [PscPropertyType.Introduction]: false,
    [PscPropertyType.CustomProperty]: false,
  };
  const matchers = Object.keys(simpleMatchers).reduce<
    PathSegmentConfigurationPropsMatcherOptions[]
  >((ts, t) => {
    ts.push(
      new PathSegmentConfigurationPropsMatcherOptions({
        propertyType: parseInt(t, 10),
        readonly: simpleMatchers[t],
      }),
    );

    return ts;
  }, []);

  console.log(matchers, value);

  const matchersRef = useRef(matchers);
  const onChangeCallback = useCallback((v) => {
    setValue(v);
  }, []);
  const valueRef = useRef(value);

  return (
    <div className={"test-page"}>
      <div className="psc">
        <PathSegmentsConfiguration
          defaultValue={valueRef.current}
          isDirectory={false}
          matchers={matchersRef.current}
          segments={segmentsRef.current}
          onChange={onChangeCallback}
        />
        <Button
          size={"sm"}
          onClick={() => {
            createPortal(SegmentMatcherConfiguration, {
              segments: samplePath.split("\\"),
              segmentMarkers: {},
              property: {
                id: 1,
                isCustom: false,
                name: t<string>("Resource"),
              },
              modesData: new SegmentMatcherConfigurationModesData({
                layers: [1],
                regex: {
                  text: "new-media-library-path-configuration/a/bc/New Text Document.txt",
                },
              }),
              onSubmit: (value) => {
                // selectMatcher(visibleMatchers.find(t => t.property == m.property)!, value);
              },
            });
          }}
        >
          Simple SMC
        </Button>
        <div className={"flex items-start"}>
          <div className={""}>
            <ReactJson name={"Raw"} src={value} theme={"monokai"} />
          </div>
          <div>
            <ReactJson
              name={"Dto"}
              src={convertToPathConfigurationDtoFromPscValue(value)}
              theme={"monokai"}
            />
          </div>
        </div>
        <div className="grid grid-cols-2">
          {value.map((m) => {
            return (
              <div className={""}>
                <ReactJson
                  name={m.property.toString(t, undefined)}
                  src={m.value}
                  theme={"monokai"}
                />
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
