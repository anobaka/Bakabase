"use client";

import type { EditingListStringValueProcessOptions, ListStringValueProcessOptions } from "./models";
import type { IProperty } from "@/components/Property/models";
import type {
  BulkModificationProcessValue,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import React, { useEffect, useState } from "react";
import { getEnumKey } from "@/i18n";

import { StringValueProcessEditor } from "../StringValueProcess";

import { validate } from "./helpers";

import {
  BulkModificationListStringProcessOperation,
  bulkModificationListStringProcessOperations,
  bulkModificationProcessorOptionsItemsFilterBies,
  BulkModificationProcessorOptionsItemsFilterBy,
  type BulkModificationProcessorValueType,
  PropertyType,
} from "@/sdk/constants";
import { Input, Select } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { ProcessValueEditor } from "@/pages/bulk-modification/components/BulkModification/ProcessValue";

type Props = {
  property: IProperty;
  operation?: BulkModificationListStringProcessOperation;
  options?: ListStringValueProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationListStringProcessOperation,
    options: ListStringValueProcessOptions,
    error?: string,
  ) => any;
};

const log = buildLogger("ListProcessorEditor");
const Editor = ({
  property,
  operation: propsOperation,
  options: propsOptions,
  onChange,
  variables,
  availableValueTypes,
}: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<EditingListStringValueProcessOptions>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationListStringProcessOperation>(
    propsOperation ?? BulkModificationListStringProcessOperation.SetWithFixedValue,
  );

  log("operation", operation, "options", options, typeof operation);

  useEffect(() => {
    const error = validate(operation, options);

    onChange?.(
      operation,
      options as ListStringValueProcessOptions,
      error == undefined ? undefined : t<string>(error),
    );
  }, [options, operation]);

  const changeOptions = (patches: EditingListStringValueProcessOptions) => {
    const newOptions = {
      ...options,
      ...patches,
    };

    setOptions(newOptions);
  };

  const changeOperation = (newOperation: BulkModificationListStringProcessOperation) => {
    setOperation(newOperation);
    const newOptions = {};

    setOptions(newOptions);
  };

  const renderValueCell = (field: string = "value") => {
    return (
      <ProcessValueEditor
        baseValueType={property.type}
        preferredProperty={property}
        value={options[field]}
        variables={variables}
        onChange={(value: BulkModificationProcessValue) =>
          changeOptions({
            value,
          })
        }
      />
    );
  };

  const renderSubOptions = (options: EditingListStringValueProcessOptions) => {
    log("renderOptions", operation, options);

    if (!operation) {
      return null;
    }
    const components: { label: string; comp: any }[] = [];

    switch (operation) {
      case BulkModificationListStringProcessOperation.SetWithFixedValue:
      case BulkModificationListStringProcessOperation.Append:
      case BulkModificationListStringProcessOperation.Prepend:
        components.push({
          label: "",
          comp: renderValueCell(),
        });
        break;
      case BulkModificationListStringProcessOperation.Modify: {
        const filterBy = options?.modifyOptions?.filterBy;

        components.push({
          label: t<string>("bulkModification.filter.filterBy"),
          comp: (
            <div className={"flex items-center gap-2"}>
              <div className={"w-2/5"}>
                <Select
                  dataSource={bulkModificationProcessorOptionsItemsFilterBies.map((fb) => ({
                    label: t(getEnumKey("BulkModificationProcessorOptionsItemsFilterBy", fb.label)),
                    value: fb.value,
                  }))}
                  selectionMode={"single"}
                  onSelectionChange={(keys) => {
                    changeOptions({
                      modifyOptions: {
                        ...options?.modifyOptions,
                        filterBy: parseInt(
                          Array.from(keys || [])[0] as string,
                          10,
                        ) as BulkModificationProcessorOptionsItemsFilterBy,
                      },
                    });
                  }}
                />
              </div>
              {filterBy != undefined &&
                (() => {
                  switch (filterBy) {
                    case BulkModificationProcessorOptionsItemsFilterBy.All:
                      return null;
                    case BulkModificationProcessorOptionsItemsFilterBy.Containing:
                    case BulkModificationProcessorOptionsItemsFilterBy.Matching:
                      return (
                        <Input
                          placeholder={t<string>(
                            filterBy == BulkModificationProcessorOptionsItemsFilterBy.Containing
                              ? "bulkModification.placeholder.inputKeyword"
                              : "bulkModification.placeholder.inputRegex",
                          )}
                          onValueChange={(v) => {
                            changeOptions({
                              modifyOptions: {
                                ...options?.modifyOptions,
                                filterValue: v,
                              },
                            });
                          }}
                        />
                      );
                  }
                })()}
            </div>
          ),
        });

        components.push({
          label: t<string>("bulkModification.filter.forEachItem"),
          comp: (
            <StringValueProcessEditor
              availableValueTypes={availableValueTypes}
              operation={options?.modifyOptions?.operation}
              propertyType={PropertyType.SingleLineText}
              // property={{
              //   type: PropertyType.SingleLineText,
              //   bizValueType: StandardValueType.String,
              //   dbValueType: StandardValueType.String,
              //   id: 0,
              //   name: 'Fake text',
              //   pool: PropertyPool.Custom,
              //   poolName: 'Custom',
              //   typeName: 'SingleLineText',
              // }}
              options={options?.modifyOptions?.options}
              variables={variables}
              onChange={(sOperation, sOptions, error) => {
                changeOptions({
                  modifyOptions: {
                    ...options?.modifyOptions,
                    operation: sOperation,
                    options: sOptions,
                  },
                });
              }}
            />
          ),
        });
        break;
      }
      case BulkModificationListStringProcessOperation.Delete:
        break;
    }

    return components.map((c, i) => (
      <div key={i} className="flex flex-col gap-1">
        {c.label && <span className="text-sm text-default-500">{c.label}</span>}
        <div>{c.comp}</div>
      </div>
    ));
  };

  return (
    <div className={"flex flex-col gap-3"}>
      <Select
        label={t<string>("bulkModification.label.operation")}
        dataSource={bulkModificationListStringProcessOperations.map((tpo) => ({
          label: t(getEnumKey("BulkModificationListStringProcessOperation", tpo.label)),
          value: tpo.value,
        }))}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        selectionMode={"single"}
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(
              Array.from(keys || [])[0] as string,
              10,
            ) as BulkModificationListStringProcessOperation,
          );
        }}
      />
      {renderSubOptions(options)}
    </div>
  );
};

Editor.displayName = "Editor";

export default Editor;
