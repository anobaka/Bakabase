"use client";

import React, { useEffect, useReducer, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  DownloadOutlined,
  EnterOutlined,
  MergeOutlined,
  PlusCircleOutlined,
  SearchOutlined,
  ToTopOutlined,
  UploadOutlined,
} from "@ant-design/icons";

import { AliasAdditionalItem } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Divider,
  Input,
  Modal,
  Pagination,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorPanel } from "@/components/FileSystemSelector";

type Form = {
  pageSize: 20;
  pageIndex: number;
  additionalItems: AliasAdditionalItem.Candidates;
  text?: string;
  fuzzyText?: string;
};

type AliasPage = {
  originalText: string;
  text: string;
  preferred?: string;
  candidates?: string[];
};

type BulkOperationContext = {
  preferredTexts: string[];
};
const AliasPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [form, setForm] = useState<Form>({
    pageSize: 20,
    pageIndex: 0,
    additionalItems: AliasAdditionalItem.Candidates,
  });
  const [aliases, setAliases] = useState<AliasPage[]>([]);
  const [bulkOperationContext, setBulkOperationContext] =
    useState<BulkOperationContext>({ preferredTexts: [] });
  const [totalCount, setTotalCount] = useState(0);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  useEffect(() => {
    search();
  }, []);

  const search = (pForm?: Partial<Form>) => {
    const nf = {
      ...form,
      ...pForm,
    };

    setForm(nf);
    BApi.alias.searchAliasGroups(nf).then((a) => {
      setAliases(
        a.data?.map((x) => ({
          originalText: x.text!,
          text: x.text!,
          preferred: x.preferred ?? undefined,
          candidates: x.candidates ?? undefined,
        })) ?? [],
      );
      setTotalCount(a.totalCount!);
    });
  };

  const renderPagination = () => {
    const pageCount = Math.ceil(totalCount / form.pageSize);

    if (pageCount > 1) {
      return (
        <div className={"flex justify-center"}>
          <Pagination
            page={form.pageIndex}
            size={"sm"}
            total={pageCount}
            onChange={(p) => search({ pageIndex: p })}
          />
        </div>
      );
    }

    return;
  };

  const resetBulkOperationContext = () => {
    setBulkOperationContext({
      preferredTexts: [],
    });
  };

  console.log("1232131231", bulkOperationContext.preferredTexts);

  return (
    <div className="">
      <div className={"flex items-center justify-between"}>
        <div>
          <Input
            placeholder={t<string>("Press enter to search")}
            startContent={<SearchOutlined className={"text-sm"} />}
            value={form.fuzzyText}
            onKeyDown={(e) => {
              if (e.key == "Enter") {
                search();
              }
            }}
            onValueChange={(v) =>
              setForm({
                ...form,
                fuzzyText: v,
              })
            }
          />
        </div>
        <div className={"flex items-center gap-2"}>
          <Button
            color={"primary"}
            size={"sm"}
            onClick={() => {
              let value: string;

              createPortal(Modal, {
                defaultVisible: true,
                size: "lg",
                title: t<string>("Add an alias"),
                children: <Input onValueChange={(v) => (value = v)} />,
                onOk: async () => {
                  await BApi.alias.addAlias({
                    text: value,
                  });
                },
              });
            }}
          >
            <PlusCircleOutlined className={"text-base"} />
            {t<string>("Add an alias")}
          </Button>
          <Button
            color={"secondary"}
            size={"sm"}
            onClick={() => {
              createPortal(FileSystemSelectorPanel, {
                onSelected: (e) => {
                  const modal = createPortal(Modal, {
                    visible: true,
                    title: t<string>("Importing, please wait..."),
                    footer: false,
                    closeButton: false,
                  });

                  BApi.alias.importAliases({ path: e.path }).then((r) => {
                    modal.destroy();
                  });
                },
                targetType: "file",
                filter: (e) => e.isDirectory || e.path.endsWith(".csv"),
              });
            }}
          >
            <UploadOutlined className={"text-base"} />
            {t<string>("Import")}
          </Button>
          <Button
            size={"sm"}
            onClick={() => {
              BApi.alias.exportAliases();
            }}
          >
            <DownloadOutlined className={"text-base"} />
            {t<string>("Export")}
          </Button>
        </div>
      </div>
      <Divider className={"my-1"} />
      {bulkOperationContext.preferredTexts.length > 0 && (
        <>
          <Card>
            <CardHeader>
              <div className={"text-md flex items-center gap-2"}>
                {t<string>("Bulk operations")}
                <Tooltip
                  content={t<string>(
                    "{{text}} will be the preferred text in merged groups, you can change the preferred text by clicking the text.",
                    { text: bulkOperationContext.preferredTexts[0] },
                  )}
                >
                  <Button
                    color={"secondary"}
                    size={"sm"}
                    startContent={<MergeOutlined className={"text-sm"} />}
                    onClick={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("Merging alias groups: {{texts}}", {
                          texts: bulkOperationContext.preferredTexts.join(","),
                        }),
                        children: t<string>(
                          "All selected alias groups will be merged into one, and the final preferred is {{preferred}}, are you sure?",
                          { preferred: bulkOperationContext.preferredTexts[0] },
                        ),
                        onOk: async () => {
                          await BApi.alias.mergeAliasGroups({
                            preferredTexts: bulkOperationContext.preferredTexts,
                          });
                          resetBulkOperationContext();
                          search();
                        },
                      });
                    }}
                  >
                    {t<string>("Merge")}
                  </Button>
                </Tooltip>
                <Button
                  color={"danger"}
                  size={"sm"}
                  startContent={<DeleteOutlined className={"text-sm"} />}
                  onClick={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("Deleting alias groups: {{texts}}", {
                        texts: bulkOperationContext.preferredTexts.join(","),
                      }),
                      children: t<string>(
                        "All selected alias groups and its candidates will be delete and there is no way back, are you sure?",
                      ),
                      onOk: async () => {
                        await BApi.alias.deleteAliasGroups({
                          preferredTexts: bulkOperationContext.preferredTexts,
                        });
                        resetBulkOperationContext();
                        search();
                      },
                    });
                  }}
                >
                  {t<string>("Delete")}
                </Button>
                <Button
                  color={"default"}
                  size={"sm"}
                  startContent={<EnterOutlined className={"text-sm"} />}
                  onClick={() => {
                    resetBulkOperationContext();
                  }}
                >
                  {t<string>("Exit")}
                </Button>
              </div>
            </CardHeader>
            <Divider />
            <CardBody>
              <div className={"flex flex-wrap gap-1"}>
                {bulkOperationContext.preferredTexts.map((t, i) => {
                  return (
                    <Chip
                      color={i == 0 ? "primary" : "default"}
                      radius={"sm"}
                      size={"sm"}
                      onClick={() => {
                        bulkOperationContext.preferredTexts.splice(i, 1);
                        setBulkOperationContext({
                          ...bulkOperationContext,
                          preferredTexts: [
                            t,
                            ...bulkOperationContext.preferredTexts,
                          ],
                        });
                      }}
                      onClose={() => {
                        const texts =
                          bulkOperationContext.preferredTexts.filter(
                            (x) => x != t,
                          );

                        if (texts.length == 0) {
                          resetBulkOperationContext();
                        } else {
                          setBulkOperationContext({
                            ...bulkOperationContext,
                            preferredTexts: texts,
                          });
                        }
                      }}
                    >
                      {t}
                    </Chip>
                  );
                })}
              </div>
            </CardBody>
          </Card>
        </>
      )}
      {aliases.length > 0 && (
        <div className={"mt-1"}>
          <Table
            isCompact
            isStriped
            bottomContent={renderPagination()}
            color={"primary"}
            selectedKeys={bulkOperationContext.preferredTexts.filter((x) =>
              aliases.some((a) => a.text == x),
            )}
            selectionMode={"multiple"}
            topContent={renderPagination()}
            onSelectionChange={(keys) => {
              let selection: string[];

              if (keys === "all") {
                selection = aliases.map((a) => a.text);
              } else {
                selection = Array.from(keys).map(String);
              }

              const notSelected = aliases
                .map((x) => x.text)
                .filter((x) => !selection.includes(x));
              const ns = Array.from(
                new Set(
                  bulkOperationContext.preferredTexts
                    .filter((x) => !notSelected.includes(x))
                    .concat(selection),
                ),
              );

              setBulkOperationContext({
                ...bulkOperationContext,
                preferredTexts: ns,
              });
            }}
          >
            <TableHeader>
              <TableColumn>{t<string>("Preferred")}</TableColumn>
              <TableColumn>{t<string>("Candidates")}</TableColumn>
            </TableHeader>
            <TableBody>
              {aliases.map((a) => {
                return (
                  <TableRow key={a.text}>
                    <TableCell>{a.originalText}</TableCell>
                    <TableCell>
                      <div
                        className={"flex flex-wrap gap-1"}
                        onClick={(e) => {
                          e.cancelable = true;
                          e.stopPropagation();
                          e.preventDefault();
                        }}
                      >
                        {a.candidates?.map((c) => {
                          return (
                            <Tooltip
                              content={
                                <div className={"flex"}>
                                  <Button
                                    color={"success"}
                                    size={"sm"}
                                    startContent={
                                      <ToTopOutlined className={"text-sm"} />
                                    }
                                    variant={"light"}
                                    onClick={() => {
                                      BApi.alias
                                        .patchAlias(
                                          {
                                            isPreferred: true,
                                          },
                                          { text: c },
                                        )
                                        .then(() => {
                                          search();
                                        });
                                    }}
                                  >
                                    {t<string>("Set as preferred")}
                                  </Button>
                                </div>
                              }
                            >
                              <Chip
                                radius={"sm"}
                                onClose={() => {
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t<string>(
                                      "Deleting an alias: {{text}}",
                                      { text: c },
                                    ),
                                    content: t<string>(
                                      "There is no way back, are you sure?",
                                    ),
                                    onOk: async () => {
                                      await BApi.alias.deleteAlias({ text: c });
                                      a.candidates = a.candidates?.filter(
                                        (x) => x != c,
                                      );
                                      forceUpdate();
                                    },
                                  });
                                }}
                              >
                                {c}
                              </Chip>
                            </Tooltip>
                          );
                        })}
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
};

AliasPage.displayName = "AliasPage";

export default AliasPage;
