"use client";

import React, { useEffect, useMemo, useState } from "react";
import DiffMatchPatch from "diff-match-patch";
import "./index.scss";
import { useTranslation } from "react-i18next";
import { ArrowRightOutlined, QuestionCircleOutlined } from "@ant-design/icons";

import { SpecialTextType, specialTextTypes } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import {
  Button,
  Chip,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  Textarea,
  Divider,
} from "@/components/bakaui";

import type { SpecialText } from "@/pages/text/models";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DetailPage from "@/pages/text/Detail";

const tagRenders: Record<string, (t: SpecialText) => React.ReactNode> = {
  Single: (t: SpecialText) => t.value1,
  Wrapper: (t: SpecialText) => (
    <>
      {t.value1}
      <span className={"opacity-50"}>...</span>
      {t.value2}
    </>
  ),
  Value1ToValue2: (t: SpecialText) => (
    <span className={"flex items-center gap-1"}>
      {t.value1}
      <ArrowRightOutlined className={"text-small opacity-50"} />
      {t.value2}
    </span>
  ),
};

const typeTagRendersMapping: Partial<Record<SpecialTextType, (t: SpecialText) => React.ReactNode>> = {
  [SpecialTextType.Useless]: tagRenders.Single,
  [SpecialTextType.Language]: tagRenders.Value1ToValue2,
  [SpecialTextType.Wrapper]: tagRenders.Wrapper,
  [SpecialTextType.Standardization]: tagRenders.Value1ToValue2,
  [SpecialTextType.Volume]: tagRenders.Single,
  [SpecialTextType.Trim]: tagRenders.Single,
};

const typeDescriptions = {
  [SpecialTextType.Useless]:
    "Ignore the part inside the wrapper that is successfully matched by the regular expression",
  [SpecialTextType.Language]:
    "Text will be parsed as [specific language] if it surrounded by [wrappers]",
  [SpecialTextType.Wrapper]:
    "Text wrapper, used to match and extract the text within the wrapper",
  [SpecialTextType.Standardization]:
    "Treat [text1] as [text2] during analyzation",
  [SpecialTextType.Volume]: "Extract volume information from this text group",
  [SpecialTextType.Trim]: "TBD, do not set it for now",
  [SpecialTextType.DateTime]:
    "Date and time parsing template, used to extract dates and times from text",
};

