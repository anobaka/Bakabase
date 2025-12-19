"use client";

"use strict";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { CardHeader } from "@heroui/react";
import { QuestionCircleOutlined } from "@ant-design/icons";
import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import { Button, Card, CardBody, Input, Modal, Select, Tooltip } from "@/components/bakaui";
import PropertySelector from "@/components/PropertySelector";
import { PropertyPool, propertyValueScopes } from "@/sdk/constants";
import ProcessStep from "@/pages/bulk-modification/components/BulkModification/ProcessStep";
import ProcessStepModal from "@/pages/bulk-modification/components/BulkModification/ProcessStepModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { buildLogger } from "@/components/utils";
import { useBulkModificationInternalsStore } from "@/stores/bulkModificationInternals";
import { PropertyValueScopeSelectorLabel } from "@/components/Labels";
import { PropertyLabel } from "@/components/Property";

type Props = {
  variable?: Partial<BulkModificationVariable>;
  onChange?: (variable: BulkModificationVariable) => any;
} & DestroyableProps;

const validate = (v?: Partial<BulkModificationVariable>) =>
  !(!v || !v.name || !v.propertyId || !v.propertyPool || v.scope == undefined);

const log = buildLogger("VariableModal");
const VariableModal = ({ variable: propsVariable, onDestroyed, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const bmInternals = useBulkModificationInternalsStore.getState();

  const [variable, setVariable] = useState<Partial<BulkModificationVariable>>(propsVariable ?? {});

  useUpdateEffect(() => {
    setVariable(propsVariable ?? {});
  }, [propsVariable]);

  log(variable, propertyValueScopes);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          isDisabled: !validate(variable),
        },
      }}
      size={"xl"}
      title={t<string>("Setting variable")}
      onDestroyed={onDestroyed}
      onOk={() => {
        if (!validate(variable)) {
          throw new Error("Invalid variable");
        }
        onChange?.(variable as BulkModificationVariable);
      }}
    >
      <Card>
        <CardBody>
          <div className={"grid items-center gap-2"} style={{ gridTemplateColumns: "auto 1fr" }}>
            <div className={"text-right"}>{t<string>("Property")}</div>
            <div className={"flex items-center gap-2"}>
              <Button
                color={"primary"}
                size="sm"
                variant={"flat"}
                onClick={() => {
                  createPortal(PropertySelector, {
                    pool: PropertyPool.All,
                    multiple: false,
                    selection: variable?.property
                      ? [
                          {
                            pool: variable.property.pool,
                            id: variable.property.id,
                          },
                        ]
                      : undefined,
                    isDisabled: (p) =>
                      !bmInternals.supportedStandardValueTypes?.includes(p.bizValueType),
                    onSubmit: async (ps) => {
                      const p = ps[0];

                      setVariable({
                        ...variable,
                        propertyPool: p.pool,
                        propertyId: p.id,
                        property: p,
                      });
                    },
                  });
                }}
              >
                {variable?.property ? (
                  <PropertyLabel showPool property={variable.property} />
                ) : (
                  t<string>("Select a property")
                )}
              </Button>
              {/* {variable?.property && ( */}
              {/*   <Chip */}
              {/*     size={'sm'} */}
              {/*     radius={'sm'} */}
              {/*     isDisabled */}
              {/*   > */}
              {/*     {t<string>(`PropertyType.${PropertyType[variable.property.type]}`)} */}
              {/*   </Chip> */}
              {/* )} */}
            </div>
            <div className={"text-right"}>
              <PropertyValueScopeSelectorLabel />
            </div>
            <div>
              <Select
                disallowEmptySelection
                dataSource={propertyValueScopes.map((s) => ({
                  label: t<string>(`PropertyValueScope.${s.label}`),
                  value: s.value,
                }))}
                placeholder={t<string>("Select a scope for property value")}
                selectedKeys={
                  variable?.scope == undefined ? undefined : [variable.scope.toString()]
                }
                selectionMode={"single"}
                size="sm"
                onSelectionChange={(v) => {
                  const scope = Array.from(v ?? [])[0] as number;

                  setVariable({
                    ...variable,
                    scope,
                  });
                }}
              />
            </div>
            <div className={"text-right"}>{t<string>("Name")}</div>
            <div>
              <Input
                isClearable
                isRequired
                placeholder={t<string>("Set a name for this variable")}
                size={"sm"}
                value={variable?.name}
                onValueChange={(v) => {
                  setVariable({
                    ...variable,
                    name: v,
                  });
                }}
              />
            </div>
          </div>
        </CardBody>
      </Card>
      <Card>
        <CardHeader>
          <div className={"flex items-center gap-1"}>
            <div>{t<string>("Preprocessing")}</div>
            <Tooltip
              content={
                <div>
                  <div>
                    {t<string>(
                      "If a preprocessing procedure is set, the variables will be preprocessed first before being used.",
                    )}
                  </div>
                  <div>{t<string>("You can add multiple preprocessing steps.")}</div>
                </div>
              }
            >
              <QuestionCircleOutlined className={"text-base"} />
            </Tooltip>
          </div>
        </CardHeader>
        <CardBody>
          {variable?.preprocesses && variable.preprocesses.length > 0 && (
            <div className={"flex flex-col gap-1 mb-2"}>
              {variable.preprocesses.map((step, i) => {
                return (
                  <ProcessStep
                    editable
                    no={i + 1}
                    property={variable.property!}
                    step={step}
                    onChange={(newStep) => {
                      variable.preprocesses![i] = newStep;
                      setVariable({
                        ...variable,
                      });
                    }}
                    onDelete={() => {
                      variable.preprocesses?.splice(i, 1);
                      setVariable({
                        ...variable,
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
              isDisabled={!variable?.property}
              size={"sm"}
              variant={"ghost"}
              onClick={() => {
                if (variable?.property) {
                  createPortal(ProcessStepModal, {
                    property: variable.property,
                    onSubmit: (operation: number, options: any) => {
                      if (!variable.preprocesses) {
                        variable.preprocesses = [];
                      }
                      variable.preprocesses.push({
                        operation,
                        options,
                      });
                      setVariable({
                        ...variable,
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

VariableModal.displayName = "VariableModal";

export default VariableModal;
