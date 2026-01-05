"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import React, { useState } from "react";
import { CardHeader } from "@heroui/react";
import { Button, Card, CardBody, Modal } from "@/components/bakaui";
import PropertySelector from "@/components/PropertySelector";
import { BulkModificationProcessorValueType, PropertyPool } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ProcessStep from "@/pages/bulk-modification/components/BulkModification/ProcessStep";
import ProcessStepModal from "@/pages/bulk-modification/components/BulkModification/ProcessStepModal";
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
const ProcessModal = ({ onDestroyed, process: propsProcess, onSubmit, variables }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const bmInternals = useBulkModificationInternalsStore.getState();

  const [process, setProcess] = useState<Partial<BulkModificationProcess>>(propsProcess ?? {});

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
      title={t<string>("bulkModification.label.settingProcess")}
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
          <div className={"grid items-center gap-2"} style={{ gridTemplateColumns: "auto 1fr" }}>
            <div className={"text-right"}>{t<string>("bulkModification.label.property")}</div>
            <div>
              <Button
                color={"primary"}
                size="sm"
                variant={"light"}
                onClick={() => {
                  createPortal(PropertySelector, {
                    v2: true,
                    pool: PropertyPool.All,
                    isDisabled: (p) =>
                      bmInternals.disabledPropertyKeys?.[p.pool]?.includes(p.id) ||
                      !bmInternals.supportedStandardValueTypes?.includes(p.bizValueType),
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
                  t<string>("bulkModification.select.property")
                )}
              </Button>
            </div>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader className="flex-col items-start gap-0.5">
          <div className="font-medium">{t<string>("bulkModification.label.steps")}</div>
          <p className="text-xs text-default-400">
            {t<string>("bulkModification.info.addPreprocessSteps")}
          </p>
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
              {t<string>("bulkModification.action.addPreprocess")}
            </Button>
          </div>
        </CardBody>
      </Card>
    </Modal>
  );
};

ProcessModal.displayName = "ProcessModal";

export default ProcessModal;
