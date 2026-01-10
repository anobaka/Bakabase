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
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import envConfig from "@/config/env.ts";

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
  const [bulkOperationContext, setBulkOperationContext] = useState<BulkOperationContext>({
    preferredTexts: [],
  });
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
      const data = a.data?.map((x) => ({
        originalText: x.text!,
        text: x.text!,
        preferred: x.preferred ?? undefined,
        candidates: x.candidates ?? undefined,
      })) ?? [];

      setAliases(data);
      setTotalCount(a.totalCount!);

      // If current page has no data and not on first page, go to last available page
      if (data.length === 0 && nf.pageIndex > 0 && a.totalCount! > 0) {
        const lastPage = Math.max(0, Math.ceil(a.totalCount! / nf.pageSize) - 1);
        search({ pageIndex: lastPage });
      }
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

  // console.log("1232131231", bulkOperationContext.preferredTexts);

  return (
    <div className="">
      <div className={"flex items-center justify-between"}>
        <div className="flex items-center gap-2">
          <Input
            placeholder={t<string>("alias.input.searchPlaceholder")}
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
          <div className="text-xs text-default-500">
            {t<string>("alias.info.description")}
          </div>
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
                title: t<string>("alias.action.add"),
                children: (
                  <Input
                    classNames={{
                      input: "break-all",
                    }}
                    onValueChange={(v) => (value = v)}
                  />
                ),
                onOk: async () => {
                  await BApi.alias.addAlias({
                    text: value,
                  });
                  search();
                },
              });
            }}
          >
            <PlusCircleOutlined className={"text-base"} />
            {t<string>("alias.action.add")}
          </Button>
          <Button
            color={"secondary"}
            size={"sm"}
            onClick={() => {
              createPortal(FileSystemSelectorModal, {
                onSelected: (e) => {
                  const modal = createPortal(Modal, {
                    visible: true,
                    title: t<string>("alias.label.importing"),
                    footer: false,
                    closeButton: false,
                  });

                  BApi.alias.importAliases({ path: e.path }).then((r) => {
                    modal.destroy();
                  });
                },
                targetType: "file",
                filter: (e) => e.isDirectoryOrDrive || e.path.endsWith(".csv"),
              });
            }}
          >
            <UploadOutlined className={"text-base"} />
            {t<string>("alias.action.import")}
          </Button>
          <Button
            size={"sm"}
            onPress={() => {
              BApi.gui.openUrlInDefaultBrowser({
                url: `${envConfig.apiEndpoint}/alias/xlsx`,
              });
            }}
          >
            <DownloadOutlined className={"text-base"} />
            {t<string>("alias.action.export")}
          </Button>
        </div>
      </div>
      <Divider className={"my-1"} />
      {bulkOperationContext.preferredTexts.length > 0 && (
        <>
          <Card>
            <CardHeader>
              <div className={"text-md flex items-center gap-2"}>
                {t<string>("alias.label.bulkOperations")}
                <Tooltip
                  classNames={{
                    content: "max-w-md break-all whitespace-normal",
                  }}
                  content={t<string>("alias.tip.preferredText", { text: bulkOperationContext.preferredTexts[0] })}
                >
                  <Button
                    color={"secondary"}
                    size={"sm"}
                    startContent={<MergeOutlined className={"text-sm"} />}
                    onClick={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: (
                          <div className="break-all whitespace-normal">
                            {t<string>("alias.confirm.mergeTitle", {
                              texts: bulkOperationContext.preferredTexts.join(", "),
                            })}
                          </div>
                        ),
                        children: (
                          <div className="break-all whitespace-normal">
                            {t<string>("alias.confirm.mergeDescription", { preferred: bulkOperationContext.preferredTexts[0] })}
                          </div>
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
                    {t<string>("alias.action.merge")}
                  </Button>
                </Tooltip>
                <Button
                  color={"danger"}
                  size={"sm"}
                  startContent={<DeleteOutlined className={"text-sm"} />}
                  onClick={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: (
                        <div className="break-all whitespace-normal">
                          {t<string>("alias.confirm.deleteTitle", {
                            texts: bulkOperationContext.preferredTexts.join(", "),
                          })}
                        </div>
                      ),
                      children: (
                        <div className="break-all whitespace-normal">
                          {t<string>("alias.confirm.deleteDescription")}
                        </div>
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
                  {t<string>("common.action.delete")}
                </Button>
                <Button
                  color={"default"}
                  size={"sm"}
                  startContent={<EnterOutlined className={"text-sm"} />}
                  onClick={() => {
                    resetBulkOperationContext();
                  }}
                >
                  {t<string>("alias.action.exit")}
                </Button>
              </div>
            </CardHeader>
            <Divider />
            <CardBody>
              <div className={"flex flex-wrap gap-1"}>
                {bulkOperationContext.preferredTexts.map((t, i) => {
                  return (
                    <Chip
                      key={t}
                      classNames={{
                        base: "max-w-full",
                        content: "break-all whitespace-normal",
                      }}
                      color={i == 0 ? "primary" : "default"}
                      radius={"sm"}
                      size={"sm"}
                      className="h-auto py-1"
                      onClick={() => {
                        bulkOperationContext.preferredTexts.splice(i, 1);
                        setBulkOperationContext({
                          ...bulkOperationContext,
                          preferredTexts: [t, ...bulkOperationContext.preferredTexts],
                        });
                      }}
                      onClose={() => {
                        const texts = bulkOperationContext.preferredTexts.filter((x) => x != t);

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
            removeWrapper
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

              const notSelected = aliases.map((x) => x.text).filter((x) => !selection.includes(x));
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
              <TableColumn>{t<string>("alias.label.preferred")}</TableColumn>
              <TableColumn>{t<string>("alias.label.candidates")}</TableColumn>
            </TableHeader>
            <TableBody>
              {aliases.map((a) => {
                return (
                  <TableRow key={a.text}>
                    <TableCell className={"break-all max-w-xs"}>{a.originalText}</TableCell>
                    <TableCell className={"max-w-md"}>
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
                              key={c}
                              content={
                                <div className={"flex"}>
                                  <Button
                                    color={"success"}
                                    size={"sm"}
                                    startContent={<ToTopOutlined className={"text-sm"} />}
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
                                    {t<string>("alias.action.setAsPreferred")}
                                  </Button>
                                </div>
                              }
                            >
                              <Chip
                                classNames={{
                                  base: "max-w-full",
                                  content: "break-all whitespace-normal",
                                }}
                                radius={"sm"}
                                onClose={() => {
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t<string>("alias.confirm.deleteSingle"),
                                    children: (
                                      <div className="break-all whitespace-normal">
                                        <div>{t("alias.confirm.weAreDeleting")}</div>
                                        <div className="font-semibold my-2">{c}</div>
                                        <div>
                                          {t<string>("alias.confirm.noWayBack")}
                                        </div>
                                      </div>
                                    ),
                                    onOk: async () => {
                                      await BApi.alias.deleteAlias({ text: c });
                                      a.candidates = a.candidates?.filter((x) => x != c);
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
