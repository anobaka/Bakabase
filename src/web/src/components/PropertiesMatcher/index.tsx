"use client";

import type { IProperty } from "../Property/models";
import type { PropertyType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { useCallback, useState } from "react";
import { IoLocate } from "react-icons/io5";
import { MdAutoFixHigh } from "react-icons/md";
import { AiOutlineSearch } from "react-icons/ai";

import { Button, Tooltip, Modal, Card } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BriefProperty from "../Chips/Property/BriefProperty";

type Property = {
  name: string;
  type: PropertyType;
  options?: any;
};

type Props = {
  properties: Property[];
  onValueChanged?: (properties: (IProperty | undefined)[]) => any;
};
const PropertiesMatcher = ({
  properties,
  onValueChanged: propsOnValueChanged,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [isFindingBestMatch, setIsFindingBestMatch] = useState(false);

  const onValueChange = useCallback(
    (properties: (IProperty | undefined)[]) => {
      console.log(properties);
      propsOnValueChanged?.(properties);
    },
    [propsOnValueChanged],
  );

  return (
    <Tooltip content={t<string>("propertyMatcher.autoMatch.tooltip")}>
      <Button
        isIconOnly
        color={"primary"}
        size={"sm"}
        variant={"light"}
        isLoading={isFindingBestMatch}
        onPress={async () => {
          setIsFindingBestMatch(true);
          try {
            const results = await Promise.all(
              properties.map((property) =>
                BApi.property.findBestMatchingProperty({
                  type: property.type,
                  name: property.name,
                }),
              ),
            );

            const ret: (IProperty | undefined)[] = results.map(
              (r) => r.data,
            );

            const missingIndexes = ret
              .map((p, idx) => (p ? -1 : idx))
              .filter((idx) => idx !== -1) as number[];

            if (missingIndexes.length === 0) {
              onValueChange(ret);
              return;
            }

            const modal = createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("propertyMatcher.partialMatch.title"),
              size: "lg",
              children: (
                <div className={"flex flex-col gap-3"}>
                  <div className={"flex flex-col gap-1"}>
                    <div className={"font-semibold"}>{t<string>("propertyMatcher.autoMatched.label")}</div>
                    <div className={"flex flex-wrap items-center gap-1 text-sm"}>
                      {ret.map((p, idx) => (p ? <BriefProperty key={`matched-${idx}`} property={p} /> : null))}
                    </div>
                  </div>
                  <div className={"flex flex-col gap-1"}>
                    <div className={"font-semibold"}>{t<string>("propertyMatcher.missing.label")}</div>
                    <div className={"flex flex-wrap items-center gap-1 text-sm text-warning-500"}>
                      {missingIndexes.map((idx) => (
                        <BriefProperty key={`missing-${idx}`} property={properties[idx]!} />
                      ))}
                    </div>
                  </div>
                  {t<string>("propertyMatcher.noMatch.youCan")}:
                  <div className={"grid grid-cols-2 gap-2"}>
                    <Card
                      isPressable
                      className={
                        "relative flex flex-col items-center gap-2 justify-center py-3 border-2 border-primary-500 bg-primary-50 hover:bg-primary-100 shadow-sm"
                      }
                      onPress={async () => {
                        // Auto-create all missing properties (batch)
                        const models = missingIndexes.map((idx) => {
                          const p = properties[idx]!;
                          return {
                            name: p.name,
                            type: p.type,
                            options: p.options
                              ? JSON.stringify(p.options)
                              : undefined,
                          } as any;
                        });
                        const r = await BApi.customProperty.addCustomPropertyBatch(models);
                        if (r.code) {
                          throw new Error(r.message);
                        }
                        const created = r.data || [];
                        created.forEach((cp, i) => {
                          const idx = missingIndexes[i]!;
                          ret[idx] = cp;
                        });
                        onValueChange(ret);
                        modal.destroy();
                      }}
                    >
                      <span className={"absolute top-1 right-1 rounded-full bg-primary-500 text-white text-[10px] px-2 py-0.5"}>
                        {t<string>("propertyMatcher.recommended.label")}
                      </span>
                      <MdAutoFixHigh className={"text-2xl text-primary-600"} />
                      <div className={"text-sm font-medium text-primary-700 text-center px-2"}>
                        {t<string>("propertyMatcher.autoCreateMissing.label")}
                      </div>
                    </Card>
                    <Card
                      isPressable={missingIndexes.length > 0}
                      className={
                        `relative flex flex-col items-center gap-2 justify-center py-2 border border-default-200 bg-content1 text-default-600 hover:bg-content2 ${
                          missingIndexes.length === 0 ? "opacity-50 pointer-events-none" : ""
                        }`
                      }
                      onPress={() => {
                        // Close and let user handle manually elsewhere
                        onValueChange(ret);
                        modal.destroy();
                      }}
                    >
                      <AiOutlineSearch className={"text-2xl text-default-500"} />
                      <div className={"text-sm text-default-700 text-center px-2"}>
                        {t<string>("propertyMatcher.keepMatched.label")}
                      </div>
                    </Card>
                  </div>
                </div>
              ),
              footer: {
                actions: ["cancel"],
              },
            });
          } finally {
            setIsFindingBestMatch(false);
          }
        }}
      >
        <IoLocate className={"text-base"} />
      </Button>
    </Tooltip>
  );
};

PropertiesMatcher.displayName = "PropertiesMatcher";

export default PropertiesMatcher;
