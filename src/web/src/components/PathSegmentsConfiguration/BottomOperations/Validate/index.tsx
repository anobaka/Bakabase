"use client";

import type { IPscPropertyMatcherValue } from "@/components/PathSegmentsConfiguration/models/PscPropertyMatcherValue";

import React, { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { MdCheckCircle } from "react-icons/md";

import { ResourceProperty } from "@/sdk/constants";
import { Button, Chip, Modal } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { convertToPathConfigurationDtoFromPscValue } from "@/components/PathSegmentsConfiguration/helpers";

type PropertyKey = {
  id: number;
  isCustom: boolean;
};

type ResultEntry = {
  isDirectory: boolean;
  relativePath: string;
  segmentAndMatchedValues: {
    segmentText: string;
    propertyKeys: PropertyKey[];
  }[];
  globalMatchedValues: {
    propertyKey: PropertyKey;
    textValues: string[];
  }[];
};

type TestResult = {
  resources: ResultEntry[];
  rootPath: string;
  customPropertyMap?: Record<number, { name: string }>;
};

type Props = {
  isDisabled: boolean;
  value: IPscPropertyMatcherValue[];
};
const Validate = ({ isDisabled, value }: Props) => {
  const { t } = useTranslation();

  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<TestResult>();
  const buttonRef = useRef<HTMLButtonElement>(null);

  return (
    <>
      <Button
        ref={buttonRef}
        color={"primary"}
        disabled={isDisabled}
        isLoading={loading}
        size={"small"}
        onClick={() => {
          setLoading(true);
          BApi.mediaLibrary
            .validatePathConfiguration(
              convertToPathConfigurationDtoFromPscValue(value),
            )
            .then((t) => {
              // @ts-ignore
              setResult(t.data);
            })
            .finally(() => {
              setLoading(false);
            });
        }}
      >
        {t<string>("Validate")}
      </Button>
      {buttonRef.current && (
        <Modal
          footer={{
            actions: ["cancel"],
            cancelProps: {
              children: t<string>("Close"),
            },
          }}
          portalContainer={buttonRef.current}
          size={"xl"}
          title={t<string>(
            "Found top {{count}} resources. (shows up to 100 results)",
            { count: (result?.resources || []).length },
          )}
          visible={!!result}
          onClose={() => {
            setResult(undefined);
          }}
        >
          <section className="mt-1">
            {result && result.resources && result.resources.length > 0 && (
              <div className="mt-1 flex flex-col gap-1">
                {result.resources.map((e, i) => {
                  const {
                    globalMatchedValues,
                    isDirectory,
                    segmentAndMatchedValues,
                  } = e;

                  console.log(globalMatchedValues);

                  const segments: any[] = [];

                  for (let j = 0; j < segmentAndMatchedValues?.length; j++) {
                    const ps = segmentAndMatchedValues[j];
                    const matchLabels: string[] = [];

                    if (j == segmentAndMatchedValues.length - 1) {
                      matchLabels.push(
                        t<string>(ResourceProperty[ResourceProperty.Resource]),
                      );
                    }
                    if (ps.propertyKeys?.length > 0) {
                      for (const p of ps.propertyKeys) {
                        if (p.isCustom) {
                          const pName = result.customPropertyMap?.[p.id]?.name;

                          matchLabels.push(
                            pName ?? t<string>("Unknown property"),
                          );
                        } else {
                          matchLabels.push(t<string>(ResourceProperty[p.id]));
                        }
                      }
                    }
                    // console.log(types, ps);
                    segments.push(
                      <div className={"flex flex-col"}>
                        {matchLabels.length > 0 && (
                          <div
                            className={`text-sm ${matchLabels.length > 1 ? "conflict" : ""}`}
                            style={{
                              color:
                                matchLabels.length > 1
                                  ? "var(--bakaui-danger)"
                                  : "var(--bakaui-primary)",
                            }}
                          >
                            {matchLabels.join(", ")}
                          </div>
                        )}
                        <div className="text-sm">{ps.segmentText}</div>
                      </div>,
                    );
                    if (j != e.segmentAndMatchedValues.length - 1) {
                      segments.push(
                        <span
                          className={""}
                          style={{ color: "var(--bakaui-warning)" }}
                        >
                          /
                        </span>,
                      );
                    }
                  }

                  const globalMatchesElements: any[] = [];

                  if (globalMatchedValues.length > 0) {
                    for (const gmv of globalMatchedValues) {
                      const { textValues, propertyKey } = gmv;
                      const propertyLabel = propertyKey.isCustom
                        ? (result.customPropertyMap?.[propertyKey.id]?.name ??
                          t<string>("Unknown property"))
                        : t<string>(ResourceProperty[propertyKey.id]);

                      if (textValues?.length > 0) {
                        globalMatchesElements.push(
                          <div className={"flex items-center gap-1"}>
                            <div className="text-sm font-bold">
                              {propertyLabel}
                            </div>
                            <div className="flex flex-wrap gap-1">
                              {textValues.map((v) => (
                                <Chip radius={"sm"} size={"sm"}>
                                  {v}
                                </Chip>
                              ))}
                            </div>
                          </div>,
                        );
                      }
                    }
                  }

                  return (
                    <div
                      className={
                        "flex items-center p-1 rounded border-default-200"
                      }
                    >
                      <div className="mr-2">
                        <Chip radius={"sm"} size={"sm"}>
                          {i + 1}
                        </Chip>
                      </div>
                      <div className="grow flex flex-col gap-1">
                        <div className="flex items-center gap-1">
                          <MdCheckCircle className={"text-base"} />
                          {segments}
                        </div>
                        {globalMatchesElements.length > 0 && (
                          <div className="flex items-start flex-wrap gap-1 border-default-200 pt-1">
                            {globalMatchesElements}
                          </div>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </section>
        </Modal>
      )}
    </>
  );
};

Validate.displayName = "Validate";

export default Validate;
