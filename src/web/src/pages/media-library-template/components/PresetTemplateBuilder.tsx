"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";
import type { PresetProperty } from "@/sdk/constants";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import _ from "lodash";
import {
  AiOutlineDelete,
  AiOutlineDoubleRight,
  AiOutlineLike,
  AiOutlinePlusCircle,
} from "react-icons/ai";
import { QuestionCircleOutlined } from "@ant-design/icons";

import {
  Button,
  Chip,
  Divider,
  Input,
  Modal,
  Select,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { PropertyPool } from "@/sdk/constants";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import { EnhancerIcon } from "@/components/Enhancer";

type DataPool =
  components["schemas"]["Bakabase.Modules.Presets.Abstractions.Models.MediaLibraryTemplatePresetDataPool"];

type Props = {
  onSubmitted?: (id: number) => any;
} & DestroyableProps;

type Form = Omit<components["schemas"]["Bakabase.Modules.Presets.Abstractions.Models.MediaLibraryTemplateCompactBuilder"], 'layeredProperties'> & {layeredProperties?: (PresetProperty|undefined)[]};

const validate = (form: Partial<Form>): Form | undefined => {
  if (!form.name || form.name.length == 0) {
    return;
  }
  if (!form.resourceType || !form.resourceLayer) {
    return;
  }

  return form as Form;
};
const PresetTemplateBuilder = ({ onDestroyed, onSubmitted }: Props) => {
  const { t } = useTranslation();
  const [dataPool, setDataPool] = useState<DataPool>();

  const [form, setForm] = useState<Partial<Form>>({});

  useEffect(() => {
    BApi.mediaLibraryTemplate
      .getMediaLibraryTemplatePresetDataPool()
      .then((r) => {
        setDataPool(r.data);
      });
  }, []);

  const propertyMap = _.keyBy(dataPool?.properties ?? [], (x) => x.id);
  const resourceTypeGroups = _.groupBy(dataPool?.resourceTypes || [], (t) =>
    Math.floor(t.type / 1000),
  );

  const recommendProperties =
    dataPool?.resourceTypePresetPropertyIds[form.resourceType ?? 0] || [];
  const recommendEnhancerIds =
    dataPool?.resourceTypeEnhancerIds[form.resourceType ?? 0] || [];

  const validForm = validate(form);

  console.log(form, validForm, recommendProperties, recommendEnhancerIds);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: !validForm,
        },
      }}
      size={"full"}
      title={
        <div className={"flex items-center gap-1"}>
          <div>{t<string>("Preset media library template builder")}</div>
        </div>
      }
      onDestroyed={onDestroyed}
      onOk={async () => {
        const r =
          await BApi.mediaLibraryTemplate.addMediaLibraryTemplateFromPresetBuilder(
            // @ts-ignore
            validForm!,
          );

        if (!r.code) {
          onSubmitted?.(r.data);
        }
      }}
    >
      {dataPool ? (
        <div
          className={"grid gap-x-4 gap-y-2 items-center"}
          style={{ gridTemplateColumns: "auto 1fr" }}
        >
          <div className={"text-right"}>{t<string>("Resource type")}</div>
          <div className={"flex flex-col gap-2"}>
            {_.keys(resourceTypeGroups).map((type) => {
              const resourceTypes = resourceTypeGroups[type]!;

              return (
                <div
                  className={"grid gap-2 items-center"}
                  style={{ gridTemplateColumns: "auto 1fr" }}
                >
                  <AiOutlineDoubleRight className={"text-base"} />
                  <div className={"flex items-center gap-1 flex-wrap"}>
                    {resourceTypes.map((tn) => (
                      <Button
                        color={
                          form.resourceType == tn.type ? "primary" : "default"
                        }
                        size={"sm"}
                        onPress={() => {
                          if (form.resourceType != tn.type) {
                            setForm({
                              name: form.name,
                              resourceType: tn.type,
                              properties:
                                dataPool.resourceTypePresetPropertyIds[
                                  tn.type
                                ] || [],
                              enhancerIds:
                                dataPool.resourceTypeEnhancerIds[tn.type] || [],
                              resourceLayer: 1,
                            });
                          }
                        }}
                      >
                        {tn.name}
                        {tn.description && (
                          <Tooltip content={tn.description}>
                            <QuestionCircleOutlined className={"text-base"} />
                          </Tooltip>
                        )}
                      </Button>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
          <div />
          <Divider />
          <div className={"text-right"}>{t<string>("Properties")}</div>
          <div>
            <div className={"flex items-center gap-1 flex-wrap"}>
              {dataPool.properties.map((p) => (
                <Button
                  color={
                    form.properties?.includes(p.id) ? "primary" : "default"
                  }
                  size={"sm"}
                  variant={
                    recommendProperties.includes(p.id) ? "solid" : "flat"
                  }
                  onPress={() => {
                    const newProperties = form.properties
                      ? [...form.properties]
                      : [];

                    if (newProperties.includes(p.id)) {
                      _.remove(newProperties, (t) => t === p.id);
                    } else {
                      newProperties.push(p.id);
                    }
                    setForm({
                      ...form,
                      properties: newProperties,
                    });
                  }}
                >
                  <BriefProperty
                    fields={["type", "name"]}
                    property={{
                      pool: PropertyPool.Custom,
                      name: p.name,
                      type: p.type,
                    }}
                  />
                  {recommendProperties.includes(p.id) && (
                    <AiOutlineLike className={"text-base"} />
                  )}
                </Button>
              ))}
            </div>
          </div>
          <div />
          <Divider />
          <div className={"flex items-center gap-1"}>
            {t<string>("Resource layer")}
            <Tooltip
              content={t<string>(
                "You can configure how many levels deep the resource path should be under the media library path, and you can assign the intermediate directories as property values of the resource",
              )}
            >
              <QuestionCircleOutlined className={"text-base"} />
            </Tooltip>
          </div>
          <div className={"flex flex-wrap items-center"}>
            <Chip color={"success"} variant={"light"}>
              {t<string>("Media library")}
            </Chip>
            <Chip color={"warning"} variant={"light"}>
              /
            </Chip>
            {form.resourceLayer && form.resourceLayer > 0 ? (
              <>
                {_.range(0, form.resourceLayer - 1).map((lp, idx) => {
                  return (
                    <div className={"flex items-center gap-1"}>
                      <Select
                        className={"min-w-[200px]"}
                        dataSource={form.properties?.map((p) => ({
                          label: propertyMap?.[p]!.name,
                          value: p,
                        }))}
                        label={t<string>("The {{layer}}th layer", {
                          layer: idx + 1,
                        })}
                        size={"sm"}
                        value={lp}
                        isClearable
                        placeholder={t<string>("Not configured for now")}
                        onSelectionChange={(keys) => {
                          const keyStr = Array.from(keys)[0] as string;
                          form.layeredProperties![idx] = keyStr
                            ? parseInt(keyStr, 10) as PresetProperty
                            : undefined!;
                          setForm({
                            ...form,
                            layeredProperties: form.layeredProperties!.slice(),
                          });
                        }}
                      />
                      <Button
                        isIconOnly
                        color={"danger"}
                        size={"sm"}
                        variant={"light"}
                        onPress={() => {
                          const lps = (form.layeredProperties ?? []).slice();

                          lps.splice(idx, 1);
                          setForm({
                            ...form,
                            layeredProperties: lps,
                            resourceLayer: lps.length + 1,
                          });
                        }}
                      >
                        <AiOutlineDelete className={"text-base"} />
                      </Button>
                      <Chip color={"warning"} variant={"light"}>
                        /
                      </Chip>
                    </div>
                  );
                })}
              </>
            ) : null}
            <Button
              isIconOnly
              color={"primary"}
              size={"sm"}
              variant={"light"}
              onPress={() => {
                const lps = form.layeredProperties ?? [];
                lps.push(undefined);
                setForm({
                  ...form,
                  layeredProperties: lps,
                  resourceLayer: lps.length + 1,
                });
              }}
            >
              <AiOutlinePlusCircle className={"text-base"} />
            </Button>
            <Chip color={"success"} variant={"light"}>
              {t<string>("Resource")}
            </Chip>
          </div>
          <div />
          <Divider />
          <div className={"text-right"}>{t<string>("Enhancers")}</div>
          <div>
            <div className={"flex items-center gap-1 flex-wrap"}>
              {dataPool.enhancers.map((e) => (
                <Button
                  color={
                    form.enhancerIds?.includes(e.id) ? "primary" : "default"
                  }
                  size={"sm"}
                  variant={
                    recommendEnhancerIds.includes(e.id) ? "solid" : "flat"
                  }
                  onPress={() => {
                    const newEnhancerIds = form.enhancerIds
                      ? [...form.enhancerIds]
                      : [];

                    if (newEnhancerIds.includes(e.id)) {
                      _.remove(newEnhancerIds, (p) => p === e.id);
                    } else {
                      newEnhancerIds.push(e.id);
                    }
                    setForm({
                      ...form,
                      enhancerIds: newEnhancerIds,
                    });
                  }}
                >
                  <EnhancerIcon id={e.id} />
                  {e.name}
                  {recommendEnhancerIds.includes(e.id) && (
                    <AiOutlineLike className={"text-base"} />
                  )}
                </Button>
              ))}
            </div>
          </div>
          <div className={"text-right"}>{t<string>("Name")}</div>
          <div>
            <Input
              isRequired
              className={"w-[320px]"}
              fullWidth={false}
              placeholder={t<string>("Set a name for this template")}
              value={form.name}
              onValueChange={(name) =>
                setForm({
                  ...form,
                  name,
                })
              }
            />
          </div>
        </div>
      ) : (
        <div className={"flex items-center justify-center gap-2 text-lg grow"}>
          <Spinner size={"md"} />
          {t<string>("Initializing")}
        </div>
      )}
    </Modal>
  );
};

PresetTemplateBuilder.displayName = "PresetTemplateBuilder";

export default PresetTemplateBuilder;
