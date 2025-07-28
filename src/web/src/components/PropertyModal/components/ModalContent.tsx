"use client";

import type { Key } from "@react-types/shared";

import { Radio, RadioGroup, TableHeader } from "@heroui/react";
import { useTranslation } from "react-i18next";
import { useRef, useState } from "react";
import AceEditor from "react-ace";
import "ace-builds/src-noconflict/mode-javascript";
import "ace-builds/src-noconflict/theme-monokai";
import "ace-builds/src-noconflict/ext-language_tools";
import { useUpdate } from "react-use";

import MultilevelData from "./MultilevelData";

import {
  Button,
  Chip,
  Input,
  Modal,
  Popover,
  Progress,
  Select,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableRow,
  Tooltip,
} from "@/components/bakaui";
import FeatureStatusTip from "@/components/FeatureStatusTip";
import { PropertyType } from "@/sdk/constants";
import {
  type ChoicePropertyOptions,
  type NumberPropertyOptions,
  type PercentagePropertyOptions,
  type RatingPropertyOptions,
  type TagsPropertyOptions,
} from "@/components/Property/models";
import ValueRenderer from "@/components/StandardValue/ValueRenderer";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import ChoiceList from "@/components/PropertyModal/components/ChoiceList";
import TagList from "@/components/PropertyModal/components/TagList";
import { optimizeOptions } from "@/components/PropertyModal/helpers";
import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const UnderDevelopmentGroupKey = "UnderDevelopment";

const PropertyTypeGroup: Record<string, PropertyType[]> = {
  Text: [
    PropertyType.SingleLineText,
    PropertyType.MultilineText,
    PropertyType.Link,
  ],
  Number: [PropertyType.Number, PropertyType.Percentage, PropertyType.Rating],
  Option: [
    PropertyType.SingleChoice,
    PropertyType.MultipleChoice,
    PropertyType.Multilevel,
  ],
  DateTime: [PropertyType.DateTime, PropertyType.Date, PropertyType.Time],
  Other: [PropertyType.Attachment, PropertyType.Boolean, PropertyType.Tags],
  [UnderDevelopmentGroupKey]: [PropertyType.Formula],
};

type Props = {
  value?: Partial<CustomPropertyForm>;
  validValueTypes?: PropertyType[];
  onChange?: (value?: CustomPropertyForm) => void;
};

type CustomPropertyForm = {
  id?: number;
  name: string;
  type: PropertyType;
  options?: any;
};

const NumberPrecisions = [0, 1, 2, 3, 4].map((x) => ({
  value: x,
  label: Number(1).toFixed(x),
}));

const RatingMaxValueDataSource = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((x) => ({
  value: x,
  label: x,
}));

