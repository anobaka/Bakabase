"use client";

import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { IProperty } from "@/components/Property/models";

import React, { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { useUpdate, useUpdateEffect } from "react-use";

import { buildLogger, findCapturingGroupsInRegex } from "@/components/utils";
import { Chip, Textarea } from "@/components/bakaui";
import { useEnhancerOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import { type PropertyPool, RegexEnhancerTarget } from "@/sdk/constants";
import DynamicTargets from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/components/DynamicTargets";

const log = buildLogger("RegexEnhancerOptions");

type Props = {
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  options?: EnhancerFullOptions;
  category: { name: string; id: number; customPropertyIds?: number[] };
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  onCategoryChanged?: () => any;
};

const extractCaptureGroups = (expressions: string[]) =>
  expressions.reduce<string[]>((s, t) => {
    s.push(...findCapturingGroupsInRegex(t));

    return s;
  }, []);
const RegexEnhancerOptions = ({
  options: propsOptions,
  enhancer,
  propertyMap,
  category,
  onPropertyChanged,
  onCategoryChanged,
}: Props) => {
  const { t } = useTranslation();
  const enhancerOptions = useEnhancerOptionsStore((state) => state.data);
  const [tmpEnhancerOptions, setTmpEnhancerOptions] = useState(
    enhancerOptions || {},
  );
  const tmpEnhancerOptionsRef = useRef(tmpEnhancerOptions);
  const [options, setOptions] = useState(propsOptions);
  const optionsRef = useRef(options);
  const forceUpdate = useUpdate();

  useUpdateEffect(() => {
    optionsRef.current = propsOptions;
    addCaptureGroupsToOptions();
  }, [propsOptions]);

  useUpdateEffect(() => {
    optionsRef.current = options;
  }, [options]);

  useEffect(() => {
    log("new enhancer options", enhancerOptions);
    setTmpEnhancerOptions(JSON.parse(JSON.stringify(enhancerOptions || {})));
  }, [enhancerOptions]);

  useUpdateEffect(() => {
    tmpEnhancerOptionsRef.current = tmpEnhancerOptions;
    addCaptureGroupsToOptions();
  }, [tmpEnhancerOptions]);

  const addCaptureGroupsToOptions = () => {
    const newOptions: EnhancerFullOptions = {};
    const expressions =
      tmpEnhancerOptionsRef.current?.regexEnhancer?.expressions || [];
    const captureGroups = extractCaptureGroups(expressions);

    log(captureGroups, optionsRef.current?.targetOptions);

    if (optionsRef.current?.targetOptions?.length) {
      newOptions.targetOptions = optionsRef.current.targetOptions.filter(
        (x) =>
          x.dynamicTarget == undefined ||
          captureGroups.includes(x.dynamicTarget),
      );
    }

    for (const cg of captureGroups) {
      newOptions.targetOptions ??= [];
      if (
        !optionsRef.current?.targetOptions?.find(
          (x) =>
            x.target == RegexEnhancerTarget.CaptureGroups &&
            x.dynamicTarget == cg,
        )
      ) {
        newOptions.targetOptions.push({
          target: RegexEnhancerTarget.CaptureGroups,
          dynamicTarget: cg,
        });
      }
    }
    log(newOptions);
    setOptions({ ...newOptions });
  };

  const expressions = tmpEnhancerOptions?.regexEnhancer?.expressions || [];
  const captureGroups = extractCaptureGroups(expressions);

  return (
    <>
      <div>
        <Textarea
          description={
            <div>
              {captureGroups.length > 0 ? (
                <div>
                  {t<string>("enhancer.regex.captureGroups.label")}
                  {captureGroups.map((g) => {
                    return (
                      <Chip size={"sm"} variant={"light"}>
                        {g}
                      </Chip>
                    );
                  })}
                </div>
              ) : (
                <div>
                  {t<string>(
                    "enhancer.regex.captureGroups.warning",
                  )}
                </div>
              )}
              <div>
                {t<string>(
                  "enhancer.regex.expressions.description",
                )}
              </div>
              <div>
                {t<string>(
                  "enhancer.regex.captureGroups.mergeDescription",
                )}
              </div>
              <div>
                {t<string>(
                  "enhancer.regex.expressions.categoryTip",
                )}
              </div>
              <div>
                {t<string>(
                  "enhancer.regex.expressions.namingTip",
                )}
              </div>
            </div>
          }
          label={t<string>("enhancer.regex.expressions.label")}
          maxRows={10}
          minRows={3}
          value={tmpEnhancerOptions?.regexEnhancer?.expressions?.join("\n")}
          onBlur={() => {
            BApi.options.patchEnhancerOptions(tmpEnhancerOptions).then((r) => {
              if (!r.code) {
                toast.success(t<string>("common.success.saved"));
              }
            });
          }}
          onValueChange={(v) => {
            setTmpEnhancerOptions({
              ...tmpEnhancerOptions,
              regexEnhancer: {
                ...(tmpEnhancerOptions?.regexEnhancer || {}),
                expressions: v.split("\n"),
              },
            });
          }}
        />
      </div>
      {options && (
        <div className={"flex flex-col gap-y-4"}>
          <DynamicTargets
            category={category}
            enhancer={enhancer}
            options={options}
            propertyMap={propertyMap}
            onCategoryChanged={onCategoryChanged}
            onPropertyChanged={onPropertyChanged}
          />
        </div>
      )}
    </>
  );
};

RegexEnhancerOptions.displayName = "RegexEnhancerOptions";

export default RegexEnhancerOptions;
