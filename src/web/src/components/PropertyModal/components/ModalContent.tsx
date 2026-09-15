"use client";

import type { Key } from "@react-types/shared";

import { Radio, RadioGroup, TableHeader } from "@heroui/react";
import { useTranslation } from "react-i18next";
import { useState } from "react";
import { FiChevronDown, FiCheck } from "react-icons/fi";

import MultilevelData from "./MultilevelData";
import PropertyPreview from "./PropertyPreview";
import { ReferenceValueUsageProvider } from "./ReferenceValueUsage";

import { isReferenceValueType } from "@/components/Property/PropertySystem";
import {
  Button,
  Chip,
  Input,
  Modal,
  Popover,
  Select,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableRow,
  Tooltip,
} from "@/components/bakaui";
import { AttachmentLayout, PropertyPool, PropertyType } from "@/sdk/constants";
import {
  type SingleChoicePropertyOptions,
  type MultipleChoicePropertyOptions,
  type NumberPropertyOptions,
  type PercentagePropertyOptions,
  type RatingPropertyOptions,
  type TagsPropertyOptions,
  type AttachmentPropertyOptions,
} from "@/components/Property/models";
import ValueRenderer from "@/components/StandardValue/ValueRenderer";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import ChoiceList from "@/components/PropertyModal/components/ChoiceList";
import TagList from "@/components/PropertyModal/components/TagList";
import { optimizeOptions } from "@/components/PropertyModal/helpers";
import BApi from "@/sdk/BApi";
import { getEnumKey } from "@/i18n";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const UnderDevelopmentGroupKey = "UnderDevelopment";