const log = buildLogger("ModalContent");
const ModalContent = ({ validValueTypes, value, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [typeGroupsVisible, settypeGroupsVisible] = useState(false);
  const typePopoverDomRef = useRef<HTMLDivElement>(null);

  const [property, setProperty] = useState<CustomPropertyForm>(
    JSON.parse(
      JSON.stringify({
        ...(value || {}),
        options: optimizeOptions(value?.options),
      }),
    ),
  );

  // log(property);

  const checkValueUsage = property.id
    ? async (value: string) => {
        const rsp = await BApi.customProperty.getCustomPropertyValueUsage(
          property.id!,
          { value: value },
        );

        return rsp.data!;
      }
    : undefined;

  const renderOptions = () => {
    if (property.type != undefined) {
      switch (property.type) {
        case PropertyType.SingleLineText:
        case PropertyType.MultilineText:
          break;
        case PropertyType.SingleChoice:
        case PropertyType.MultipleChoice: {
          const options = property.options as ChoicePropertyOptions;
          const multiple = property.type === PropertyType.MultipleChoice;

          // console.log(options);
          return (
            <>
              <ChoiceList
                checkUsage={checkValueUsage}
                choices={options?.choices}
                className={"mt-4"}
                onChange={(choices) => {
                  patchProperty({
                    options: {
                      ...options,
                      choices,
                    },
                  });
                }}
              />
              {/* <Switch */}
              {/*   className={'mt-4'} */}
              {/*   size={'sm'} */}
              {/*   isSelected={options?.allowAddingNewDataDynamically} */}
              {/*   onValueChange={c => { */}
              {/*     patchProperty({ */}
              {/*       options: { */}
              {/*         ...options, */}
              {/*         allowAddingNewDataDynamically: c, */}
              {/*       }, */}
              {/*     }); */}
              {/*   }} */}
              {/* > */}
              {/*   {t<string>('Allow adding new options while choosing')} */}
              {/* </Switch> */}
              <Select
                className={"mt-2"}
                dataSource={options?.choices}
                label={t<string>("Default value")}
                selectedKeys={
                  options?.defaultValue
                    ? multiple
                      ? options.defaultValue
                      : [options.defaultValue]
                    : undefined
                }
                selectionMode={multiple ? "multiple" : "single"}
                size={"sm"}
                onSelectionChange={(c) => {
                  const array = Array.from((c as Set<Key>).values());

                  patchProperty({
                    options: {
                      ...options,
                      defaultValue: multiple ? array : array[0],
                    },
                  });
                }}
              />
            </>
          );
        }
        case PropertyType.Number: {
          const options = (property.options as NumberPropertyOptions) ?? {};
          const previewValue = 80;
          const previewValueStr = Number(previewValue).toFixed(
            options?.precision || 0,
          );

          // console.log(previewValue, options?.precision, previewValueStr);
          options.precision ??= 0;

          return (
            <>
              <Select
                dataSource={NumberPrecisions}
                label={t<string>("Precision")}
                selectedKeys={[options.precision.toString()]}
                onSelectionChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      precision: (c as Set<Key>).values().next().value,
                    },
                  });
                }}
              />
              <Input
                disabled
                label={t<string>("Preview")}
                value={previewValueStr}
              />
            </>
          );
        }
        case PropertyType.Percentage: {
          const options = (property.options as PercentagePropertyOptions) ?? {};
          const previewValue = 80;
          const previewValueStr = `${Number(previewValue).toFixed(options?.precision || 0)}%`;

          options.precision ??= 0;

          return (
            <>
              <Select
                dataSource={NumberPrecisions}
                label={t<string>("Precision")}
                selectedKeys={[options.precision.toString()]}
                onSelectionChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      precision: (c as Set<Key>).values().next().value,
                    },
                  });
                }}
              />
              <Switch
                isSelected={options?.showProgressbar}
                size={"sm"}
                onValueChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      showProgressbar: c,
                    },
                  });
                }}
              >
                {t<string>("Show progressbar")}
              </Switch>
              {options?.showProgressbar ? (
                <div>
                  <div>{t<string>("Preview")}</div>
                  <Progress
                    className={"max-w-[70%]"}
                    label={
                      <div className={"text-[color:var(--bakaui-color)]"}>
                        {previewValueStr}
                      </div>
                    }
                    value={previewValue}
                  />
                </div>
              ) : (
                <Input
                  disabled
                  label={t<string>("Preview")}
                  value={previewValueStr}
                />
              )}
            </>
          );
        }
        case PropertyType.Rating: {
          const options = (property.options as RatingPropertyOptions) ?? {};

          options.maxValue ??= 5;

          return (
            <>
              <Select
                dataSource={RatingMaxValueDataSource}
                label={t<string>("Max value")}
                selectedKeys={[options.maxValue.toString()]}
                onSelectionChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      maxValue: (c as Set<Key>).values().next().value,
                    },
                  });
                }}
              />
            </>
          );
        }
        case PropertyType.Boolean: {
          break;
        }
        case PropertyType.Link:
        case PropertyType.Attachment:
          break;
        case PropertyType.Date:
          break;
        case PropertyType.DateTime:
          break;
        case PropertyType.Time:
          break;
        case PropertyType.Formula: {
          return (
            <>
              <RadioGroup
                label={t<string>("Formula syntax")}
                orientation="horizontal"
              >
                <Radio value="buenos-aires">Javascript</Radio>
              </RadioGroup>
              <AceEditor
                wrapEnabled
                editorProps={{ $blockScrolling: true }}
                mode="javascript"
                name="UNIQUE_ID_OF_DIV"
                theme="monokai"
                onChange={(v) => {
                  console.log(v);
                }}
              />
            </>
          );
        }
        case PropertyType.Multilevel: {
          return (
            <MultilevelData
              options={property.options}
              onChange={(options) => {
                patchProperty({
                  options,
                });
              }}
            />
          );
        }
        case PropertyType.Tags: {
          const options = property.options as TagsPropertyOptions;

          return (
            <>
              <TagList
                checkUsage={checkValueUsage}
                className={"mt-4"}
                tags={options?.tags}
                onChange={(tags) => {
                  patchProperty({
                    options: {
                      ...options,
                      tags,
                    },
                  });
                }}
              />
              {/* <Switch */}
              {/*   className={'mt-4'} */}
              {/*   size={'sm'} */}
              {/*   isSelected={options?.allowAddingNewDataDynamically} */}
              {/*   onValueChange={c => { */}
              {/*     patchProperty({ */}
              {/*       options: { */}
              {/*         ...options, */}
              {/*         allowAddingNewDataDynamically: c, */}
              {/*       }, */}
              {/*     }); */}
              {/*   }} */}
              {/* > */}
              {/*   {t<string>('Allow adding new options while choosing')} */}
              {/* </Switch> */}
            </>
          );
        }
      }
    }

    return;
  };
  const renderPropertyTypeButton = () => {
    const btn = (
      <Button
        color={"default"}
        size={"lg"}
        onClick={() => {
          // console.log(13456577, true);
          settypeGroupsVisible(true);
        }}
      >
        {property.type == undefined ? (
          t<string>("Select a type")
        ) : (
          <PropertyTypeIcon textVariant={"default"} type={property.type} />
        )}
      </Button>
    );

    if (property && property.id != undefined && property.id > 0) {
      // return btn;
      return (
        <div>
          <Tooltip content={t<string>("Click to change type")}>
            <div>{btn}</div>
          </Tooltip>
        </div>
      );
    } else {
      return <div>{btn}</div>;
    }
  };

  const patchProperty = (patches: Partial<CustomPropertyForm>) => {
    const newProperties = {
      ...property,
      ...patches,
    };

    setProperty(newProperties);

    if (
      newProperties.name == undefined ||
      newProperties.name.length == 0 ||
      newProperties.type == undefined ||
      !(newProperties.type > 0)
    ) {
      onChange?.(undefined);
    } else {
      onChange?.(newProperties);
    }
  };

  return (
    <div className={"flex flex-col gap-2"}>
      <div className={"flex gap-2 items-center"}>
        <Popover
          isDismissable
          showArrow
          closeMode={["mask", "esc"]}
          placement={"right"}
          trigger={renderPropertyTypeButton()}
          visible={typeGroupsVisible}
          onOpenChange={(isOpen) => {
            settypeGroupsVisible(isOpen);
          }}
          onVisibleChange={(visible) => {
            settypeGroupsVisible(visible);
          }}
        >
          <div ref={typePopoverDomRef} className={"p-2 flex flex-col gap-2"}>
            {Object.keys(PropertyTypeGroup).map((group) => {
              return (
                <div
                  className={
                    "pb-2 mb-2 border-b-1 last:mb-0 last:border-b-0 last:pb-0"
                  }
                >
                  <div className={"mb-2 font-bold"}>{t<string>(group)}</div>
                  <div className="grid grid-cols-3 gap-x-2 text-sm leading-5">
                    {PropertyTypeGroup[group]!.map((type) => {
                      if (group == UnderDevelopmentGroupKey) {
                        return (
                          <Tooltip
                            content={
                              <FeatureStatusTip
                                name={t<string>(PropertyType[type])}
                                status={"developing"}
                              />
                            }
                          >
                            <Button
                              className={"justify-start"}
                              disabled={
                                validValueTypes?.includes(type) === false
                              }
                              variant={"light"}
                            >
                              <PropertyTypeIcon
                                textVariant={"default"}
                                type={type}
                              />
                            </Button>
                          </Tooltip>
                        );
                      }

                      return (
                        <Button
                          className={"justify-start"}
                          disabled={validValueTypes?.includes(type) === false}
                          variant={"light"}
                          onClick={() => {
                            if (property.id != undefined && property.id > 0) {
                              BApi.customProperty
                                .getCustomPropertyConversionRules()
                                .then((r) => {
                                  const rules =
                                    r.data?.[property.type!]?.[type] ?? [];
                                  // change property type
                                  const model = createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t<string>(
                                      "You are changing property type",
                                    ),
                                    children: (
                                      <div>
                                        <div>
                                          {t<string>(
                                            "Changing the property type may cause the loss of existing data. Click 'continue' to check.",
                                          )}
                                        </div>
                                        {rules.length > 0 && (
                                          <div className={"mt-2"}>
                                            <div className={"font-bold"}>
                                              {t<string>(
                                                "Following rule(s) will be applied",
                                              )}
                                            </div>
                                            <div
                                              className={
                                                "flex flex-wrap gap-2 items-center mt-1"
                                              }
                                            >
                                              {rules.map((r) => {
                                                if (r.description == null) {
                                                  return (
                                                    <Chip size={"sm"}>
                                                      {r.name}
                                                    </Chip>
                                                  );
                                                }

                                                return (
                                                  <Tooltip
                                                    content={
                                                      <pre>{r.description}</pre>
                                                    }
                                                  >
                                                    <Chip size={"sm"}>
                                                      {r.name}
                                                    </Chip>
                                                  </Tooltip>
                                                );
                                              })}
                                            </div>
                                          </div>
                                        )}
                                      </div>
                                    ),
                                    footer: {
                                      actions: ["ok", "cancel"],
                                      okProps: {
                                        children: t<string>("Continue"),
                                      },
                                    },
                                    onOk: async () => {
                                      const rsp =
                                        await BApi.customProperty.previewCustomPropertyTypeConversion(
                                          property.id!,
                                          type,
                                        );

                                      if (rsp.data) {
                                        const changes = rsp.data?.changes || [];
                                        const { dataCount, toType, fromType } =
                                          rsp.data;

                                        createPortal(Modal, {
                                          defaultVisible: true,
                                          title: t<string>("Final check"),
                                          size:
                                            changes.length! > 0
                                              ? "lg"
                                              : undefined,
                                          children: (
                                            <div>
                                              <div className={"text-base"}>
                                                {changes.length > 0
                                                  ? t<string>(
                                                      "Found {{count}} data, and {{changedDataCount}} data will be modified or deleted",
                                                      {
                                                        count: dataCount,
                                                        changedDataCount:
                                                          changes.length!,
                                                      },
                                                    )
                                                  : t<string>(
                                                      "Found {{count}} data, and all of them will be retained",
                                                      { count: dataCount! },
                                                    )}
                                              </div>
                                              <div className={"font-bold"}>
                                                {t<string>(
                                                  "Be careful, this process is irreversible",
                                                )}
                                              </div>
                                              {changes.length > 0 && (
                                                <Table>
                                                  <TableHeader>
                                                    <TableColumn>
                                                      {t<string>(
                                                        "Source value",
                                                      )}
                                                    </TableColumn>
                                                    <TableColumn>
                                                      {t<string>(
                                                        "Converted value",
                                                      )}
                                                    </TableColumn>
                                                  </TableHeader>
                                                  <TableBody>
                                                    {changes.map((c) => {
                                                      return (
                                                        <TableRow>
                                                          <TableCell>
                                                            <ValueRenderer
                                                              type={fromType!}
                                                              value={deserializeStandardValue(
                                                                c.serializedFromValue ??
                                                                  null,
                                                                fromType!,
                                                              )}
                                                              variant={"light"}
                                                            />
                                                          </TableCell>
                                                          <TableCell>
                                                            <ValueRenderer
                                                              type={toType!}
                                                              value={deserializeStandardValue(
                                                                c.serializedToValue ??
                                                                  null,
                                                                toType!,
                                                              )}
                                                              variant={"light"}
                                                            />
                                                          </TableCell>
                                                        </TableRow>
                                                      );
                                                    })}
                                                  </TableBody>
                                                </Table>
                                              )}
                                            </div>
                                          ),
                                          onOk: async () => {
                                            await BApi.customProperty.changeCustomPropertyType(
                                              property.id!,
                                              type,
                                            );
                                            await BApi.customProperty
                                              .getCustomPropertyByKeys({
                                                ids: [property.id!],
                                              })
                                              .then((r) => {
                                                patchProperty(r.data![0]!);
                                              });
                                          },
                                          footer: {
                                            actions: ["ok", "cancel"],
                                            okProps: {
                                              children: t<string>("Convert"),
                                            },
                                          },
                                        });
                                      }
                                    },
                                  });
                                });
                            } else {
                              patchProperty({
                                ...property,
                                type,
                              });
                            }
                            settypeGroupsVisible(false);
                          }}
                        >
                          <PropertyTypeIcon
                            textVariant={"default"}
                            type={type}
                          />
                        </Button>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        </Popover>
        <Input
          className={"flex-1"}
          label={t<string>("Name")}
          size={"sm"}
          value={property.name}
          onValueChange={(name) =>
            patchProperty({
              ...property,
              name,
            })
          }
        />
      </div>
      {renderOptions()}
    </div>
  );
};

ModalContent.displayName = "ModalContent";

export default ModalContent;
