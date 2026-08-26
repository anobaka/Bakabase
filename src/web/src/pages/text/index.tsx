"use client";

import React, { useEffect, useMemo, useState } from "react";
import DiffMatchPatch from "diff-match-patch";
import "./index.scss";
import { useTranslation } from "react-i18next";
import { ArrowRightOutlined } from "@ant-design/icons";

import { TextTypeShape, WellKnownTextType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import {
  Button,
  Chip,
  Input,
  Modal,
  Select,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Textarea,
  Divider,
} from "@/components/bakaui";

import type { TextEntry, TextType } from "@/pages/text/models";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DetailPage from "@/pages/text/Detail";

/** How an entry reads depends on its type's shape, so rendering keys off that rather than the type. */
const entryRenders: Record<TextTypeShape, (t: TextEntry) => React.ReactNode> = {
  [TextTypeShape.Values]: (t) => t.value1,
  [TextTypeShape.DelimiterPair]: (t) => (
    <>
      {t.value1}
      <span className={"opacity-50"}>...</span>
      {t.value2}
    </>
  ),
  [TextTypeShape.MappingPair]: (t) => (
    <span className={"flex items-center gap-1"}>
      {t.value1}
      <ArrowRightOutlined className={"text-small opacity-50"} />
      {t.value2}
    </span>
  ),
};

const typeDescriptions: Partial<Record<WellKnownTextType, string>> = {
  [WellKnownTextType.Useless]: "text.typeDescription.useless",
  [WellKnownTextType.Language]: "text.typeDescription.language",
  [WellKnownTextType.Wrapper]: "text.typeDescription.wrapper",
  [WellKnownTextType.Standardization]: "text.typeDescription.standardization",
  [WellKnownTextType.Volume]: "text.typeDescription.volume",
  [WellKnownTextType.Trim]: "text.typeDescription.trim",
  [WellKnownTextType.DateTime]: "text.typeDescription.dateTime",
};

const usedInMapping: Partial<Record<WellKnownTextType, string[]>> = {
  [WellKnownTextType.Useless]: ["text.usedIn.textPretreatment"],
  [WellKnownTextType.Language]: ["text.usedIn.bakabaseEnhancerAnalysis"],
  [WellKnownTextType.Wrapper]: [
    "text.usedIn.textPretreatment",
    "text.usedIn.resourceDisplayNameTemplate",
    "text.usedIn.exhentaiEnhancerAnalysis",
  ],
  [WellKnownTextType.Standardization]: ["text.usedIn.textPretreatment"],
  [WellKnownTextType.Volume]: ["text.usedIn.bakabaseEnhancerAnalysis"],
  [WellKnownTextType.Trim]: ["text.usedIn.textPretreatment"],
  [WellKnownTextType.DateTime]: [
    "text.usedIn.bakabaseEnhancerAnalysis",
    "text.usedIn.parsingOrConvertingPropertyValue",
  ],
};

const shapeLabels: Record<TextTypeShape, string> = {
  [TextTypeShape.Values]: "text.shape.values",
  [TextTypeShape.DelimiterPair]: "text.shape.delimiterPair",
  [TextTypeShape.MappingPair]: "text.shape.mappingPair",
};

const TextPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [types, setTypes] = useState<TextType[]>([]);
  const [entriesMap, setEntriesMap] = useState<Record<number, TextEntry[]>>({});

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
          <div className="font-medium mb-2">{t<string>("text.label.original")}</div>
          <div>{left}</div>
        </div>
        <div className="border border-default-200 rounded-md p-2 whitespace-pre-wrap break-words text-sm">
          <div className="font-medium mb-2">{t<string>("text.label.pretreated")}</div>
          <div>{right}</div>
        </div>
      </div>
    );
  };

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    const r = await BApi.text.getAllTextTypes();
    const list = (r.data ?? []) as TextType[];

    setTypes(list);

    const entries = await Promise.all(
      list.map(async (type) => {
        const er = await BApi.text.getTextEntries(type.id);
        const items = ((er.data ?? []) as TextEntry[])
          .slice()
          .sort((a, b) => a.value1.localeCompare(b.value1));

        return [type.id, items] as const;
      }),
    );

    setEntriesMap(Object.fromEntries(entries));
  };

  const editEntry = (type: TextType, entry: TextEntry) => {
    let draft = entry;

    createPortal(Modal, {
      defaultVisible: true,
      title: type.name,
      children: (
        <div className={"flex items-center gap-2"}>
          <DetailPage shape={type.shape} value={entry} onChange={(v) => (draft = v)} />
        </div>
      ),
      size: "lg",
      onOk: async () => {
        if (draft.id > 0) {
          await BApi.text.patchTextEntry(draft.id, {
            value1: draft.value1,
            value2: draft.value2,
          });
        } else {
          await BApi.text.addTextEntry(type.id, {
            value1: draft.value1,
            value2: draft.value2,
          });
        }
        await loadData();
      },
    });
  };

  const createType = () => {
    let name = "";
    let shape: TextTypeShape = TextTypeShape.Values;
    let description = "";

    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("text.action.addType"),
      children: (
        <div className={"flex flex-col gap-2"}>
          <Input
            required
            label={t<string>("text.label.typeName")}
            onValueChange={(v) => (name = v)}
          />
          <Select
            defaultSelectedKeys={[String(TextTypeShape.Values)]}
            dataSource={Object.keys(shapeLabels).map((k) => ({
              label: t<string>(shapeLabels[parseInt(k, 10) as TextTypeShape]),
              value: k,
            }))}
            label={t<string>("text.label.shape")}
            onSelectionChange={(keys) => {
              const key = Array.from(keys ?? [])[0];

              if (key != undefined) {
                shape = parseInt(key.toString(), 10) as TextTypeShape;
              }
            }}
          />
          <Input
            label={t<string>("text.label.typeDescription")}
            onValueChange={(v) => (description = v)}
          />
        </div>
      ),
      onOk: async () => {
        await BApi.text.addTextType({ name, shape, description });
        await loadData();
      },
    });
  };

  const renameType = (type: TextType) => {
    let name = type.name;

    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("text.action.renameType"),
      children: (
        <Input
          required
          defaultValue={type.name}
          label={t<string>("text.label.typeName")}
          onValueChange={(v) => (name = v)}
        />
      ),
      onOk: async () => {
        await BApi.text.renameTextType(type.id, { name });
        await loadData();
      },
    });
  };

  const deleteType = (type: TextType) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("text.confirm.deleteTypeTitle"),
      children: t<string>("text.confirm.deleteTypeMessage", { name: type.name }),
      onOk: async () => {
        await BApi.text.deleteTextType(type.id);
        await loadData();
      },
    });
  };

  return (
    <div className="text-page" title="Text">
      <Table isStriped removeWrapper>
        <TableHeader>
          <TableColumn>{t<string>("common.label.type")}</TableColumn>
          <TableColumn>{t<string>("text.label.appliedTo")}</TableColumn>
          <TableColumn>{t<string>("text.label.texts")}</TableColumn>
          <TableColumn>{t<string>("text.label.opt")}</TableColumn>
        </TableHeader>
        <TableBody>
          {types.map((type) => {
            const entries = entriesMap[type.id] ?? [];
            const render = entryRenders[type.shape] ?? entryRenders[TextTypeShape.Values];
            const usedIn = type.wellKnown == undefined ? [] : (usedInMapping[type.wellKnown] ?? []);
            const description =
              type.wellKnown == undefined
                ? type.description
                : t<string>(typeDescriptions[type.wellKnown] ?? "");

            return (
              <TableRow key={type.id}>
                <TableCell>
                  <div className={"flex flex-col"}>
                    <span className={"flex items-center gap-1"}>
                      {type.wellKnown == undefined
                        ? type.name
                        : t<string>(`WellKnownTextType.${WellKnownTextType[type.wellKnown]}`)}
                      {type.wellKnown != undefined && (
                        <Chip color={"default"} radius={"sm"} size={"sm"} variant={"flat"}>
                          {t<string>("text.label.builtin")}
                        </Chip>
                      )}
                    </span>
                    <span className={"text-xs text-default-400"}>{description}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <div className={"flex gap-1 flex-wrap"}>
                    {usedIn.map((x, xi) => (
                      <Chip key={xi} color={"default"} radius={"sm"} size={"sm"} variant={"flat"}>
                        {t<string>(x)}
                      </Chip>
                    ))}
                  </div>
                </TableCell>
                <TableCell>
                  <div className={"flex flex-wrap gap-1"}>
                    {entries.map((c) => (
                      <Chip
                        key={c.id}
                        radius={"sm"}
                        variant={"bordered"}
                        onClick={() => editEntry(type, c)}
                        onClose={() => {
                          createPortal(Modal, {
                            title: t<string>("text.confirm.deleteTitle"),
                            defaultVisible: true,
                            onOk: async () => {
                              await BApi.text.deleteTextEntry(c.id);
                              await loadData();
                            },
                          });
                        }}
                      >
                        {render(c)}
                      </Chip>
                    ))}
                  </div>
                </TableCell>
                <TableCell>
                  <div className={"flex items-center gap-1"}>
                    <Button
                      color={"primary"}
                      size={"sm"}
                      variant={"light"}
                      onClick={() =>
                        editEntry(type, { id: 0, typeId: type.id, value1: "", value2: "" })
                      }
                    >
                      {t<string>("common.action.add")}
                    </Button>
                    {type.wellKnown == undefined && (
                      <>
                        <Button size={"sm"} variant={"light"} onClick={() => renameType(type)}>
                          {t<string>("common.action.rename")}
                        </Button>
                        <Button
                          color={"danger"}
                          size={"sm"}
                          variant={"light"}
                          onClick={() => deleteType(type)}
                        >
                          {t<string>("common.action.delete")}
                        </Button>
                      </>
                    )}
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      <div className={"opt flex items-center gap-2"}>
        <Button color={"primary"} size={"sm"} variant={"light"} onClick={createType}>
          {t<string>("text.action.addType")}
        </Button>
        <Button
          color={"primary"}
          size={"sm"}
          variant={"light"}
          onClick={() => {
            BApi.text.ensureTextSeeds().then((a) => {
              if (!a.code) {
                loadData();
              }
            });
          }}
        >
          {t<string>("text.action.addPrefabs")}
        </Button>
      </div>
      <Divider className="my-4" />
      <div className="mt-2">
        <div className="font-medium mb-2">{t<string>("text.label.pretreatmentTest")}</div>
        <Textarea
          minRows={3}
          placeholder={t<string>("text.input.enterText")}
          value={testInput}
          onValueChange={(v) => {
            setTestInput(v);
            setTestResult("");
          }}
        />
        <div className="mt-2 flex items-center gap-2">
          <Button
            color="primary"
            isLoading={isRunning}
            size="sm"
            onClick={async () => {
              setIsRunning(true);
              try {
                const r = await BApi.text.cleanText({ text: testInput });

                setTestResult(r.data ?? "");
              } finally {
                setIsRunning(false);
              }
            }}
          >
            {t<string>("text.action.runPretreatment")}
          </Button>
        </div>
        <div className="mt-3">
          {!testResult || !testInput ? null : hasDiff ? (
            renderDiffChunks(testInput, testResult)
          ) : (
            <div className="text-default-500 text-sm">{t<string>("text.label.noChanges")}</div>
          )}
        </div>
      </div>
    </div>
  );
};

TextPage.displayName = "TextPage";

export default TextPage;
