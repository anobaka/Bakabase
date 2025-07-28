"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from "@/pages/bulk-modification2/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import React, { useState } from "react";
import { CardHeader } from "@heroui/react";
import { QuestionCircleOutlined } from "@ant-design/icons";

import { Button, Card, CardBody, Modal, Tooltip } from "@/components/bakaui";
import PropertySelectorPage from "@/components/PropertySelector";
import {
  BulkModificationProcessorValueType,
  PropertyPool,
} from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ProcessStep from "@/pages/bulk-modification2/components/BulkModification/ProcessStep";
import ProcessStepModal from "@/pages/bulk-modification2/components/BulkModification/ProcessStepModal";
import { useBulkModificationInternalsStore } from "@/stores/bulkModificationInternals";
import { PropertyLabel } from "@/components/Property";

type Props = {
  process?: Partial<BulkModificationProcess>;
  variables?: BulkModificationVariable[];
  onSubmit?: (process: BulkModificationProcess) => void;
} & DestroyableProps;

const validate = (p?: Partial<BulkModificationProcess>) =>
  !(!p || !p.propertyId || !p.propertyPool);

const AllBulkModificationValueTypes = [
  BulkModificationProcessorValueType.ManuallyInput,
  BulkModificationProcessorValueType.Variable,
];
const ProcessModal = ({
  onDestroyed,
  process: propsProcess,
  onSubmit,
  variables,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const bmInternals = useBulkModificationInternalsStore.getState();

  const [process, setProcess] = useState<Partial<BulkModificationProcess>>(
    propsProcess ?? {},
  );

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          isDisabled: !validate(process),
        },
      }}
      size={"xl"}
      title={t<string>("Setting process")}
      onDestroyed={onDestroyed}
      onOk={() => {
        if (!validate(process)) {
          throw new Error("Invalid process");
        }
        onSubmit?.(process as BulkModificationProcess);
      }}
    >
      <Card>
        <CardBody>
          <div
            className={"grid items-center gap-2"}
            style={{ gridTemplateColumns: "auto 1fr" }}
          >
            <div className={"text-right"}>{t<string>("Property")}</div>
            <div>
              <Button
                color={"primary"}
                size="sm"
                variant={"light"}
                onClick={() => {
                  createPortal(PropertySelectorPage, {
                    pool: PropertyPool.All,
                    isDisabled: (p) =>
                      bmInternals.disabledPropertyKeys?.[p.pool]?.includes(
                        p.id,
                      ) ||
                      !bmInternals.supportedStandardValueTypes?.includes(
                        p.bizValueType,
                      ),
                    multiple: false,
                    onSubmit: async (ps) => {
                      const p = ps[0];

                      setProcess({
                        ...process,
                        propertyPool: p.pool,
                        propertyId: p.id,
                        property: p,
                      });
                    },
                  });
                }}
              >
                {process?.property ? (
                  <PropertyLabel showPool property={process.property} />
                ) : (
                  t<string>("Select a property")
                )}
              </Button>
            </div>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader>
          <div className={"flex items-center gap-1"}>
            <div>{t<string>("Steps")}</div>
            <Tooltip
              content={
                <div>
                  <div>
                    {t<string>("You can add multiple preprocessing steps.")}
                  </div>
                </div>
              }
            >
              <QuestionCircleOutlined className={"text-base"} />
            </Tooltip>
          </div>
        </CardHeader>
        <CardBody>
          {process?.steps && process.steps.length > 0 && (
            <div className={"flex flex-col gap-1 mb-2"}>
              {process.steps.map((step, i) => {
                return (
                  <ProcessStep
                    editable
                    availableValueTypes={AllBulkModificationValueTypes}
                    no={`${i + 1}`}
                    property={process.property!}
                    step={step}
                    variables={variables}
                    onChange={(newStep) => {
                      process.steps![i] = newStep;
                      setProcess({
                        ...process,
                      });
                    }}
                    onDelete={() => {
                      process.steps?.splice(i, 1);
                      setProcess({
                        ...process,
                      });
                    }}
                  />
                );
              })}
            </div>
          )}
          <div>
            <Button
              color={"secondary"}
              isDisabled={!process?.property}
              size={"sm"}
              variant={"ghost"}
              onClick={() => {
                if (process?.property) {
                  createPortal(ProcessStepModal, {
                    property: process.property,
                    variables,
                    availableValueTypes: AllBulkModificationValueTypes,
                    onSubmit: (operation: number, options: any) => {
                      if (!process.steps) {
                        process.steps = [];
                      }
                      process.steps.push({
                        operation,
                        options,
                      });
                      setProcess({
                        ...process,
                      });
                    },
                  });
                }
              }}
            >
              {t<string>("Add a preprocess")}
            </Button>
          </div>
        </CardBody>
      </Card>
    </Modal>
  );
};

ProcessModal.displayName = "ProcessModal";

export default ProcessModal;
