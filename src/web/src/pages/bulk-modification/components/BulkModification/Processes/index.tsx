"use client";

import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import React, { useEffect, useState } from "react";
import { DeleteOutlined } from "@ant-design/icons";

import { Button, Chip, Divider, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ProcessModal from "@/pages/bulk-modification/components/BulkModification/ProcessModal";
import ProcessStep from "@/pages/bulk-modification/components/BulkModification/ProcessStep";
import { buildLogger } from "@/components/utils";

type Props = {
  processes?: BulkModificationProcess[];
  variables?: BulkModificationVariable[];
  onChange?: (processes: BulkModificationProcess[]) => void;
};

const log = buildLogger("Processes");
const Processes = ({ processes: propsProcesses, onChange, variables }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [processes, setProcesses] = useState<BulkModificationProcess[]>(propsProcesses ?? []);

  useEffect(() => {}, []);

  log(processes, variables);

  return (
    <div className={"grow"}>
      {processes.length > 0 && (
        <div className={"flex flex-col gap-1 mb-2"}>
          {processes.map((process, i) => {
            return (
              <React.Fragment key={i}>
                <div
                  className={
                    "flex gap-1 cursor-pointer hover:bg-[var(--bakaui-overlap-background)] rounded items-center"
                  }
                  onClick={() => {
                    createPortal(ProcessModal, {
                      process: process,
                      variables: variables,
                      onSubmit: (p) => {
                        processes[i] = p;
                        const nps = [...processes];

                        setProcesses(nps);
                        onChange?.(nps);
                      },
                    });
                  }}
                >
                  <div className={"flex items-center gap-1"}>
                    <Chip radius={"sm"} size={"sm"}>
                      {i + 1}
                    </Chip>
                    <div>
                      <Chip color={"secondary"} radius={"sm"} size={"sm"} variant={"flat"}>
                        {process.property?.poolName}
                      </Chip>
                      <Chip color={"primary"} radius={"sm"} size={"sm"} variant={"light"}>
                        {process.property?.name}
                      </Chip>
                    </div>
                    <Button
                      className={"min-w-fit px-2"}
                      color={"danger"}
                      size={"sm"}
                      variant={"light"}
                      onClick={() => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t<string>("Delete current process"),
                          children: t<string>(
                            "Are you sure you want to delete this process and all its process steps?",
                          ),
                          onOk: async () => {
                            const nps = processes.filter((_, j) => j !== i);

                            setProcesses(nps);
                            onChange?.(nps);
                          },
                        });
                      }}
                    >
                      <DeleteOutlined className={"text-base"} />
                    </Button>
                  </div>
                  <div className={"pl-2 flex flex-col gap-1"}>
                    {process.steps?.map((step, j) => {
                      log(step, variables);

                      return (
                        <ProcessStep
                          editable={false}
                          no={`${i + 1}.${j + 1}`}
                          property={process.property}
                          step={step}
                          variables={variables}
                          onDelete={() => {
                            process.steps?.splice(i, 1);
                            setProcesses([...processes]);
                            onChange?.(processes);
                          }}
                        />
                      );
                    })}
                  </div>
                </div>
                {i != processes.length - 1 && (
                  <div className={"px-2"}>
                    <Divider orientation={"horizontal"} />
                  </div>
                )}
              </React.Fragment>
            );
          })}
        </div>
      )}
      <div>
        <Button
          color={"primary"}
          size={"sm"}
          variant={"ghost"}
          onClick={() => {
            createPortal(ProcessModal, {
              variables: variables,
              onSubmit: (p) => {
                const nps = [...processes, p];

                setProcesses(nps);
                onChange?.(nps);
              },
            });
          }}
        >
          {t<string>("Add")}
        </Button>
      </div>
    </div>
  );
};

Processes.displayName = "Processes";

export default Processes;
