"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { ExtensionGroupPage } from "@/pages/extension-group";
import type { PropertyType } from "@/sdk/constants";
import type { components } from "@/sdk/BApi2";
import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useState } from "react";
import toast from "react-hot-toast";
import { AiOutlineCheckCircle, AiOutlineCloseCircle } from "react-icons/ai";
import { IoSync } from "react-icons/io5";
import { TbSectionSign } from "react-icons/tb";
import { TiChevronRightOutline } from "react-icons/ti";
import { MdAutoFixHigh } from "react-icons/md";
import _ from "lodash";

import {
  Alert,
  Button,
  Chip,
  Modal,
  Select,
  Textarea,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { PropertyPool } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyMatcher from "@/components/PropertyMatcher";
import PropertiesMatcher from "@/components/PropertiesMatcher";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

type Props = {
  onImported?: () => any;
} & DestroyableProps;

type PropertyConversion = {
  toPropertyId: number;
  toPropertyPool: PropertyPool;
  toProperty?: IProperty;
};

type ExtensionGroupConversion = {
  toExtensionGroupId: number;
};

type Configuration =
  components["schemas"]["Bakabase.Abstractions.Models.View.MediaLibraryTemplateImportConfigurationViewModel"];
type SimpleProperty = {
  pool: PropertyPool;
  id: number;
  name: string;
  type: PropertyType;
};
type SimplePropertyMap = {
  [key in PropertyPool]?: Record<number, SimpleProperty>;
};

const MissingDataMessage =
  "The current template contains data missing from the application. Please configure how to handle this data before proceeding with the import.";

const findProperExtensionGroup = (
  extensionGroup: ExtensionGroup,
  extensionGroups: ExtensionGroup[],
): ExtensionGroup | undefined => {
  const candidates = extensionGroups.filter(
    (eg) => _.xor(extensionGroup.extensions, eg.extensions).length === 0,
  );
  const best = candidates.find((eg) => eg.name === extensionGroup.name);

  return best ?? candidates[0];
};

const validate = (
  propertyConversionMap?: Record<number, PropertyConversion>,
  extensionGroupConversionMap?: Record<number, ExtensionGroupConversion>,
  configuration?: Configuration,
): boolean => {
  if (!configuration || !configuration.noNeedToConfigure) {
    return true;
  }
  if (
    configuration.uniqueExtensionGroups &&
    configuration.uniqueExtensionGroups.length >
      _.keys(extensionGroupConversionMap).length
  ) {
    return false;
  }
  if (
    configuration.uniqueCustomProperties &&
    configuration.uniqueCustomProperties.length >
      _.keys(propertyConversionMap).length
  ) {
    return false;
  }

  return true;
};
const ImportModal = ({ onImported }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [shareCode, setShareCode] = useState<string>();
  const [configuration, setConfiguration] = useState<Configuration>();

  const [propertyConversionsMap, setPropertyConversionsMap] =
    useState<Record<number, PropertyConversion>>();
  const [extensionGroupConversionsMap, setExtensionGroupConversionsMap] =
    useState<Record<number, ExtensionGroupConversion>>();

  const [propertyMap, setPropertyMap] = useState<SimplePropertyMap | undefined>(
    undefined,
  );
  const [extensionGroups, setExtensionGroups] = useState<
    ExtensionGroup[] | undefined
  >(undefined);

  const initDataForImport = async () => {
    if (!propertyMap) {
      // @ts-ignore
      const pr = await BApi.property.getPropertiesByPool(
        PropertyPool.Custom | PropertyPool.Reserved,
      );
      const pm = (pr.data ?? []).reduce<SimplePropertyMap>((s, t) => {
        const m = (s[t.pool] ??= {});

        m[t.id] = t;

        return s;
      }, {});

      setPropertyMap(pm);
    }
    if (!extensionGroups) {
      const eg = await BApi.extensionGroup.getAllExtensionGroups();

      setExtensionGroups(eg.data ?? []);
    }
  };

  const $import = async () => {
    const model = {
      shareCode: shareCode!,
      customPropertyConversionsMap: propertyConversionsMap,
      extensionGroupConversionsMap,
      automaticallyCreateMissingData: false,
    };

    return await BApi.mediaLibraryTemplate.importMediaLibraryTemplate(model);
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled:
            !shareCode &&
            !validate(
              propertyConversionsMap,
              extensionGroupConversionsMap,
              configuration,
            ),
          children: t<string>("Import"),
        },
      }}
      size={"lg"}
      title={t<string>("Import media library template")}
      onOk={async () => {
        if (!configuration) {
          const importConfigurationResp =
            await BApi.mediaLibraryTemplate.getMediaLibraryTemplateImportConfiguration(
              shareCode!,
            );

          if (importConfigurationResp.code) {
            const msg = `${t<string>("Failed to get media library template import configuration")}:${importConfigurationResp.message}`;

            toast.error(msg);
            throw new Error(msg);
          } else {
            setConfiguration(importConfigurationResp.data!);
            if (!importConfigurationResp.data!.noNeedToConfigure) {
              await initDataForImport();
              throw new Error(t<string>(MissingDataMessage));
            }
          }
        }
        const importResult = await $import();

        if (importResult.code) {
          const msg = `${t<string>("Failed to import media library template")}:${importResult.message}`;

          toast.error(msg);
          throw new Error(msg);
        } else {
          onImported?.();
        }
      }}
    >
      <div>
        <Textarea
          isRequired
          isDisabled={!!configuration}
          label={t<string>("Share code")}
          placeholder={t<string>("Paste share code here")}
          value={shareCode}
          onValueChange={setShareCode}
        />
        {configuration && (
          <Button
            className={"mt-2"}
            color={"default"}
            size={"sm"}
            onPress={() => {
              setShareCode("");
              setConfiguration(undefined);
            }}
          >
            <IoSync />
            {t<string>("Change share code")}
          </Button>
        )}
      </div>
      {configuration && !configuration.noNeedToConfigure && (
        <div className={"flex flex-col gap-2"}>
          <div>
            <Alert color={"warning"} title={t<string>(MissingDataMessage)} />
          </div>
          {configuration.uniqueCustomProperties &&
            configuration.uniqueCustomProperties.length > 0 && (
              <div className={"flex flex-col gap-2"}>
                <div className={"flex items-center gap-1 text-lg font-bold"}>
                  <TbSectionSign className={""} />
                  {t<string>("New properties")}
                  <PropertiesMatcher
                    properties={configuration.uniqueCustomProperties.map(
                      (p) => ({ type: p.type, name: p.name }),
                    )}
                    onValueChanged={(ps) => {
                      const pcm = propertyConversionsMap ?? {};

                      for (
                        let i = 0;
                        i < configuration.uniqueCustomProperties!.length;
                        i++
                      ) {
                        const p = ps[i];

                        if (p) {
                          pcm[i] = {
                            toPropertyId: p.id,
                            toPropertyPool: p.pool,
                            toProperty: p,
                          };
                        }
                      }
                      setPropertyConversionsMap(pcm);
                    }}
                  />
                </div>
                <div>
                  <div
                    className={"inline-grid gap-1 items-center"}
                    style={{ gridTemplateColumns: "auto auto auto auto" }}
                  >
                    {configuration.uniqueCustomProperties.map((p, pIdx) => {
                      const conversion = propertyConversionsMap?.[pIdx];
                      const property =
                        conversion?.toPropertyPool && conversion?.toPropertyId
                          ? propertyMap?.[conversion.toPropertyPool]?.[
                              conversion.toPropertyId
                            ]
                          : undefined;
                      const isSet = !!property;

                      return (
                        <>
                          <div>
                            <Chip
                              color={isSet ? "success" : "danger"}
                              size={"sm"}
                              variant={"light"}
                            >
                              {isSet ? (
                                <AiOutlineCheckCircle className={"text-base"} />
                              ) : (
                                <AiOutlineCloseCircle className={"text-base"} />
                              )}
                            </Chip>
                          </div>
                          <div className={"flex items-center gap-1"}>
                            <BriefProperty
                              fields={["name", "type"]}
                              property={p}
                            />
                          </div>
                          <TiChevronRightOutline className={"text-base"} />
                          <PropertyMatcher
                            matchedProperty={
                              propertyConversionsMap?.[pIdx]?.toProperty
                            }
                            name={p.name}
                            options={p.options}
                            type={p.type}
                            onValueChanged={(property) => {
                              if (!property) {
                                throw new Error(
                                  "Property should not be undefined",
                                );
                              }
                              setPropertyMap({
                                ...propertyMap,
                                [property.pool]: {
                                  ...propertyMap![property.pool],
                                  [property.id]: property,
                                },
                              });
                              setPropertyConversionsMap({
                                ...propertyConversionsMap,
                                [pIdx]: {
                                  toPropertyPool: property.pool,
                                  toPropertyId: property.id,
                                },
                              });
                            }}
                          />
                        </>
                      );
                    })}
                  </div>
                </div>
              </div>
            )}
          {configuration.uniqueExtensionGroups &&
            configuration.uniqueExtensionGroups.length > 0 && (
              <div className={"flex flex-col gap-2"}>
                <div className={"flex items-center gap-1 text-lg font-bold"}>
                  <TbSectionSign className={""} />
                  {t<string>("New extension groups")}
                </div>
                <div>
                  <div
                    className={"inline-grid gap-1 items-center"}
                    style={{ gridTemplateColumns: "auto auto auto auto auto" }}
                  >
                    {configuration.uniqueExtensionGroups.map((eg, egIdx) => {
                      const conversion = extensionGroupConversionsMap?.[egIdx];
                      const leg = conversion?.toExtensionGroupId
                        ? extensionGroups?.find(
                            (g) => g.id == conversion.toExtensionGroupId,
                          )
                        : undefined;
                      const isSet = !!leg;

                      // console.log(conversion, leg, isSet);
                      return (
                        <>
                          <div>
                            <Chip
                              color={isSet ? "success" : "danger"}
                              size={"sm"}
                              variant={"light"}
                            >
                              {isSet ? (
                                <AiOutlineCheckCircle className={"text-base"} />
                              ) : (
                                <AiOutlineCloseCircle className={"text-base"} />
                              )}
                            </Chip>
                          </div>
                          <div className={"flex items-center flex-wrap gap-1"}>
                            {eg.name}
                            {eg.extensions?.map((e) => {
                              return (
                                <Chip
                                  radius={"sm"}
                                  size={"sm"}
                                  variant={"flat"}
                                >
                                  {e}
                                </Chip>
                              );
                            })}
                          </div>
                          <TiChevronRightOutline className={"text-base"} />
                          <Tooltip content={t<string>("Automatically process")}>
                            <Button
                              isIconOnly
                              color={"primary"}
                              variant={"light"}
                              onPress={async () => {
                                const candidate = findProperExtensionGroup(
                                  eg,
                                  extensionGroups ?? [],
                                );

                                if (candidate) {
                                  setExtensionGroupConversionsMap({
                                    ...extensionGroupConversionsMap,
                                    [egIdx]: {
                                      toExtensionGroupId: candidate.id,
                                    },
                                  });
                                } else {
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t<string>(
                                      "No proper extension group found",
                                    ),
                                    children: t<string>(
                                      "Should we create a new extension group for this?",
                                    ),
                                    onOk: async () => {
                                      const r =
                                        await BApi.extensionGroup.addExtensionGroup(
                                          {
                                            name: eg.name,
                                            extensions: eg.extensions,
                                          },
                                        );

                                      if (r.code) {
                                        throw new Error(r.message);
                                      } else {
                                        const newEg = r.data!;

                                        setExtensionGroups([
                                          ...extensionGroups!,
                                          newEg,
                                        ]);
                                        setExtensionGroupConversionsMap({
                                          ...extensionGroupConversionsMap,
                                          [egIdx]: {
                                            toExtensionGroupId: newEg.id,
                                          },
                                        });
                                      }
                                    },
                                  });
                                }
                              }}
                            >
                              <MdAutoFixHigh className={"text-base"} />
                            </Button>
                          </Tooltip>
                          <div className={"min-w-[240px]"}>
                            <Select
                              fullWidth
                              isMultiline
                              dataSource={extensionGroups?.map((x) => ({
                                label: (
                                  <div className={"flex flex-col gap-1"}>
                                    <div>{x.name}</div>
                                    {x.extensions &&
                                      x.extensions.length > 0 && (
                                        <div
                                          className={
                                            "flex items-center gap-1 flex-wrap"
                                          }
                                        >
                                          {x.extensions.map((e) => {
                                            return (
                                              <Chip
                                                radius={"sm"}
                                                size={"sm"}
                                                variant={"flat"}
                                              >
                                                {e}
                                              </Chip>
                                            );
                                          })}
                                        </div>
                                      )}
                                  </div>
                                ),
                                textValue: x.name,
                                value: x.id.toString(),
                              }))}
                              multiple={false}
                              placeholder={t<string>(
                                "Select an extension group manually",
                              )}
                              selectedKeys={
                                conversion?.toExtensionGroupId
                                  ? [conversion.toExtensionGroupId.toString()]
                                  : undefined
                              }
                              size={"sm"}
                              variant={"bordered"}
                              onSelectionChange={(selection) => {
                                const id = parseInt(
                                  Array.from(selection)[0] as string,
                                  10,
                                );

                                setExtensionGroupConversionsMap({
                                  ...extensionGroupConversionsMap,
                                  [egIdx]: {
                                    ...conversion,
                                    toExtensionGroupId: id,
                                  },
                                });
                              }}
                            />
                          </div>
                        </>
                      );
                    })}
                  </div>
                </div>
              </div>
            )}
        </div>
      )}
    </Modal>
  );
};

ImportModal.displayName = "ImportModal";

export default ImportModal;
