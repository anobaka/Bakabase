"use client";

import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { DeleteOutlined, ExclamationCircleOutlined } from "@ant-design/icons";

import StepDemonstrator from "../StepDemonstrator";

import { Button, Card, CardBody, CardHeader, Checkbox, Chip, Divider, Modal, Switch } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ProcessModal from "@/pages/bulk-modification/components/BulkModification/ProcessModal";

type Props = {
  processes?: BulkModificationProcess[];
  variables?: BulkModificationVariable[];
  deleteResources?: boolean;
  deleteFiles?: boolean;
  onChange?: (processes: BulkModificationProcess[]) => void;
  onDeleteResourcesChange?: (deleteResources: boolean, deleteFiles: boolean) => void;
};

const Processes = ({ processes: propsProcesses, onChange, variables, deleteResources, deleteFiles, onDeleteResourcesChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [processes, setProcesses] = useState<BulkModificationProcess[]>(propsProcesses ?? []);

  useEffect(() => {
    setProcesses(propsProcesses ?? []);
  }, [propsProcesses]);

  return (
    <div className={"grow"}>
      <div className={deleteResources ? "opacity-50 pointer-events-none" : ""}>
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
        {!deleteResources && (
          <div className={"flex items-center gap-2"}>
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
        )}
      </div>
      <Divider className={"my-3"} />
      <div className={"flex flex-col gap-2"}>
        <div className={"flex items-center gap-2"}>
          <Switch
            size="sm"
            isSelected={deleteResources ?? false}
            onValueChange={(v) => {
              onDeleteResourcesChange?.(v, deleteFiles ?? false);
            }}
          >
            <span className={"text-sm font-medium text-danger"}>{t<string>("bulkModification.process.deleteResources")}</span>
          </Switch>
        </div>
        {deleteResources && (
          <div className={"flex flex-col gap-2 ml-2"}>
            <p className={"text-xs text-default-400"}>
              {t<string>("bulkModification.process.deleteResources.description")}
            </p>
            {!deleteFiles && (
              <div className={"flex items-start gap-2 p-2 rounded bg-warning-50 text-warning-700 text-sm"}>
                <ExclamationCircleOutlined className={"text-base mt-0.5 shrink-0"} />
                <span>{t<string>("bulkModification.process.deleteDataOnlyWarning")}</span>
              </div>
            )}
            <Checkbox
              size="sm"
              isSelected={deleteFiles ?? false}
              onValueChange={(v) => {
                onDeleteResourcesChange?.(true, v);
              }}
            >
              {t<string>("bulkModification.process.deleteFiles")}
            </Checkbox>
          </div>
        )}
      </div>
    </div>
  );
};

Processes.displayName = "Processes";

export default Processes;