const usedInMapping: Record<SpecialTextType, string[]> = {
  [SpecialTextType.Useless]: ["Text pretreatment"],
  [SpecialTextType.Language]: ["Bakabase enhancer analysis"],
  [SpecialTextType.Wrapper]: [
    "Text pretreatment",
    "Resource display name template",
    "Exhentai enhancer analysis",
  ],
  [SpecialTextType.Standardization]: ["Text pretreatment"],
  [SpecialTextType.Volume]: ["Bakabase enhancer analysis"],
  [SpecialTextType.Trim]: ["Text pretreatment"],
  [SpecialTextType.DateTime]: [
    "Bakabase enhancer analysis",
    "Parsing or converting property value",
  ],
};
const TextPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [textsMap, setTextsMap] = useState<{
    [key in SpecialTextType]?: SpecialText[];
  }>({});

  const [testInput, setTestInput] = useState<string>("");
  const [testResult, setTestResult] = useState<string>("");
  const [isRunning, setIsRunning] = useState<boolean>(false);

  const hasDiff = useMemo(() => testInput !== testResult, [testInput, testResult]);

  const renderDiffChunks = (a: string, b: string) => {
    if (!a && !b) return null;

    const dmp = new DiffMatchPatch();
    const diffs = dmp.diff_main(a || "", b || "");
    dmp.diff_cleanupSemantic(diffs);

    const left: React.ReactNode[] = [];
    const right: React.ReactNode[] = [];

    for (const [op, text] of diffs as Array<[number, string]>) {
      if (op === 0) {
        left.push(<span>{text}</span>);
        right.push(<span>{text}</span>);
      } else if (op === -1) {
        left.push(<span className="bg-danger-100 text-danger-600">{text}</span>);
      } else if (op === 1) {
        right.push(<span className="bg-success-100 text-success-700">{text}</span>);
      }
    }

    return (
      <div className="grid grid-cols-2 gap-4">
        <div className="border border-default-200 rounded-md p-2 whitespace-pre-wrap break-words text-sm">
          <div className="font-medium mb-2">{t<string>("Original")}</div>
          <div>{left}</div>
        </div>
        <div className="border border-default-200 rounded-md p-2 whitespace-pre-wrap break-words text-sm">
          <div className="font-medium mb-2">{t<string>("Pretreated")}</div>
          <div>{right}</div>
        </div>
      </div>
    );
  };

  useEffect(() => {
    loadData();
  }, []);

  const loadData = () => {
    BApi.specialText.getAllSpecialTexts().then((t) => {
      const data = t.data || {};
      const ts = specialTextTypes.reduce<{
        [key in SpecialTextType]?: SpecialText[];
      }>((s, t) => {
        const list = data[t.value] ?? [];

        list.sort((a, b) => a.value1.localeCompare(b.value1));
        s[t.value] = list.map((l) => ({
          id: l.id!,
          value1: l.value1!,
          value2: l.value2,
          type: l.type,
        }));

        return s;
      }, {});

      setTextsMap(ts);
    });
  };

  const renderDetail = (c: SpecialText) => {
    let text = c;

    createPortal(Modal, {
      defaultVisible: true,
      children: (
        <div className={"flex items-center gap-2"}>
          <DetailPage value={c} onChange={(t) => (text = t)} />
        </div>
      ),
      size: "lg",
      onOk: async () => {
        if (c.id > 0) {
          await BApi.specialText.patchSpecialText(c.id, text);
        } else {
          await BApi.specialText.addSpecialText(text);
        }
        await loadData();
      },
    });
  };

  return (
    <div className="text-page" title="Text">
      <Table isStriped removeWrapper>
        <TableHeader>
          <TableColumn>{t<string>("Type")}</TableColumn>
          <TableColumn>{t<string>("Applied to")}</TableColumn>
          <TableColumn>{t<string>("Texts")}</TableColumn>
          <TableColumn>{t<string>("Opt")}</TableColumn>
        </TableHeader>
        <TableBody>
          {Object.keys(textsMap).map((typeStr) => {
            const type = parseInt(typeStr, 10) as SpecialTextType;
            const texts = textsMap[type] ?? [];

            return (
              <TableRow>
                <TableCell>
                  <div className={"flex items-center gap-1"}>
                    {t<string>(SpecialTextType[type])}
                    <Tooltip content={t<string>(typeDescriptions[type])}>
                      <QuestionCircleOutlined className={"text-base"} />
                    </Tooltip>
                  </div>
                </TableCell>
                <TableCell>
                  <div className={"flex gap-1 flex-wrap"}>
                    {usedInMapping[type].map((x) => {
                      return (
                        <Chip
                          color={"default"}
                          radius={"sm"}
                          size={"sm"}
                          variant={"flat"}
                        >
                          {t<string>(x)}
                        </Chip>
                      );
                    })}
                  </div>
                </TableCell>
                <TableCell>
                  <div className={"flex flex-wrap gap-1"}>
                    {texts.map((c) => {
                      const renderer =
                        typeTagRendersMapping[c.type] ?? tagRenders.Single;

                      return (
                        <Chip
                          radius={"sm"}
                          variant={"bordered"}
                          onClose={() => {
                            createPortal(Modal, {
                              title: t<string>("Sure to delete?"),
                              defaultVisible: true,
                              onOk: async () => {
                                await BApi.specialText.deleteSpecialText(c.id);
                                await loadData();
                              },
                            });
                          }}
                          key={c.id}
                          // size={'sm'}
                          onClick={() => {
                            renderDetail(c);
                          }}
                        >
                          {renderer(c)}
                        </Chip>
                      );
                    })}
                  </div>
                </TableCell>
                <TableCell>
                  <Button
                    color={"primary"}
                    size={"sm"}
                    variant={"light"}
                    onClick={() =>
                      renderDetail({
                        type: type,
                        id: 0,
                        value1: "",
                      })
                    }
                  >
                    {t<string>("Add")}
                  </Button>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      <div className={"opt"}>
        <Button
          color={"primary"}
          size={"sm"}
          variant={"light"}
          onClick={() => {
            BApi.specialText.addSpecialTextPrefabs().then((a) => {
              if (!a.code) {
                loadData();
              }
            });
          }}
        >
          {t<string>("Add prefabs")}
        </Button>
      </div>
      <Divider className="my-4" />
      <div className="mt-2">
        <div className="font-medium mb-2">{t<string>("Pretreatment test")}</div>
        <Textarea
          minRows={3}
          placeholder={t<string>("Enter text")}
          value={testInput}
          onValueChange={(v) => {
            setTestInput(v);
            setTestResult("");
          }}
        />
        <div className="mt-2 flex items-center gap-2">
          <Button
            color="primary"
            size="sm"
            isLoading={isRunning}
            onClick={async () => {
              setIsRunning(true);
              try {
                const r = await BApi.specialText.pretreatText({ text: testInput });
                setTestResult(r.data ?? "");
              } finally {
                setIsRunning(false);
              }
            }}
          >
            {t<string>("Run pretreatment")}
          </Button>
        </div>
        <div className="mt-3">
          {(!testResult || !testInput) ? null : (
            hasDiff ? (
              renderDiffChunks(testInput, testResult)
            ) : (
              <div className="text-default-500 text-sm">{t<string>("No changes")}</div>
            )
          )}
        </div>
      </div>
    </div>
  );
};

TextPage.displayName = "TextPage";

export default TextPage;
