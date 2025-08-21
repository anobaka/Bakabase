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
const EnhancerSelectorModal = ({
  selectedIds: propSelectedIds,
  onSubmit,
}: Props) => {
  const { t } = useTranslation();

  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [selectedIds, setSelectedIds] = useState<number[]>(
    propSelectedIds ?? [],
  );

  const MAX_VISIBLE_TARGETS = 5;

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
    <Modal
      defaultVisible
      classNames={{ base: "max-w-[80vw]" }}
      size={"2xl"}
      onOk={() => onSubmit?.(selectedIds)}
    >
      <div className={"grid grid-cols-4 gap-2"}>
        {enhancers.map((e) => {
          const isSelected = selectedIds.includes(e.id);
          const visibleTargets = e.targets.slice(0, MAX_VISIBLE_TARGETS);
          const hiddenTargets = e.targets.slice(MAX_VISIBLE_TARGETS);

          return (
            <Card
              key={e.id}
              isPressable
              onPress={() => {
                if (isSelected) {
                  setSelectedIds(selectedIds.filter((id) => id !== e.id));
                } else {
                  setSelectedIds([...selectedIds, e.id]);
                }
              }}
              className={`relative ${
                isSelected ? "border-2 border-success bg-success/10" : ""
              }`}
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
                  {visibleTargets.map((target) => {
                    if (target.description) {
                      return (
                        <Tooltip key={`${e.id}-${target.name}`} content={target.description}>
                          <Chip
                            color={"default"}
                            radius={"sm"}
                            size={"sm"}
                            variant={"flat"}
                          >
                            <div className={"flex items-center"}>
                              {target.name}
                              <AiOutlineQuestionCircle className={"text-base"} />
                            </div>
                          </Chip>
                        </Tooltip>
                      );
                    }

                    return (
                      <Chip
                        key={`${e.id}-${target.name}`}
                        color={"default"}
                        radius={"sm"}
                        size={"sm"}
                        variant={"flat"}
                      >
                        {target.name}
                      </Chip>
                    );
                  })}

                  {hiddenTargets.length > 0 && (
                    <Tooltip
                      content={
                        <div className={"max-w-[360px] flex flex-wrap gap-1"}>
                          {hiddenTargets.map((target) => (
                            <Chip
                              key={`${e.id}-hidden-${target.name}`}
                              color={"default"}
                              radius={"sm"}
                              size={"sm"}
                              variant={"flat"}
                            >
                              {target.name}
                            </Chip>
                          ))}
                        </div>
                      }
                    >
                      <Chip color={"primary"} radius={"sm"} size={"sm"} variant={"flat"}>
                        +{hiddenTargets.length} {t<string>("more")}
                      </Chip>
                    </Tooltip>
                  )}
                </div>
              </CardBody>
              {isSelected && (
                <div className={"absolute top-2 right-2"}>
                  <Chip color={"success"} radius={"sm"} size={"sm"} variant={"solid"}>
                    {t<string>("Selected")}
                  </Chip>
                </div>
              )}
            </Card>
          );
        })}
      </div>
    </Modal>
  );
};

EnhancerSelectorModal.displayName = "EnhancerSelectorModal";

export default EnhancerSelectorModal;
