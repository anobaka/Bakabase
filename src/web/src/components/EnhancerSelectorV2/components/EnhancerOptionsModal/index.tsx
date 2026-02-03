"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BangumiSubjectType } from "@/sdk/constants";

import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useUpdate } from "react-use";
import _ from "lodash";

import DynamicTargets from "./components/DynamicTargets";
import FixedTargets from "./components/FixedTargets";

import { Button, Chip, Modal, Select, Switch, Textarea } from "@/components/bakaui";
import {
  bangumiSubjectTypes,
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
            <div className="text-small">{t<string>("enhancer.options.keywordProperty.label")}</div>
            <div className="text-xs text-default-400">
              <div>
                {t<string>(
                  "enhancer.options.keywordProperty.description",
                )}
              </div>
              <div>
                {t<string>(
                  "enhancer.options.keywordProperty.tip",
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
              });
            }}
          >
            {currentProperty ? (
              <BriefProperty property={currentProperty} />
            ) : (
              t<string>("enhancer.options.selectProperty.action")
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
                    "enhancer.options.propertyValueScope.description",
                  )}
                </div>
                <div>
                  {t<string>("enhancer.options.propertyValueScope.tip")}
                </div>
              </div>
            }
            label={t<string>("enhancer.options.propertyValueScope.label")}
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
                  title: t<string>("enhancer.options.manualScope.title"),
                  children: (
                    <div>
                      {t<string>(
                        "enhancer.options.manualScope.description",
                      )}
                    </div>
                  ),
                  footer: {
                    actions: ["cancel"],
                    cancelProps: {
                      children: t<string>("enhancer.options.manualScope.confirm"),
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
            <div className="text-small">{t<string>("enhancer.options.pretreat.label")}</div>
            <div className="text-xs text-default-400">
              <div>
                {t<string>("enhancer.options.pretreat.description")}
              </div>
              <div>
                {t<string>(
                  "enhancer.options.pretreat.tip",
                )}
                <Button
                  color="primary"
                  size="sm"
                  variant="light"
                  onPress={() => {
                    // console.log(createPortal)
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("common.tip.leavePageWarning"),
                      children: t<string>("common.confirm.short"),
                      onOk: async () => {
                        navigate("/text");
                      },
                    });
                  }}
                >
                  {t<string>("enhancer.options.pretreat.action")}
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

  const renderBangumiPrioritySubjectType = () => {
    if (enhancer.id !== EnhancerId.Bangumi) {
      return null;
    }

    return (
      <Select
        isClearable
        dataSource={bangumiSubjectTypes.map((e) => ({
          label: t<string>(`BangumiSubjectType.${e.label}`),
          value: e.value.toString(),
        }))}
        description={
          <div>
            <div>{t<string>("enhancer.bangumi.prioritySubjectType.description")}</div>
            <div>{t<string>("enhancer.bangumi.prioritySubjectType.hint")}</div>
          </div>
        }
        label={t<string>("enhancer.bangumi.prioritySubjectType.label")}
        selectedKeys={
          options.bangumiPrioritySubjectType != null
            ? [options.bangumiPrioritySubjectType.toString()]
            : []
        }
        size="sm"
        onSelectionChange={(keys) => {
          const arr = Array.from(keys);

          if (arr.length === 0) {
            setOptions({ ...options, bangumiPrioritySubjectType: undefined });
          } else {
            const val = parseInt(arr[0] as string, 10) as BangumiSubjectType;

            setOptions({ ...options, bangumiPrioritySubjectType: val });
          }
        }}
      />
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
          {t<string>("enhancer.options.configure.title")}
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
                  "enhancer.options.runAfter.description",
                )}
              </div>
              <div>{t<string>("enhancer.options.runAfter.tip")}</div>
            </div>
          }
          label={t<string>("enhancer.options.runAfter.label")}
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

        {renderBangumiPrioritySubjectType()}

        {enhancer.tags.includes(EnhancerTag.UseRegex) && (
          <div>
            <Textarea
              description={
                <div>
                  {captureGroups.length > 0 ? (
                    <div>
                      {t<string>("enhancer.regex.captureGroups.label")}
                      {captureGroups.map((g) => {
                        return (
                          <Chip size={"sm"} key={g} variant={"light"}>
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
          {t<string>("enhancer.options.enhanceProperties.label")}
          <div className="text-xs text-default-400">
            <div>{t<string>("enhancer.options.enhanceProperties.tip")}</div>
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
