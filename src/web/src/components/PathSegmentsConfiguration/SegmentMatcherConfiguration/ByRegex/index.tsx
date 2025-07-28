"use client";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  InfoCircleOutlined,
  QuestionCircleOutlined,
  WarningOutlined,
} from "@ant-design/icons";

import { getResultFromExecAll } from "../../helpers";

import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Input,
  Radio,
  RadioGroup,
  Tooltip,
} from "@/components/bakaui";
import { splitPathIntoSegments } from "@/components/utils";

type Props = {
  modeIsSelected: boolean;
  onSelectMode: () => void;
  onRegexChange: (regex: string) => void;
  regex?: string;
  textToBeMatched?: string;
  isResourceProperty: boolean;
};
const ByRegex = ({
  onRegexChange,
  modeIsSelected,
  onSelectMode,
  regex,
  textToBeMatched,
  isResourceProperty,
}: Props) => {
  const { t } = useTranslation();

  const [testResult, setTestResult] = useState<{
    success: boolean;
    error?: string;
    groups?: string[];
    text?: string;
    tip?: string;
    index?: number;
  }>();
  const [value, setValue] = useState(regex);

  if (!textToBeMatched) {
    return null;
  }

  const renderTestResult = () => {
    if (testResult) {
      if (!testResult.success) {
        return (
          <div className={"mt-1"}>
            {testResult.error && (
              <>
                <Chip
                  color={"danger"}
                  size={"sm"}
                  startContent={<WarningOutlined className={"text-sm"} />}
                  variant={"light"}
                >
                  {testResult.error}
                </Chip>
                <br />
              </>
            )}
            <Chip color={"danger"} size={"sm"} variant={"light"}>
              {t<string>("Test failed, please check your regex")}
            </Chip>
          </div>
        );
      } else {
        let { tip } = testResult;
        let values: string[] | undefined;

        if (testResult.groups != undefined) {
          values = testResult.groups;
        } else {
          if (testResult.text != undefined && testResult.text.length > 0) {
            const sub = textToBeMatched.substring(
              0,
              testResult.index! + testResult.text!.length,
            );
            const segments = splitPathIntoSegments(sub);
            const match =
              splitPathIntoSegments(textToBeMatched)[segments.length - 1];

            values = match == undefined ? [] : [match];
          }
        }

        return (
          <div className="mt-1">
            {tip && (
              <Chip
                color={"warning"}
                size={"sm"}
                startContent={<InfoCircleOutlined className={"text-sm"} />}
                variant={"light"}
              >
                {tip}
              </Chip>
            )}
            {values && values.length > 0 && (
              <div className="flex flex-wrap gap-1 items-center">
                <div className="font-bold">{t<string>("Match result")}</div>
                {values.map((v) => (
                  <Chip radius={"sm"} size={"sm"}>
                    {v}
                  </Chip>
                ))}
              </div>
            )}
          </div>
        );
      }
    }

    return;
  };

  return (
    <Card isHoverable className="mb-2 cursor-pointer w-full">
      <CardHeader className="text-lg font-bold">
        <RadioGroup
          value={modeIsSelected ? "regex" : ""}
          onValueChange={onSelectMode}
        >
          <Radio value={"regex"}>
            {t<string>("Set by {{thing}}", { thing: t<string>("regex") })}
          </Radio>
        </RadioGroup>
        <Tooltip
          content={
            <div>
              {t<string>("/ is the directory separator always, not \\")}
              <br />
              {t<string>(
                "The whole matched text will be ignored if capturing groups are used",
              )}
              <br />
              {t<string>(
                "You should not use capturing groups on Resource property due to partial path is not available to match a file system entry",
              )}
            </div>
          }
        >
          <QuestionCircleOutlined className={"text-base ml-2"} />
        </Tooltip>
      </CardHeader>
      <CardBody>
        <div>
          <div className="flex items-center gap-2 mb-2">
            <span>{t<string>("Text to be matched")}</span>
            <Chip color={"success"} size={"sm"} variant={"light"}>
              {textToBeMatched}
            </Chip>
          </div>
          <div className={"flex items-center gap-1"}>
            <Input
              isClearable
              aria-label="please input"
              style={{ width: "100%" }}
              value={value}
              onClear={() => {
                setValue(undefined);
              }}
              onClick={() => {
                if (!modeIsSelected) {
                  onSelectMode();
                }
              }}
              onValueChange={(v) => {
                setValue(v);
              }}
            />
            <Button
              disabled={value == undefined}
              onClick={() => {
                if (value) {
                  try {
                    const v = getResultFromExecAll(
                      new RegExp(value),
                      textToBeMatched,
                    );

                    console.log(v, isResourceProperty);
                    if (
                      v &&
                      v.groups &&
                      v.groups.length > 0 &&
                      isResourceProperty
                    ) {
                      throw new Error(
                        t<string>(
                          "Capturing groups are not allowed on Resource property",
                        ),
                      );
                    }

                    let tip: string | undefined;

                    if (v) {
                      if (v.groups && v.groups.length > 0) {
                        tip = t<string>(
                          "Capturing groups are used, only matched text will be applied",
                        );
                      } else {
                        if (v.text != undefined && v.text.length > 0) {
                          tip = t<string>(
                            "Whole segment will be applied if capturing group is not used",
                          );
                        }
                      }
                    } else {
                      tip = t<string>(
                        "No text was matched, but you can still save the regular expression.",
                      );
                    }

                    setTestResult({
                      success: true,
                      ...v,
                      tip,
                    });

                    onRegexChange(value);
                  } catch (e) {
                    setTestResult({
                      success: false,
                      error: e.message,
                    });
                  }
                }
              }}
            >
              {t<string>("Test")}
            </Button>
          </div>
          {renderTestResult()}
        </div>
      </CardBody>
    </Card>
  );
};

ByRegex.displayName = "ByRegex";

export default ByRegex;
