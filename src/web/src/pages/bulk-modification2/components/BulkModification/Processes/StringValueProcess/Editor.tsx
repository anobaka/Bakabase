"use client";

import type { StringProcessOptions } from "./models";
import type { PropertyType } from "@/sdk/constants";
import type { BulkModificationVariable } from "@/pages/bulk-modification2/components/BulkModification/models";

import { Trans, useTranslation } from "react-i18next";
import React, { useEffect, useState } from "react";

import { validate } from "./helpers";

import {
  type BulkModificationProcessorValueType,
  BulkModificationStringProcessOperation,
  bulkModificationStringProcessOperations,
} from "@/sdk/constants";
import { ProcessValueEditor } from "@/pages/bulk-modification2/components/BulkModification/ProcessValue";
import { Input, NumberInput, Select } from "@/components/bakaui";
import DirectionSelector from "@/pages/bulk-modification2/components/BulkModification/DirectionSelector";
import { buildLogger } from "@/components/utils";

type Props = {
  operation?: BulkModificationStringProcessOperation;
  options?: StringProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (
    operation: BulkModificationStringProcessOperation,
    options?: StringProcessOptions,
    error?: string,
  ) => any;
  propertyType: PropertyType;
};

const log = buildLogger("StringProcessorEditor");

export default ({
  operation: propsOperation,
  options: propsOptions,
  onChange,
  variables,
  availableValueTypes,
  propertyType,
}: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<StringProcessOptions>(
    propsOptions ?? {},
  );
  const [operation, setOperation] =
    useState<BulkModificationStringProcessOperation>(
      propsOperation ??
        BulkModificationStringProcessOperation.SetWithFixedValue,
    );

  log("operation", operation, "options", options, typeof operation);

  useEffect(() => {
    const error = validate(operation, options);

    onChange?.(
      operation,
      options,
      error == undefined ? undefined : t<string>(error),
    );
  }, [options, operation]);

  const changeOptions = (patches: Partial<StringProcessOptions>) => {
    const newOptions = {
      ...options,
      ...patches,
    };

    setOptions(newOptions);
  };

  const changeOperation = (
    newOperation: BulkModificationStringProcessOperation,
  ) => {
    setOperation(newOperation);
    const newOptions = {};

    setOptions(newOptions);
  };

  const renderValueCell = () => {
    return (
      <ProcessValueEditor
        availableValueTypes={availableValueTypes}
        baseValueType={propertyType}
        value={options.value}
        variables={variables}
        onChange={(value) => {
          changeOptions({ value });
        }}
      />
    );
  };

  const renderSubOptions = (options: StringProcessOptions) => {
    log("renderOptions", operation, options);

    if (!operation) {
      return null;
    }
    const components: { label: string; comp: any }[] = [];

    switch (operation) {
      case BulkModificationStringProcessOperation.SetWithFixedValue:
        components.push({
          label: t<string>("Value"),
          comp: renderValueCell(),
        });
        break;
      case BulkModificationStringProcessOperation.Delete:
        break;
      case BulkModificationStringProcessOperation.AddToStart:
      case BulkModificationStringProcessOperation.AddToEnd:
        components.push({
          label: t<string>("Value"),
          comp: renderValueCell(),
        });
        break;
      case BulkModificationStringProcessOperation.AddToAnyPosition:
        components.push({
          label: t<string>("Value"),
          comp: (
            <div className={"flex items-center gap-1 whitespace-nowrap"}>
              <Trans
                i18nKey={
                  "BulkModification.Processor.Editor.Operation.AddToAnyPosition"
                }
                values={{
                  value: options.value,
                }}
              >
                {/* 0 */}
                Add&nbsp;
                {/* 1 */}
                {renderValueCell()}
                {/* 2 */}
                &nbsp;to the&nbsp;
                {/* 3 */}
                <NumberInput
                  className={"w-auto"}
                  placeholder={t<string>("Starts from 0")}
                  style={{ width: 100 }}
                  value={options.index}
                  onValueChange={(index) => changeOptions({ index })}
                />
                {/* 4 */}
                (th)&nbsp;character from&nbsp;
                {/* 5 */}
                <DirectionSelector
                  isReversed={options.isPositioningDirectionReversed}
                  subject={"positioning"}
                  onChange={(v) =>
                    changeOptions({ isPositioningDirectionReversed: v })
                  }
                />
              </Trans>
            </div>
          ),
        });
        break;
      case BulkModificationStringProcessOperation.RemoveFromStart:
      case BulkModificationStringProcessOperation.RemoveFromEnd:
        components.push({
          label: t<string>("Count"),
          comp: (
            <NumberInput
              className={"w-auto"}
              style={{ width: 100 }}
              value={options.count}
              onValueChange={(count) => changeOptions({ count })}
            />
          ),
        });
        break;
      case BulkModificationStringProcessOperation.RemoveFromAnyPosition:
        // delete 6 characters forward from the fifth character from the end
        components.push({
          label: t<string>("Value"),
          comp: (
            <div className={"flex items-center gap-1 whitespace-nowrap"}>
              <Trans
                i18nKey={
                  "BulkModification.Processor.Editor.Operation.RemoveFromAnyPosition"
                }
                values={{
                  value: options.value,
                }}
              >
                {/* 0 */}
                Delete&nbsp;
                {/* 1 */}
                <NumberInput
                  className={"w-auto"}
                  style={{ width: 100 }}
                  value={options.count}
                  onValueChange={(count) => changeOptions({ count })}
                />
                {/* 2 */}
                &nbsp;characters&nbsp;
                {/* 3 */}
                <DirectionSelector
                  isReversed={options.isPositioningDirectionReversed}
                  subject={"operation"}
                  onChange={(v) =>
                    changeOptions({ isPositioningDirectionReversed: v })
                  }
                />
                {/* 4 */}
                from the &nbsp;
                {/* 5 */}
                <NumberInput
                  className={"w-auto"}
                  placeholder={t<string>("Starts from 0")}
                  style={{ width: 100 }}
                  value={options.index}
                  onValueChange={(index) => changeOptions({ index })}
                />
                {/* 6 */}
                (th)&nbsp;charater from&nbsp;
                {/* 7 */}
                <DirectionSelector
                  isReversed={options.isPositioningDirectionReversed}
                  subject={"positioning"}
                  onChange={(v) =>
                    changeOptions({ isPositioningDirectionReversed: v })
                  }
                />
              </Trans>
            </div>
          ),
        });
        break;
      case BulkModificationStringProcessOperation.ReplaceFromStart:
      case BulkModificationStringProcessOperation.ReplaceFromEnd:
      case BulkModificationStringProcessOperation.ReplaceFromAnyPosition:
      case BulkModificationStringProcessOperation.ReplaceWithRegex:
        components.push({
          label: t<string>("Value"),
          comp: (
            <div className={"flex items-center gap-1 whitespace-nowrap"}>
              <Trans
                i18nKey={"BulkModification.Processor.Editor.Operation.Replace"}
              >
                {/* 0 */}
                Replace&nbsp;
                {/* 1 */}
                <Input
                  value={options.find}
                  onValueChange={(find) => changeOptions({ find })}
                />
                {/* 2 */}
                &nbsp;with&nbsp;
                {/* 3 */}
                {renderValueCell()}
              </Trans>
            </div>
          ),
        });
        break;
    }

    return components.map((c, i) => {
      return (
        <>
          <div>{c.label}</div>
          <div>{c.comp}</div>
        </>
      );
    });
  };

  return (
    <div
      className={"grid items-center gap-2"}
      style={{ gridTemplateColumns: "auto minmax(0, 1fr)" }}
    >
      <div>{t<string>("Operation")}</div>
      <Select
        dataSource={bulkModificationStringProcessOperations.map((tpo) => ({
          label: t<string>(tpo.label),
          value: tpo.value,
        }))}
        selectedKeys={
          operation == undefined ? undefined : [operation.toString()]
        }
        selectionMode={"single"}
        onSelectionChange={(keys) => {
          changeOperation(
            parseInt(
              Array.from(keys || [])[0] as string,
              10,
            ) as BulkModificationStringProcessOperation,
          );
        }}
      />
      {renderSubOptions(options)}
    </div>
  );
};
