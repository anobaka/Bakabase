"use client";

import type { ListStringValueProcessOptions } from "./models";
import type { IProperty } from "@/components/Property/models";
import type { BulkModificationVariable } from "@/pages/bulk-modification2/components/BulkModification/models";

import { Trans, useTranslation } from "react-i18next";
import React from "react";

import { BulkModificationListStringProcessOperation } from "@/sdk/constants";
import { ProcessValueDemonstrator } from "@/pages/bulk-modification2/components/BulkModification/ProcessValue";
import { buildLogger } from "@/components/utils";

type Props = {
  property: IProperty;
  operation?: BulkModificationListStringProcessOperation;
  options?: ListStringValueProcessOptions;
  variables?: BulkModificationVariable[];
};

const log = buildLogger("ListStringValueProcess");
const Demonstrator = (props: Props) => {
  const { operation, options, variables, property } = props;
  const { t } = useTranslation();

  log(props);

  switch (operation!) {
    case BulkModificationListStringProcessOperation.SetWithFixedValue: {
      return (
        <>
          <Trans
            i18nKey={
              "BulkModification.Processor.Demonstrator.Operation.SetDirectly"
            }
          >
            <div className="primary" />
            with fixed value
          </Trans>
          <ProcessValueDemonstrator
            property={property}
            value={options?.value}
            valueType={options?.valueType}
            variables={variables}
          />
        </>
      );
    }
    case BulkModificationListStringProcessOperation.AddToStart:
    case BulkModificationListStringProcessOperation.AddToEnd:
      return (
        <Trans
          i18nKey={
            "BulkModification.Processor.Demonstrator.Operation.AddToStartOrEnd"
          }
          values={{
            direction:
              operation == BulkModificationListStringProcessOperation.AddToStart
                ? t<string>("Position.Beginning")
                : t<string>("Position.End"),
            value: options?.value,
          }}
        >
          Add
          <ProcessValueDemonstrator
            property={property}
            value={options?.value}
            valueType={options?.valueType}
            variables={variables}
          />
          to
          <span className="primary">beginning or end</span>
        </Trans>
      );
    case BulkModificationListStringProcessOperation.AddToAnyPosition:
      return (
        <Trans
          i18nKey={
            "BulkModification.Processor.Demonstrator.Operation.AddToAnyPosition"
          }
          values={{
            direction: options?.isPositioningDirectionReversed
              ? t<string>("Position.End")
              : t<string>("Position.Beginning"),
            position: options?.index,
            value: options?.value,
          }}
        >
          Add
          <ProcessValueDemonstrator
            property={property}
            value={options?.value}
            valueType={options?.valueType}
            variables={variables}
          />
          to the
          <div className="secondary">{options?.index}</div>
          position from the
          <div className="primary">end</div>
        </Trans>
      );
    case BulkModificationListStringProcessOperation.RemoveFromStart:
    case BulkModificationListStringProcessOperation.RemoveFromEnd:
      return (
        <>
          <Trans
            i18nKey={
              "BulkModification.Processor.Demonstrator.Operation.RemoveFromStartOrEnd"
            }
            values={{
              direction:
                operation ==
                BulkModificationListStringProcessOperation.RemoveFromStart
                  ? t<string>("Position.Beginning")
                  : t<string>("Position.End"),
              count: options?.count,
            }}
          >
            Remove
            <span className="secondary">{options?.count}</span>
            characters from
            <span className="primary">beginning or end</span>
          </Trans>
        </>
      );
    case BulkModificationListStringProcessOperation.RemoveFromAnyPosition: {
      const texts = {
        direction: options?.isPositioningDirectionReversed
          ? t<string>("Position.End")
          : t<string>("Position.Beginning"),
        position: options?.index,
        count: options?.count,
        removeDirection: options?.isOperationDirectionReversed
          ? t<string>("TextOperation.Backward")
          : t<string>("TextOperation.Forward"),
      };

      return (
        <>
          <Trans
            i18nKey={
              "BulkModification.Processor.Demonstrator.Operation.RemoveFromAnyPosition"
            }
            values={texts}
          >
            {/* delete 6 characters forward from the fifth character from the end */}
            Delete
            <span className="secondary">{texts.count}</span>
            characters
            <span className="primary">{texts.removeDirection}</span>
            the
            <span className={"secondary"}>{texts.position}</span>
            character from the
            <span className="primary">{texts.direction}</span>
          </Trans>
        </>
      );
    }
    case BulkModificationListStringProcessOperation.ReplaceFromStart:
    case BulkModificationListStringProcessOperation.ReplaceFromEnd: {
      const texts = {
        direction:
          operation == BulkModificationListStringProcessOperation.ReplaceFromEnd
            ? t<string>("Position.End")
            : t<string>("Position.Beginning"),
        replace: options?.replace,
        find: options?.find,
      };

      return (
        <>
          <Trans
            i18nKey={
              "BulkModification.Processor.Demonstrator.Operation.ReplaceFromStartOrEnd"
            }
            values={texts}
          >
            {/* Replace xxx with yyy from start */}
            {/* 0 */}
            <div className="primary">Replace</div>
            {/* 1 */}
            <div className="secondary">{texts.find}</div>
            {/* 2 */}
            with
            {/* 3 */}
            <ProcessValueDemonstrator
              property={property}
              value={options?.replace}
              valueType={options?.valueType}
              variables={variables}
            />
            {/* 4 */}
            from
            {/* 5 */}
            <div className="primary">{texts.direction}</div>
          </Trans>
        </>
      );
    }
    case BulkModificationListStringProcessOperation.ReplaceFromAnyPosition: {
      const texts = {
        direction: options?.isPositioningDirectionReversed
          ? t<string>("end")
          : t<string>("start"),
        replace: options?.replace,
        find: options?.find,
      };

      return (
        <Trans
          i18nKey={"BulkModification.Processor.Demonstrator.Operation.Replace"}
          values={texts}
        >
          {/* Replace xxx with yyy */}
          {/* 0 */}
          <div className="primary">Replace</div>
          {/* 1 */}
          <div className="secondary">{texts.find}</div>
          {/* 2 */}
          with
          {/* 3 */}
          <ProcessValueDemonstrator
            property={property}
            value={options?.replace}
            valueType={options?.valueType}
            variables={variables}
          />
        </Trans>
      );
    }
    case BulkModificationListStringProcessOperation.ReplaceWithRegex: {
      const texts = {
        replace: options?.replace,
        find: options?.find,
      };

      return (
        <Trans
          i18nKey={
            "BulkModification.Processor.Demonstrator.Operation.ReplaceWithRegex"
          }
          values={texts}
        >
          {/* Use regex to replace xxx with yyy */}
          {/* 0 */}
          Use
          {/* 1 */}
          <div className="primary">regex</div>
          {/* 2 */}
          to
          {/* 3 */}
          <div className="primary">replace</div>
          {/* 4 */}
          <div className="secondary">{texts.find}</div>
          {/* 5 */}
          with
          {/* 6 */}
          <ProcessValueDemonstrator
            property={property}
            value={options?.replace}
            valueType={options?.valueType}
            variables={variables}
          />
        </Trans>
      );
    }
    case BulkModificationListStringProcessOperation.Delete: {
      return <div className={"primary"}>{t<string>("Delete")}</div>;
    }
    default:
      return <>{t<string>("Unsupported value")}</>;
  }
};

Demonstrator.displayName = "Demonstrator";

export default Demonstrator;
