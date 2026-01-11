"use client";

"use strict";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import StepDemonstrator from "../StepDemonstrator";

import { Button, Card, CardBody, Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import VariableModal from "@/pages/bulk-modification/components/BulkModification/VariableModal";

type Props = {
  variables?: BulkModificationVariable[];
  onChange?: (variables: BulkModificationVariable[]) => void;
};

const Variables = ({ variables: propsVariable, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [variables, setVariables] = useState<BulkModificationVariable[]>(propsVariable ?? []);

  useEffect(() => {
    setVariables(propsVariable ?? []);
  }, [propsVariable]);

  const renderVariable = (v: BulkModificationVariable, index: number) => {
    const hasPreprocesses = v.preprocesses && v.preprocesses.length > 0;

    const openEditModal = () => {
      createPortal(VariableModal, {
        variable: v,
        onChange: (updatedVar) => {
          variables[index] = updatedVar;
          const nvs = [...variables];

          setVariables(nvs);
          onChange?.(nvs);
        },
      });
    };

    const openDeleteModal = () => {
      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>("bulkModification.action.deleteVariable"),
        children: t<string>("bulkModification.confirm.deleteVariable", {
          name: v.name,
        }),
        onOk: () => {
          const nvs = variables.filter((_, idx) => idx != index);

          setVariables(nvs);
          onChange?.(nvs);
        },
      });
    };

    // Use consistent Card layout for both cases
    return (
      <Card
        isPressable
        className={"cursor-pointer hover:bg-[var(--bakaui-overlap-background)]"}
        radius={"sm"}
        shadow={"none"}
        onPress={openEditModal}
      >
        <CardBody className={"p-2 gap-1"}>
          <div className={"flex items-center gap-2"}>
            <Chip
              isCloseable
              radius={"sm"}
              size={"sm"}
              variant={"bordered"}
              onClose={(e) => {
                openDeleteModal();
              }}
            >
              {v.name}
            </Chip>
          </div>
          {hasPreprocesses && (
            <div className={"flex flex-col gap-1 pl-2"}>
              {v.preprocesses!.map((step, stepIndex) => (
                <div key={stepIndex} className={"flex items-center gap-1 flex-wrap"}>
                  <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                    {stepIndex + 1}
                  </Chip>
                  <StepDemonstrator property={v.property} step={step} variables={variables} />
                </div>
              ))}
            </div>
          )}
        </CardBody>
      </Card>
    );
  };

  return (
    <div className={"bulk-modification-variables"}>
      {variables.length > 0 && (
        <div className={"flex flex-wrap gap-2 mb-2"}>
          {variables.map((v, i) => (
            <React.Fragment key={i}>{renderVariable(v, i)}</React.Fragment>
          ))}
        </div>
      )}
      <Button
        color={"primary"}
        size={"sm"}
        variant={"ghost"}
        onPress={() => {
          createPortal(VariableModal, {
            onChange: (v) => {
              const nvs = [...variables, v];

              setVariables(nvs);
              onChange?.(nvs);
            },
          });
        }}
      >
        {t<string>("common.action.add")}
      </Button>
    </div>
  );
};

Variables.displayName = "Variables";

export default Variables;
