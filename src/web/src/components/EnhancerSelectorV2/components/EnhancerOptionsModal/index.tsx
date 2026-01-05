"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useUpdate } from "react-use";
import _ from "lodash";

import DynamicTargets from "./components/DynamicTargets";
import FixedTargets from "./components/FixedTargets";

import { Button, Chip, Modal, Select, Switch, Textarea } from "@/components/bakaui";
import {
  EnhancerId,
  EnhancerTag,
  InternalProperty,
  PropertyPool,
  PropertyValueScope,
  propertyValueScopes,
  RegexEnhancerTarget,
} from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import PropertySelector from "@/components/PropertySelector";
import { findCapturingGroupsInRegex } from "@/components/utils";

const extractCaptureGroups = (expressions: string[]) =>
  expressions.reduce<string[]>((s, t) => {
    s.push(...findCapturingGroupsInRegex(t));

    return s;
  }, []);

type Props = {
  enhancer: EnhancerDescriptor;
  options?: EnhancerFullOptions;
  onSubmit?: (options: EnhancerFullOptions) => Promise<any>;
} & DestroyableProps;

export default function EnhancerOptionsModal({
  enhancer,
  options: propsOptions,
  onSubmit,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();
  const navigate = useNavigate();

  const [options, setOptions] = useState<EnhancerFullOptions>(propsOptions ?? {});
  const [propertyMap, setPropertyMap] = useState<{
    [key in PropertyPool]?: Record<number, IProperty>;
  }>({});
  const [allEnhancers, setAllEnhancers] = useState<EnhancerDescriptor[]>([]);

  const init = async () => {
    await Promise.all([loadAllProperties(), loadEnhancers()]);
  };

  useEffect(() => {
    init();
  }, []);

  const loadAllProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(
      _.groupBy(psr, (x) => x.pool),
      (v) => _.keyBy(v, (x) => x.id),
    );

    setPropertyMap(ps);
  };

  const loadEnhancers = async () => {
    const r = await BApi.enhancer.getAllEnhancerDescriptors();

    setAllEnhancers(r.data || []);
  };

  const renderKeywordProperty = () => {
    if (!enhancer.tags.includes(EnhancerTag.UseKeyword)) {
      return null;
    }

    if (!options.keywordProperty) {
      options.keywordProperty = {
        pool: PropertyPool.Internal,
        id: InternalProperty.Filename,
        scope: PropertyValueScope.Synchronization,
      };
    }

    const currentProperty =
      propertyMap?.[options.keywordProperty.pool]?.[options.keywordProperty.id];

    const keywordPropertyScope = options.keywordProperty?.scope;

    return (
      <>
        <div className="flex items-center gap-1">
          <div className=" w-[300px]">
            <div className="text-small">{t<string>("Property value used during enhancement")}</div>
            <div className="text-xs text-default-400">
              <div>
                {t<string>(
                  "Generally, this enhancer will use file/folder name of resource to perform enhancement and its not necessary to change this.",
                )}
              </div>
              <div>
                {t<string>(
                  "In some cases, you can change the property value used during enhancement to perform enhancement on other property values.",
                )}
              </div>
            </div>
          </div>
          <Button
            color={"primary"}
            size="sm"
            variant={"light"}
            onPress={async () => {
              createPortal(PropertySelector, {
                v2: true,
                pool: PropertyPool.All,
                multiple: false,
                isDisabled: (p) =>
                  p.pool == PropertyPool.Internal && p.id != InternalProperty.Filename,
                onSubmit: async (selection) => {
                  const p = selection[0];

                  if (p) {
                    setOptions({
                      ...options,
                      keywordProperty: { pool: p.pool, id: p.id, scope: PropertyValueScope.Manual },
                    });
                  }

                  return Promise.resolve();
                },
                v2: true,
              });
            }}
          >
            {currentProperty ? (
              <BriefProperty property={currentProperty} />
            ) : (
              t<string>("Select property")
            )}
          </Button>
          <Select
            className={"w-auto"}
            dataSource={propertyValueScopes.map((e) => ({
              label: `${t<string>(`PropertyValueScope.${e.label}`)}`,
              value: e.value.toString(),
            }))}
            description={
              <div>
                <div>
                  {t<string>(
                    "Property value scope usually means who the property value is generated by.",
                  )}
                </div>
                <div>
                  {t<string>("For internal options, property value scope will be ignored.")}
                </div>
              </div>
            }
            label={t<string>("Property value scope")}
            selectedKeys={
              keywordPropertyScope == undefined ? [] : [keywordPropertyScope.toString()]
            }
            size="sm"
            onSelectionChange={(keys) => {
              const arr = Array.from(keys);
              const vals = arr.map((o) => parseInt(o as string, 10) as PropertyValueScope);

              const selected = vals[0];

              if (selected === PropertyValueScope.Manual) {
                createPortal(Modal, {
                  defaultVisible: true,
                  title: t<string>("Manual property value scope has been selected"),
                  children: (
                    <div>
                      {t<string>(
                        "Manual property value scope means the property values are generated by yourself, and these values are typically not yet available at the time of enhancement.",
                      )}
                    </div>
                  ),
                  footer: {
                    actions: ["cancel"],
                    cancelProps: {
                      children: t<string>("I've understood"),
                    },
                  },
                });
              }

              setOptions({
                ...options,
                keywordProperty: { ...options.keywordProperty!, scope: vals[0] },
              });
            }}
          />
        </div>
        <div className="flex items-center gap-1">
          <div className=" w-[300px]">
            <div className="text-small">{t<string>("Pretreat property value")}</div>
            <div className="text-xs text-default-400">
              <div>
                {t<string>("If checked, the property value will be pretreated before enhancement.")}
              </div>
              <div>
                {t<string>(
                  "You can configure the special texts to be pretreated in special text page under data menu.",
                )}
                <Button
                  color="primary"
                  size="sm"
                  variant="light"
                  onPress={() => {
                    // console.log(createPortal)
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("About to leave current page"),
                      children: t<string>("Sure?"),
                      onOk: async () => {
                        navigate("/text");
                      },
                    });
                  }}
                >
                  {t<string>("Click to check special texts")}
                </Button>
              </div>
            </div>
          </div>
          <Switch
            isSelected={options.pretreatKeyword ?? false}
            size="sm"
            onValueChange={(v) => {
              setOptions({ ...options, pretreatKeyword: v });
            }}
          />
        </div>
      </>
    );
  };

  const expressions = options?.expressions || [];
  const captureGroups = extractCaptureGroups(expressions);
  const prerequisiteEnhancers = options.requirements?.map((r) => r.toString());
  let candidateTargetsMap: Record<number, string[] | undefined> = {};

  if (enhancer.id == EnhancerId.Regex) {
    candidateTargetsMap[RegexEnhancerTarget.CaptureGroups] = captureGroups;
  }

  const hasDynamicTargets = enhancer.targets?.some((t) => t.isDynamic);
  const hasFixedTargets = enhancer.targets?.some((t) => !t.isDynamic);
  // console.log('prerequisite enhancers', options.requirements, options.requirements?.map((r) => r.toString()));

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
      }}
      size={"xl"}
      title={
        <div className={"flex items-center gap-x-2"}>
          {t<string>("Configure enhancer")}
          <BriefEnhancer enhancer={enhancer} />
        </div>
      }
      onDestroyed={onDestroyed}
      onOk={async () => {
        await onSubmit?.(options);
      }}
    >
      <div className={"flex flex-col gap-y-2"}>
        <Select
          isClearable
          dataSource={allEnhancers.map((e) => ({ label: e.name, value: e.id.toString() }))}
          description={
            <div>
              <div>
                {t<string>(
                  "You can perform chained enhancement by customizing the execution order of the enhancers.",
                )}
              </div>
              <div>{t<string>("Generally, you can just leave it as empty.")}</div>
            </div>
          }
          label={t<string>("Run after enhancers")}
          selectedKeys={
            prerequisiteEnhancers && prerequisiteEnhancers.length > 0
              ? prerequisiteEnhancers
              : undefined
          }
          selectionMode="multiple"
          size="sm"
          onSelectionChange={(keys) => {
            const arr = Array.from(keys);
            const vals = arr.map((o) => parseInt(o as string, 10) as EnhancerId);

            setOptions({ ...options, requirements: vals });
          }}
        />

        {renderKeywordProperty()}

        {enhancer.tags.includes(EnhancerTag.UseRegex) && (
          <div>
            <Textarea
              description={
                <div>
                  {captureGroups.length > 0 ? (
                    <div>
                      {t<string>("Available capture groups:")}
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
                        "No named capture groups were found, so the enhancement will not take effect.",
                      )}
                    </div>
                  )}
                  <div>
                    {t<string>(
                      "You can set multiple regex expressions(separated by new line) to match the file or folder name of each resource.",
                    )}
                  </div>
                  <div>
                    {t<string>(
                      "Text matched by multiple capture groups with the same name will be merged into a list and deduplicated.",
                    )}
                  </div>
                  <div>
                    {t<string>(
                      "After setting regex expressions, you must go to category page to configure regex enhancer for each category.",
                    )}
                  </div>
                  <div>
                    {t<string>(
                      "You need to use the same name(index-based group name will be ignored) as the capture group for the dynamic enhancement target, otherwise the resource may not be enhanced.",
                    )}
                  </div>
                </div>
              }
              label={t<string>("Regex expressions")}
              maxRows={10}
              minRows={3}
              value={options?.expressions?.join("\n")}
              onValueChange={(v) => {
                setOptions({ ...options, expressions: v.split("\n") });
              }}
            />
          </div>
        )}
      </div>

      <div>
        <div className="text-base">
          {t<string>("Enhance properties")}
          <div className="text-xs text-default-400">
            <div>{t<string>("Please select at least one property to enhance.")}</div>
          </div>
        </div>
        {options && (
          <div className={"flex flex-col gap-y-4"}>
            {hasFixedTargets && (
              <FixedTargets
                enhancer={enhancer}
                optionsList={options.targetOptions}
                propertyMap={propertyMap}
                onChange={(list) => {
                  setOptions({
                    ...options,
                    targetOptions: list,
                  });
                }}
                onPropertyChanged={loadAllProperties}
              />
            )}
            {hasDynamicTargets && (
              <DynamicTargets
                candidateTargetsMap={candidateTargetsMap}
                enhancer={enhancer}
                optionsList={options.targetOptions}
                propertyMap={propertyMap}
                onChange={(list) => {
                  setOptions({
                    ...options,
                    targetOptions: list,
                  });
                }}
                onPropertyChanged={loadAllProperties}
              />
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}
