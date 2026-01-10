"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models.ts";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/EnhancerOptionsModal/models";
import type { DestroyableProps } from "@/components/bakaui/types.ts";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineQuestionCircle, AiOutlineSetting } from "react-icons/ai";

import { Button, Card, CardBody, Chip, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi.tsx";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer.tsx";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import EnhancerOptionsModal from "@/components/EnhancerSelectorV2/components/EnhancerOptionsModal";

type ApiEnhancerOptions = BakabaseAbstractionsModelsDomainEnhancerFullOptions;

type Props = {
  enhancerOptions?: ApiEnhancerOptions[];
  onSubmit?: (options: ApiEnhancerOptions[]) => any;
} & DestroyableProps;

const EnhancerSelectorModal = ({ enhancerOptions: propEnhancerOptions, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [enhancerOptions, setEnhancerOptions] = useState<ApiEnhancerOptions[]>(propEnhancerOptions ?? []);

  const MAX_VISIBLE_TARGETS = 5;

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      setEnhancers(r.data || []);
    });
  }, []);

  const getOptionsForEnhancer = (enhancerId: number): ApiEnhancerOptions | undefined => {
    return enhancerOptions.find((o) => o.enhancerId === enhancerId);
  };

  const isSelected = (enhancerId: number): boolean => {
    return enhancerOptions.some((o) => o.enhancerId === enhancerId);
  };

  const toggleEnhancer = (enhancerId: number) => {
    if (isSelected(enhancerId)) {
      setEnhancerOptions(enhancerOptions.filter((o) => o.enhancerId !== enhancerId));
    } else {
      setEnhancerOptions([...enhancerOptions, { enhancerId }]);
    }
  };

  const updateEnhancerOptions = (enhancerId: number, options: EnhancerFullOptions) => {
    const newOptions = enhancerOptions.map((o) => {
      if (o.enhancerId === enhancerId) {
        return {
          enhancerId,
          targetOptions: options.targetOptions as ApiEnhancerOptions["targetOptions"],
          expressions: options.expressions,
          requirements: options.requirements,
          keywordProperty: options.keywordProperty as ApiEnhancerOptions["keywordProperty"],
        };
      }
      return o;
    });
    setEnhancerOptions(newOptions);
  };

  const openEnhancerOptions = (enhancer: EnhancerDescriptor) => {
    const currentOptions = getOptionsForEnhancer(enhancer.id);
    createPortal(EnhancerOptionsModal, {
      enhancer,
      options: currentOptions ? {
        targetOptions: currentOptions.targetOptions as EnhancerFullOptions["targetOptions"],
        expressions: currentOptions.expressions ?? undefined,
        requirements: currentOptions.requirements ?? undefined,
        keywordProperty: currentOptions.keywordProperty as EnhancerFullOptions["keywordProperty"],
        pretreatKeyword: currentOptions.pretreatKeyword ?? undefined,
      } : undefined,
      onSubmit: async (options) => {
        updateEnhancerOptions(enhancer.id, options);
      },
    });
  };

  return (
    <Modal
      defaultVisible
      classNames={{ base: "max-w-[80vw]" }}
      size={"2xl"}
      title={t<string>("resourceProfile.modal.selectEnhancersTitle")}
      onDestroyed={onDestroyed}
      onOk={() => onSubmit?.(enhancerOptions)}
    >
      <div className={"grid grid-cols-4 gap-2"}>
        {enhancers.map((e) => {
          const selected = isSelected(e.id);
          const visibleTargets = e.targets.slice(0, MAX_VISIBLE_TARGETS);
          const hiddenTargets = e.targets.slice(MAX_VISIBLE_TARGETS);

          return (
            <Card
              key={e.id}
              isPressable
              className={`relative ${selected ? "border-2 border-success bg-success/10" : ""}`}
              onPress={() => toggleEnhancer(e.id)}
            >
              <CardBody>
                <div className={"text-base flex items-center gap-1"}>
                  <BriefEnhancer enhancer={e} />
                  {selected && (
                    <Chip color={"success"} radius={"sm"} size={"sm"} variant={"light"}>
                      <AiOutlineCheckCircle className={"text-lg"} />
                    </Chip>
                  )}
                </div>
                <div className={"opacity-60"}>{e.description}</div>
                <div className={"flex flex-wrap gap-1"}>
                  <div className={"font-bold"}>{t<string>("resourceProfile.label.enhanceProperties")}:&nbsp;</div>
                  {visibleTargets.map((target) => {
                    if (target.description) {
                      return (
                        <Tooltip key={`${e.id}-${target.name}`} content={target.description}>
                          <Chip color={"default"} radius={"sm"} size={"sm"} variant={"flat"}>
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
                        +{hiddenTargets.length} {t<string>("resourceProfile.label.more")}
                      </Chip>
                    </Tooltip>
                  )}
                </div>
                {selected && (
                  <div className={"mt-2 flex justify-end"}>
                    <Button
                      color={"primary"}
                      size={"sm"}
                      startContent={<AiOutlineSetting />}
                      variant={"flat"}
                      onClick={(ev) => {
                        ev.stopPropagation();
                        openEnhancerOptions(e);
                      }}
                    >
                      {t<string>("resourceProfile.action.configure")}
                    </Button>
                  </div>
                )}
              </CardBody>
              {selected && (
                <div className={"absolute top-2 right-2"}>
                  <Chip color={"success"} radius={"sm"} size={"sm"} variant={"solid"}>
                    {t<string>("resourceProfile.label.selected")}
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
