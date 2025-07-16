"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineQuestionCircle } from "react-icons/ai";

import { Card, CardBody, Chip, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";

type Props = {
  selectedIds?: number[];
  onSubmit?: (ids: number[]) => any;
} & DestroyableProps;

export default ({ selectedIds: propSelectedIds, onSubmit }: Props) => {
  const { t } = useTranslation();

  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [selectedIds, setSelectedIds] = useState<number[]>(
    propSelectedIds ?? [],
  );

  useEffect(() => {
    // createPortal(
    //   PathFilterModal, {
    //
    //   },
    // );
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      setEnhancers(r.data || []);
    });
  }, []);

  return (
    <Modal defaultVisible size={"xl"} onOk={() => onSubmit?.(selectedIds)}>
      <div className={"flex flex-col gap-2"}>
        {enhancers.map((e) => {
          const isSelected = selectedIds.includes(e.id);

          return (
            <Card
              isPressable
              onPress={() => {
                if (isSelected) {
                  setSelectedIds(selectedIds.filter((id) => id !== e.id));
                } else {
                  setSelectedIds([...selectedIds, e.id]);
                }
              }}
            >
              <CardBody>
                <div className={"text-base flex items-center gap-1"}>
                  <BriefEnhancer enhancer={e} />
                  {isSelected && (
                    <Chip
                      color={"success"}
                      radius={"sm"}
                      size={"sm"}
                      variant={"light"}
                    >
                      <AiOutlineCheckCircle className={"text-lg"} />
                    </Chip>
                  )}
                </div>
                <div className={"opacity-60"}>{e.description}</div>
                <div className={"flex flex-wrap gap-1"}>
                  <div className={"font-bold"}>
                    {t<string>("Enhance properties")}:&nbsp;
                  </div>
                  {e.targets.map((target) => {
                    if (target.description) {
                      return (
                        <Tooltip content={target.description}>
                          <Chip
                            color={"default"}
                            radius={"sm"}
                            size={"sm"}
                            variant={"flat"}
                          >
                            <div className={"flex items-center"}>
                              {target.name}
                              <AiOutlineQuestionCircle
                                className={"text-base"}
                              />
                            </div>
                          </Chip>
                        </Tooltip>
                      );
                    }

                    return (
                      <Chip
                        color={"default"}
                        radius={"sm"}
                        size={"sm"}
                        variant={"flat"}
                      >
                        {target.name}
                      </Chip>
                    );
                  })}
                </div>
              </CardBody>
            </Card>
          );
        })}
      </div>
    </Modal>
  );
};
