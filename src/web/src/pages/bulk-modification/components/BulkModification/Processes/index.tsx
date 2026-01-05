"use client";

import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { DeleteOutlined } from "@ant-design/icons";

import StepDemonstrator from "../StepDemonstrator";

import { Button, Card, CardBody, CardHeader, Chip, Divider, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ProcessModal from "@/pages/bulk-modification/components/BulkModification/ProcessModal";

type Props = {
  processes?: BulkModificationProcess[];
  variables?: BulkModificationVariable[];
  onChange?: (processes: BulkModificationProcess[]) => void;
};

const Processes = ({ processes: propsProcesses, onChange, variables }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [processes, setProcesses] = useState<BulkModificationProcess[]>(propsProcesses ?? []);

  useEffect(() => {
    setProcesses(propsProcesses ?? []);
  }, [propsProcesses]);

  return (
    <div className={"grow"}>
      {processes.length > 0 && (
        <div className={"flex flex-col gap-2 mb-2"}>
          {processes.map((process, i) => {
            return (
              <Card
                key={i}
                isPressable
                className={"cursor-pointer hover:bg-[var(--bakaui-overlap-background)]"}
                radius={"sm"}
                shadow={"none"}
                onPress={() => {
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
                <CardHeader className={"p-2 pb-0 gap-2"}>
                  <Chip radius={"sm"} size={"sm"}>
                    {i + 1}
                  </Chip>
                  <div className={"flex items-center gap-1 flex-1"}>
                    <Chip color={"secondary"} radius={"sm"} size={"sm"} variant={"flat"}>
                      {process.property?.poolName}
                    </Chip>
                    <span className={"text-small"}>›</span>
                    <Chip color={"primary"} radius={"sm"} size={"sm"} variant={"light"}>
                      {process.property?.name}
                    </Chip>
                  </div>
                  <Button
                    className={"min-w-fit px-2"}
                    color={"danger"}
                    size={"sm"}
                    variant={"light"}
                    onPress={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("bulkModification.action.deleteProcess"),
                        children: t<string>("bulkModification.confirm.deleteProcess"),
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
                </CardHeader>
                {process.steps && process.steps.length > 0 && (
                  <>
                    <Divider className={"my-1"} />
                    <CardBody className={"p-2 pt-0 gap-1"}>
                      {process.steps.map((step, j) => (
                        <div key={j} className={"flex items-center gap-1 flex-wrap"}>
                          <Chip radius={"sm"} size={"sm"} variant={"flat"}>
                            {i + 1}.{j + 1}
                          </Chip>
                          <StepDemonstrator
                            property={process.property}
                            step={step}
                            variables={variables}
                          />
                        </div>
                      ))}
                    </CardBody>
                  </>
                )}
              </Card>
            );
          })}
        </div>
      )}
      <div>
        <Button
          color={"primary"}
          size={"sm"}
          variant={"ghost"}
          onPress={() => {
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
          {t<string>("common.action.add")}
        </Button>
      </div>
    </div>
  );
};

Processes.displayName = "Processes";

export default Processes;
