"use client";

"use strict";
import type { BulkModificationVariable } from "@/pages/bulk-modification2/components/BulkModification/models";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import VariableModal from "@/pages/bulk-modification2/components/BulkModification/VariableModal";

type Props = {
  variables?: BulkModificationVariable[];
  onChange?: (variables: BulkModificationVariable[]) => void;
};

export default ({ variables: propsVariable, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [variables, setVariables] = useState<BulkModificationVariable[]>(
    propsVariable ?? [],
  );

  return (
    <div className={"bulk-modification-variables"}>
      {variables.length > 0 && (
        <div className={"flex flex-wrap gap-1 mb-1"}>
          {variables.map((v, i) => {
            return (
              <Chip
                className={"cursor-pointer"}
                radius={"sm"}
                size={"sm"}
                variant={"bordered"}
                onClick={() => {
                  createPortal(VariableModal, {
                    variable: v,
                    onChange: (v) => {
                      variables[i] = v;
                      const nvs = [...variables];

                      setVariables(nvs);
                      onChange?.(nvs);
                    },
                  });
                }}
                onClose={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t<string>("Delete variable"),
                    children: t<string>(
                      "Are you sure to delete variable {{name}}?",
                      { name: v.name },
                    ),
                    onOk: () => {
                      const nvs = variables.filter((v, idx) => idx != i);

                      setVariables(nvs);
                      onChange?.(nvs);
                    },
                  });
                }}
              >
                {v.name}
              </Chip>
            );
          })}
        </div>
      )}
      <Button
        color={"primary"}
        size={"sm"}
        variant={"ghost"}
        onClick={() => {
          createPortal(VariableModal, {
            onChange: (v) => {
              const nvs = [...variables, v];

              setVariables(nvs);
              onChange?.(nvs);
            },
          });
        }}
      >
        {t<string>("Add")}
      </Button>
    </div>
  );
};