const PropertyTypeGroup: Record<string, PropertyType[]> = {
  Text: [PropertyType.SingleLineText, PropertyType.MultilineText, PropertyType.Link],
  Number: [PropertyType.Number, PropertyType.Percentage, PropertyType.Rating],
  Option: [PropertyType.SingleChoice, PropertyType.MultipleChoice, PropertyType.Multilevel],
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

const withDefaultOptions = (property: CustomPropertyForm): CustomPropertyForm => {
  if (
    (property.id ?? 0) > 0 ||
    property.type == undefined ||
    !isReferenceValueType(property.type)
  ) {
    return property;
  }

  return {
    ...property,
    options: { ...property.options, ignoreCase: property.options?.ignoreCase ?? true },
  };
};

const NumberPrecisions = [0, 1, 2, 3, 4];

const RatingMaxValueDataSource = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((x) => ({
  value: x,
  label: x,
}));

const ModalContent = ({ validValueTypes, value, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const precisionOptions = NumberPrecisions.map((value) => ({
    value,
    label: t("property.editor.decimalPlacesOption", {
      count: value,
      example: Number(1).toFixed(value),
    }),
  }));

  const [typeGroupsVisible, settypeGroupsVisible] = useState(false);

  const [property, setProperty] = useState<CustomPropertyForm>(() => {
    const draft = JSON.parse(JSON.stringify(value ?? {}));

    return withDefaultOptions({ ...draft, options: optimizeOptions(draft.options) });
  });

  const checkValueUsage = property.id
    ? async (optionId: string) => {
        const rsp = await BApi.property.getPropertyValueResourceCounts(
          PropertyPool.Custom,
          property.id!,
          { page: 1, pageSize: 100 },
        );

        if (rsp.code || !rsp.data?.isReady || !rsp.data.counts) {
          throw new Error(t("property.editor.usageUnavailable"));
        }

        return rsp.data.counts[optionId] ?? 0;
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
          const options = property.options as
            | SingleChoicePropertyOptions
            | MultipleChoicePropertyOptions
            | undefined;
          const multiple = property.type === PropertyType.MultipleChoice;

          // console.log(options);
          return (
            <>
              <ChoiceList
                key={`${property.id ?? "new"}-${property.type}`}
                checkUsage={checkValueUsage}
                choices={options?.choices}
                className="min-w-0"
                onChange={(choices) => {
                  patchProperty({
                    options: {
                      ...options,
                      choices,
                    },
                  });
                }}
              />
              <Select
                className="max-w-sm"
                dataSource={options?.choices}
                label={t<string>("Default value")}
                selectedKeys={
                  options?.defaultValue
                    ? multiple
                      ? Array.isArray(options.defaultValue)
                        ? options.defaultValue
                        : [options.defaultValue]
                      : [
                          Array.isArray(options.defaultValue)
                            ? options.defaultValue[0]
                            : options.defaultValue,
                        ]
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

          return (
            <>
              <Select
                className="max-w-xs"
                dataSource={precisionOptions}
                label={t<string>("property.editor.decimalPlaces")}
                selectedKeys={[(options.precision ?? 0).toString()]}
                onSelectionChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      precision: Number((c as Set<Key>).values().next().value),
                    },
                  });
                }}
              />
            </>
          );
        }
        case PropertyType.Percentage: {
          const options =
            (property.options as PercentagePropertyOptions & { showProgressBar?: boolean }) ?? {};

          return (
            <>
              <Select
                className="max-w-xs"
                dataSource={precisionOptions}
                label={t<string>("property.editor.decimalPlaces")}
                selectedKeys={[(options.precision ?? 0).toString()]}
                onSelectionChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      precision: Number((c as Set<Key>).values().next().value),
                    },
                  });
                }}
              />
              <Switch
                isSelected={options.showProgressBar ?? options.showProgressbar ?? false}
                size={"sm"}
                onValueChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      showProgressBar: c,
                      showProgressbar: undefined,
                    },
                  });
                }}
              >
                {t<string>("Show progressbar")}
              </Switch>
            </>
          );
        }
        case PropertyType.Rating: {
          const options = (property.options as RatingPropertyOptions) ?? {};

          return (
            <>
              <Select
                className="max-w-xs"
                dataSource={RatingMaxValueDataSource}
                label={t<string>("property.editor.maximumRating")}
                selectedKeys={[(options.maxValue ?? 5).toString()]}
                onSelectionChange={(c) => {
                  patchProperty({
                    options: {
                      ...options,
                      maxValue: Number((c as Set<Key>).values().next().value),
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
          break;
        case PropertyType.Attachment: {
          const options = (property.options as AttachmentPropertyOptions) ?? {};
          const layout = options.layout ?? AttachmentLayout.Tile;

          return (
            <RadioGroup
              label={t<string>("property.attachment.layout.label")}
              orientation="horizontal"
              value={layout.toString()}
              onValueChange={(v) => {
                patchProperty({
                  options: {
                    ...options,
                    layout: Number(v) as AttachmentLayout,
                  },
                });
              }}
            >
              <Radio value={AttachmentLayout.Tile.toString()}>
                {t<string>("property.attachment.layout.tile")}
              </Radio>
              <Radio value={AttachmentLayout.Carousel.toString()}>
                {t<string>("property.attachment.layout.carousel")}
              </Radio>
            </RadioGroup>
          );
        }
        case PropertyType.Date:
          break;
        case PropertyType.DateTime:
          break;
        case PropertyType.Time:
          break;
        case PropertyType.Formula:
          return (
            <p className="text-sm text-default-500">{t("property.editor.formulaUnavailable")}</p>
          );
        case PropertyType.Multilevel: {
          return (
            <MultilevelData
              key={`${property.id ?? "new"}-${property.type}`}
              options={property.options}
              onChange={(options) => {
                patchProperty({
                  options: { ...property.options, ...options },
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
                key={`${property.id ?? "new"}-${property.type}`}
                checkUsage={checkValueUsage}
                className="min-w-0"
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
            </>
          );
        }
      }
    }

    return;
  };
  const patchProperty = (patches: Partial<CustomPropertyForm>) => {
    const newProperties = withDefaultOptions({
      ...property,
      ...patches,
    });

    // A removed option must not remain selected as the draft's default.
    if (newProperties.options) {
      const options = { ...newProperties.options };
      const isChoice =
        newProperties.type === PropertyType.SingleChoice ||
        newProperties.type === PropertyType.MultipleChoice;

      if (isChoice || newProperties.type === PropertyType.Multilevel) {
        const ids = new Set<string>();
        const collect = (items: { value: string; children?: any[] }[]) => {
          for (const item of items) {
            ids.add(item.value);
            if (item.children) collect(item.children);
          }
        };

        collect(isChoice ? (options.choices ?? []) : (options.data ?? []));
        const defaults = Array.isArray(options.defaultValue)
          ? options.defaultValue
          : options.defaultValue
            ? [options.defaultValue]
            : [];
        const retained = defaults.filter((id: string) => ids.has(id));

        options.defaultValue =
          newProperties.type === PropertyType.SingleChoice ? retained[0] : retained;
      }
      if (newProperties.type === PropertyType.Percentage) {
        if (options.showProgressBar !== undefined || options.showProgressbar !== undefined) {
          options.showProgressBar = options.showProgressBar ?? options.showProgressbar;
        }
        delete options.showProgressbar;
      }
      newProperties.options = options;
    }

    setProperty(newProperties);

    if (
      newProperties.name == undefined ||
      newProperties.name.trim().length == 0 ||
      newProperties.type == undefined ||
      !(newProperties.type > 0)
    ) {
      onChange?.(undefined);
    } else {
      onChange?.(newProperties);
    }
  };

  const selectType = (type: PropertyType) => {
    if (validValueTypes?.includes(type) === false || type === PropertyType.Formula) return;
    if (type === property.type) {
      settypeGroupsVisible(false);

      return;
    }
    if (property.id != undefined && property.id > 0) {
      BApi.customProperty.getCustomPropertyConversionRules().then((r) => {
        const rules = r.data?.[property.type!]?.[type] ?? [];

        // change property type
        createPortal(Modal, {
          defaultVisible: true,
          title: t<string>("You are changing property type"),
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
                    {t<string>("Following rule(s) will be applied")}
                  </div>
                  <div className={"flex flex-wrap gap-2 items-center mt-1"}>
                    {rules.map((r, i) => {
                      if (r.description == null) {
                        return (
                          <Chip key={i} size={"sm"}>
                            {r.name}
                          </Chip>
                        );
                      }

                      return (
                        <Tooltip key={i} content={<pre>{r.description}</pre>}>
                          <Chip size={"sm"}>{r.name}</Chip>
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
            const rsp = await BApi.customProperty.previewCustomPropertyTypeConversion(
              property.id!,
              type,
            );

            if (rsp.data) {
              const changes = rsp.data?.changes || [];
              const { dataCount, toType, fromType } = rsp.data;

              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>("Final check"),
                size: changes.length! > 0 ? "lg" : undefined,
                children: (
                  <div>
                    <div className={"text-base"}>
                      {changes.length > 0
                        ? t<string>(
                            "Found {{count}} data, and {{changedDataCount}} data will be modified or deleted",
                            {
                              count: dataCount,
                              changedDataCount: changes.length!,
                            },
                          )
                        : t<string>("Found {{count}} data, and all of them will be retained", {
                            count: dataCount!,
                          })}
                    </div>
                    <div className={"font-bold"}>
                      {t<string>("Be careful, this process is irreversible")}
                    </div>
                    {changes.length > 0 && (
                      <Table>
                        <TableHeader>
                          <TableColumn>{t<string>("Source value")}</TableColumn>
                          <TableColumn>{t<string>("Converted value")}</TableColumn>
                        </TableHeader>
                        <TableBody>
                          {changes.map((c, i) => {
                            return (
                              <TableRow key={i}>
                                <TableCell>
                                  <ValueRenderer
                                    type={fromType!}
                                    value={deserializeStandardValue(
                                      c.serializedFromValue ?? null,
                                      fromType!,
                                    )}
                                    variant={"light"}
                                  />
                                </TableCell>
                                <TableCell>
                                  <ValueRenderer
                                    type={toType!}
                                    value={deserializeStandardValue(
                                      c.serializedToValue ?? null,
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
                  await BApi.customProperty.changeCustomPropertyType(property.id!, type);
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
  };

  const isReference = property.type != undefined && isReferenceValueType(property.type);
  const optionsContent = renderOptions();

  return (
    <div className="property-editor flex min-w-0 flex-col gap-4">
      <div className="property-editor-identity">
        <Input
          isRequired
          className="min-w-0"
          label={t<string>("property.editor.name")}
          labelPlacement="outside"
          placeholder={t<string>("property.editor.namePlaceholder")}
          value={property.name ?? ""}
          onValueChange={(name) => patchProperty({ name })}
        />
        <div className="flex min-w-0 flex-col gap-2">
          <span className="text-sm font-medium">{t("property.editor.type")}</span>
          <Popover
            isDismissable
            showArrow
            closeMode={["mask", "esc"]}
            placement="bottom-end"
            trigger={
              <Button
                aria-label={t<string>("property.editor.chooseType")}
                className="h-10 w-full justify-between gap-3"
                endContent={<FiChevronDown className="shrink-0" />}
                variant="flat"
                onPress={() => settypeGroupsVisible(true)}
              >
                {property.type == undefined ? (
                  t("property.editor.chooseType")
                ) : (
                  <span className="flex min-w-0 items-center gap-2">
                    <PropertyTypeIcon textVariant="none" type={property.type} />
                    <span className="truncate">
                      {t(getEnumKey("PropertyType", PropertyType[property.type]))}
                    </span>
                  </span>
                )}
              </Button>
            }
            visible={typeGroupsVisible}
            onOpenChange={settypeGroupsVisible}
            onVisibleChange={settypeGroupsVisible}
          >
            <div className="property-editor-types">
              {Object.entries(PropertyTypeGroup).map(([group, types]) => (
                <section key={group} className="min-w-0">
                  <h4 className="mb-1 px-2 text-xs font-medium text-default-500">
                    {t(`property.editor.group.${group}`)}
                  </h4>
                  <div className="property-editor-type-grid">
                    {types.map((type) => {
                      const developing = group === UnderDevelopmentGroupKey;
                      const disabled = developing || validValueTypes?.includes(type) === false;

                      return (
                        <Button
                          key={type}
                          aria-pressed={property.type === type}
                          className="h-auto min-h-16 min-w-0 justify-start whitespace-normal px-2 py-2 text-left"
                          color={property.type === type ? "primary" : "default"}
                          isDisabled={disabled}
                          variant={property.type === type ? "flat" : "light"}
                          onPress={() => selectType(type)}
                        >
                          <span className="mt-0.5 shrink-0 self-start text-lg">
                            <PropertyTypeIcon textVariant="none" type={type} />
                          </span>
                          <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                            <span className="flex items-center gap-2 text-sm font-medium">
                              {t(getEnumKey("PropertyType", PropertyType[type]))}
                              {property.type === type && <FiCheck className="shrink-0" />}
                            </span>
                            <span className="text-xs font-normal leading-relaxed text-default-500">
                              {t(`property.editor.typeDescription.${PropertyType[type]}`)}
                            </span>
                          </span>
                        </Button>
                      );
                    })}
                  </div>
                </section>
              ))}
            </div>
          </Popover>
        </div>
      </div>
      {property.type == undefined ? (
        <p className="py-5 text-sm text-default-500">{t("property.editor.chooseTypeHint")}</p>
      ) : (
        <div className="flex min-w-0 flex-col gap-3">
          <p className="text-sm leading-relaxed text-default-500">
            {t(`property.editor.typeDescription.${PropertyType[property.type]}`)}
          </p>
          <ReferenceValueUsageProvider
            options={property.options}
            property={
              property.id && isReference
                ? {
                    id: property.id,
                    type: property.type,
                    name: property.name,
                    pool: PropertyPool.Custom,
                  }
                : undefined
            }
          >
            <div
              className={`property-editor-workspace ${isReference ? "property-editor-workspace-reference" : ""}`}
            >
              <section className="flex min-w-0 flex-col gap-3">
                {!isReference && (
                  <h3 className="text-sm font-medium">{t("property.editor.configuration")}</h3>
                )}
                {optionsContent ?? (
                  <p className="text-sm leading-relaxed text-default-500">
                    {t("property.editor.noOptions")}
                  </p>
                )}
                {isReference && (
                  <div className="flex flex-col gap-1.5 pt-1">
                    <Switch
                      isSelected={property.options?.ignoreCase ?? false}
                      size="sm"
                      onValueChange={(ignoreCase) =>
                        patchProperty({ options: { ...property.options, ignoreCase } })
                      }
                    >
                      {t("property.reference.ignoreCase")}
                    </Switch>
                    <p className="text-xs leading-relaxed text-default-500">
                      {t("property.reference.ignoreCaseHelp")}
                    </p>
                  </div>
                )}
              </section>
              <PropertyPreview
                name={property.name}
                options={property.options}
                type={property.type}
              />
            </div>
          </ReferenceValueUsageProvider>
        </div>
      )}
    </div>
  );
};

ModalContent.displayName = "ModalContent";

export default ModalContent;
