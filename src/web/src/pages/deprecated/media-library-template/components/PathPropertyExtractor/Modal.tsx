"use client";

import type { PathPropertyExtractor } from "@/pages/deprecated/media-library-template/models.ts";
import type { DestroyableProps } from "@/components/bakaui/types.ts";

import { useTranslation } from "react-i18next";
import { useState } from "react";
import { useUpdate } from "react-use";
import { Accordion, AccordionItem } from "@heroui/react";
import { AiOutlineDelete, AiOutlinePlusCircle } from "react-icons/ai";

import { Button, Input, Modal, Radio, RadioGroup, Select } from "@/components/bakaui";
import {
  PathPositioner,
  pathPositioners,
  PathPropertyExtractorBasePathType,
} from "@/sdk/constants.ts";

type Props = {
  locators?: PathPropertyExtractor[];
  onSubmit?: (locators: PathPropertyExtractor[]) => any;
} & DestroyableProps;
const PathPropertyExtractorModal = ({ locators: propsLocators, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const [locators, setLocators] = useState<Partial<PathPropertyExtractor>[]>(propsLocators ?? []);

  const renderPositioner = (locator: Partial<PathPropertyExtractor>) => {
    switch (locator.positioner) {
      case PathPositioner.Layer: {
        let layers: number[] = [];
        const basePathType = locator.basePathType ?? PathPropertyExtractorBasePathType.MediaLibrary;

        switch (basePathType) {
          case PathPropertyExtractorBasePathType.MediaLibrary:
            layers = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5];
            break;
          case PathPropertyExtractorBasePathType.Resource:
            layers = [0, -1, -2, -3, -4, -5, -6, -7, -8, -9];
            break;
        }

        return (
          <>
            <Select
              isRequired
              dataSource={layers.map((l) => ({
                label: l,
                value: l.toString(),
              }))}
              description={t<string>(
                "Layer 0 is {{basePathType}}, negative means before {{basePathType}}, positive means after {{basePathType}}",
                {
                  basePathType: t<string>(PathPropertyExtractorBasePathType[basePathType]),
                },
              )}
              label={t<string>("Layer")}
              selectedKeys={locator.layer == undefined ? undefined : [locator.layer.toString()]}
              onSelectionChange={(keys) => {
                locator.layer = parseInt(Array.from(keys)[0] as string, 10);
                forceUpdate();
              }}
            />
          </>
        );
      }
      case PathPositioner.Regex:
        return (
          <Input
            isRequired
            label={t<string>("Regex")}
            placeholder={t<string>("Regex to match sub path")}
            value={locator.regex}
            onValueChange={(v) => {
              locator.regex = v;
              forceUpdate();
            }}
          />
        );
      default:
        return t<string>("Not supported");
    }
  };

  const isValid = () => {
    return locators.every((locator) => {
      switch (locator.positioner) {
        case PathPositioner.Layer:
          return locator.layer != undefined;
        case PathPositioner.Regex:
          return locator.regex != undefined && locator.regex.length > 0;
        default:
          return false;
      }
    });
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: !isValid(),
        },
      }}
      size={"lg"}
      title={t<string>("Extract property values from resource path")}
      onDestroyed={onDestroyed}
      onOk={() => onSubmit?.(locators as PathPropertyExtractor[])}
    >
      <div className={"flex flex-col gap-2 min-h-0 overflow-auto"}>
        <Accordion selectedKeys={locators.map((l, i) => i.toString())} variant="splitted">
          {locators.map((locator, i) => {
            const basePathType =
              locator.basePathType ?? PathPropertyExtractorBasePathType.MediaLibrary;

            return (
              <AccordionItem
                key={i}
                title={
                  <div className={"flex items-center gap-1"}>
                    {t<string>("Rule")} {i + 1}
                    <Button
                      isIconOnly
                      color={"danger"}
                      size={"sm"}
                      variant={"light"}
                      onPress={() => {
                        locators.splice(i, 1);
                        forceUpdate();
                      }}
                    >
                      <AiOutlineDelete className={"text-base"} />
                    </Button>
                  </div>
                }
              >
                <div className={"flex flex-col gap-2"}>
                  <RadioGroup
                    isRequired
                    label={t<string>("Positioning")}
                    orientation="horizontal"
                    value={locator.positioner?.toString()}
                    onValueChange={(v) => {
                      const nv = parseInt(v, 10);

                      if (locator.positioner != nv) {
                        locators[i] = { positioner: nv };
                        forceUpdate();
                      }
                    }}
                  >
                    {pathPositioners.map((p) => (
                      <Radio value={p.value.toString()}>{t<string>(p.label)}</Radio>
                    ))}
                  </RadioGroup>
                  {locator.positioner === PathPositioner.Layer && (
                    <RadioGroup
                      isRequired
                      label={t<string>("Based on")}
                      orientation="horizontal"
                      value={basePathType?.toString()}
                      onValueChange={(v) => {
                        const nv = parseInt(v, 10);

                        if (basePathType != nv) {
                          locators[i]!.basePathType = nv;
                          locators[i]!.layer = undefined;
                          forceUpdate();
                        }
                      }}
                    >
                      {[
                        {
                          label: t<string>("Based on media library"),
                          value: PathPropertyExtractorBasePathType.MediaLibrary,
                        },
                        {
                          label: t<string>("Based on resource"),
                          value: PathPropertyExtractorBasePathType.Resource,
                        },
                      ].map((p) => (
                        <Radio value={p.value.toString()}>{p.label}</Radio>
                      ))}
                    </RadioGroup>
                  )}
                  {locator.positioner && renderPositioner(locator)}
                </div>
              </AccordionItem>
            );
          })}
        </Accordion>
      </div>
      <div className={"flex flex-col gap-1"}>
        <div>
          <Button
            color={"primary"}
            size={"sm"}
            onPress={() => {
              setLocators([...locators, { positioner: PathPositioner.Layer }]);
            }}
          >
            <AiOutlinePlusCircle className={"text-base"} />
            {t<string>("Add a rule")}
          </Button>
        </div>
        <div>
          {t<string>(
            "For layer-based rules, level 0 represents the current directory; for regex rules, the text to be matched starts from the next level under the current directory up to the resource path portion.",
          )}
        </div>
        <div>
          {t<string>("All rules will be run independently, and the results will be merged")}
        </div>
      </div>
      {/* <div>{JSON.stringify(locators)}</div> */}
    </Modal>
  );
};

PathPropertyExtractorModal.displayName = "Modal";

export default PathPropertyExtractorModal;
