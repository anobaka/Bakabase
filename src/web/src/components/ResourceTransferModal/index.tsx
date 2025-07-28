"use client";

import type { Resource as ResourceModel } from "@/core/models/Resource";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { RecursivePartial } from "@/components/types";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  QuestionCircleOutlined,
  SearchOutlined,
} from "@ant-design/icons";
import toast from "react-hot-toast";

import {
  Checkbox,
  Chip,
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
import Resource from "@/components/Resource";
import ToResourceSelector from "@/components/ResourceTransferModal/ToResourceSelector";
import BApi from "@/sdk/BApi";
import { buildLogger, standardizePath } from "@/components/utils";
import { ResourceAdditionalItem } from "@/sdk/constants";
import AnimatedArrow from "@/components/AnimatedArrow";

type Props = {
  fromResources: ResourceModel[];
} & DestroyableProps;

const PageSize = 100;

type InputModelItem = {
  fromId: number;
  toId: number;
  keepMediaLibrary: boolean;
  deleteSourceResource: boolean;
};

const defaultInputModelItem: Partial<InputModelItem> = {
  keepMediaLibrary: true,
  deleteSourceResource: false,
};

type InputModel = {
  items: InputModelItem[];
  keepMediaLibraryForAll: boolean;
  deleteAllSourceResources: boolean;
};

const defaultInputModel: RecursivePartial<InputModel> = {
  items: [],
  keepMediaLibraryForAll: true,
  deleteAllSourceResources: false,
};

const addItem = (
  inputModel: RecursivePartial<InputModel>,
  item: RecursivePartial<InputModelItem>,
) => {
  if (!inputModel.items?.includes(item)) {
    inputModel.items ??= [];
    inputModel.items.push(item);
  }
};

type FilterForm = {
  showUnfinishedOnly?: boolean;
  pathKeyword?: string;
};

const log = buildLogger("ResourceTransferModal");

const validateInputModel = (
  inputModel: RecursivePartial<InputModel>,
): InputModel | undefined => {
  const items: InputModelItem[] = [];

  if (inputModel.items) {
    for (const item of inputModel.items) {
      if (item && item.fromId != undefined && item.toId != undefined) {
        items.push({
          fromId: item.fromId,
          toId: item.toId,
          keepMediaLibrary:
            item.keepMediaLibrary ?? defaultInputModelItem.keepMediaLibrary!,
          deleteSourceResource:
            item.deleteSourceResource ??
            defaultInputModelItem.deleteSourceResource!,
        });
      }
    }
  }
  if (items.length > 0) {
    return {
      items,
      keepMediaLibraryForAll:
        inputModel.keepMediaLibraryForAll ??
        defaultInputModel.keepMediaLibraryForAll!,
      deleteAllSourceResources:
        inputModel.deleteAllSourceResources ??
        defaultInputModel.deleteAllSourceResources!,
    };
  }

  return undefined;
};
const ResourceTransferModal = ({ fromResources, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);

  const [resources, setResources] = useState<ResourceModel[]>([]);
  const [inputModel, setInputModel] = useState<RecursivePartial<InputModel>>({
    ...defaultInputModel,
  });
  const [resourcePool, setResourcePool] = useState<
    Record<number, ResourceModel>
  >({});
  const [filterForm, setFilterForm] = useState<FilterForm>({});
  const [deletedSourceResourceIds, setDeletedSourceResourceIds] = useState<
    number[]
  >([]);

  useEffect(() => {
    const resources = fromResources
      .filter(
        (x) =>
          !deletedSourceResourceIds.includes(x.id) &&
          (!filterForm.showUnfinishedOnly ||
            inputModel.items?.find((i) => i!.fromId == x.id)?.toId ==
              undefined) &&
          (filterForm.pathKeyword == undefined ||
            filterForm.pathKeyword.length == 0 ||
            x.path.toLowerCase().includes(filterForm.pathKeyword)),
      )
      .slice((page - 1) * PageSize, page * PageSize);

    setResources(resources);
  }, [page, fromResources, filterForm, inputModel, deletedSourceResourceIds]);

  const TPagination = useCallback(({ page }: { page: number }) => {
    const total = Math.ceil(fromResources.length / PageSize);

    if (total > 1) {
      return (
        <Pagination
          page={page}
          size={"sm"}
          total={total}
          onChange={(page) => setPage(page)}
        />
      );
    }

    return null;
  }, []);

  log(inputModel, !!validateInputModel(inputModel));

  return (
    <Modal
      defaultVisible
      className={"max-h-[90%]"}
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: !validateInputModel(inputModel),
        },
      }}
      size={"full"}
      title={t<string>("Transfer data of resources")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        log("Transferring resources", inputModel);
        const dto = validateInputModel(inputModel);

        if (dto) {
          const rsp = await BApi.resource.transferResourceData(dto);

          if (!rsp.code) {
            for (const item of dto.items) {
              if (dto.deleteAllSourceResources || item.deleteSourceResource) {
                deletedSourceResourceIds.push(item.fromId);
              }
            }
            setDeletedSourceResourceIds([...deletedSourceResourceIds]);
          }
        } else {
          toast.error(t<string>("Invalid data"));
        }

        if (deletedSourceResourceIds.length != fromResources.length) {
          throw new Error("Prevent closing");
        }
      }}
    >
      <div className={"flex flex-col gap-1 max-h-full grow overflow-hidden"}>
        <div className={"flex items-center justify-between"}>
          <div className={"flex items-center gap-2"}>
            <div>
              <Input
                className={"w-[360px]"}
                placeholder={t<string>("Path keyword")}
                size={"sm"}
                startContent={<SearchOutlined className={"text-base"} />}
                value={filterForm.pathKeyword}
                onValueChange={(v) => {
                  setFilterForm({
                    ...filterForm,
                    pathKeyword: standardizePath(v?.toLowerCase()),
                  });
                }}
              />
            </div>
            <div>
              <Checkbox
                isSelected={filterForm.showUnfinishedOnly}
                size={"sm"}
                onValueChange={(c) => {
                  setFilterForm({
                    ...filterForm,
                    showUnfinishedOnly: c,
                  });
                }}
              >
                {t<string>("Show unfinished only")}
              </Checkbox>
            </div>
            <div className={"flex items-center gap-1"}>
              <Chip color={"secondary"} size={"sm"} variant={"light"}>
                {t<string>("Finished")}
              </Chip>
              {
                fromResources.filter(
                  (r) =>
                    inputModel.items?.find((i) => i!.fromId == r.id)?.toId !=
                    undefined,
                ).length
              }
              /{fromResources.length}
            </div>
            {/* <Tooltip content={( */}
            {/*   <div> */}
            {/*     <div>1.123123123123</div> */}
            {/*     <div>2.3212313213</div> */}
            {/*   </div> */}
            {/* )} */}
            {/* > */}
            {/*   <Button */}
            {/*     size={'sm'} */}
            {/*     color={'secondary'} */}
            {/*     // variant={'flat'} */}
            {/*   > */}
            {/*     {t<string>('Auto select overwritten resources')} */}
            {/*   </Button> */}
            {/* </Tooltip> */}
          </div>
          <div className={"flex items-center gap-2"}>
            <div>
              <Tooltip
                color={"secondary"}
                content={
                  <div>
                    <div className={"flex items-center gap-1"}>
                      {t<string>(
                        "Following properties will not be transferred",
                      )}
                      {[
                        "Parent association",
                        "Path",
                        "FileCreatedAt",
                        "FileModifiedAt",
                      ].map((x) => (
                        <Chip radius={"sm"} size={"sm"}>
                          {t<string>(`Property.${x}`)}
                        </Chip>
                      ))}
                    </div>
                  </div>
                }
              >
                <QuestionCircleOutlined className={"text-base"} />
              </Tooltip>
            </div>
          </div>
        </div>
        <div className={"grow overflow-y-auto"}>
          {resources.length == 0 ? (
            t<string>("No resources to be transferred")
          ) : (
            <Table isCompact removeWrapper>
              <TableHeader>
                <TableColumn width={100}>{t<string>("#")}</TableColumn>
                <TableColumn width={200}>
                  {t<string>("Source resource")}
                </TableColumn>
                <TableColumn width={60}>-</TableColumn>
                <TableColumn width={200}>
                  {t<string>("Resource will be overwritten")}
                </TableColumn>
                <TableColumn>{t<string>("Operations")}</TableColumn>
              </TableHeader>
              <TableBody>
                {resources.map((x, i) => {
                  const item = inputModel.items?.find(
                    (y) => y?.fromId == x.id,
                  ) || {
                    ...defaultInputModelItem,
                    fromId: x.id,
                  };
                  const toResource = resourcePool[item.toId ?? 0];

                  return (
                    <TableRow>
                      <TableCell>
                        <div className={"flex justify-center"}>
                          <Chip
                            color={
                              item.toId == undefined ? "default" : "success"
                            }
                            radius={"sm"}
                          >
                            {i + 1}
                          </Chip>
                        </div>
                      </TableCell>
                      <TableCell>
                        <div className={"relative"}>
                          <Resource resource={x} />
                          {(inputModel.deleteAllSourceResources ||
                            item.deleteSourceResource) && (
                            <div className={"absolute top-0 right-0"}>
                              <DeleteOutlined
                                className={"text-3xl text-danger"}
                              />
                            </div>
                          )}
                        </div>
                      </TableCell>
                      <TableCell>
                        <AnimatedArrow />
                      </TableCell>
                      <TableCell>
                        {item.toId ? (
                          toResource ? (
                            <Resource resource={toResource} />
                          ) : (
                            t<string>("Unknown resource")
                          )
                        ) : (
                          <div className={"text-warning"}>
                            {t<string>(
                              "Please select a target resource on the right side",
                            )}
                          </div>
                        )}
                      </TableCell>
                      <TableCell className={"flex flex-col"}>
                        <div className={"h-full flex flex-col gap-2"}>
                          <ToResourceSelector
                            fromResourcePath={x.path}
                            onSelect={(id) => {
                              item.toId = id;
                              BApi.resource
                                .getResourcesByKeys({
                                  ids: [id],
                                  additionalItems: ResourceAdditionalItem.All,
                                })
                                .then((res) => {
                                  const r = res.data?.[0];

                                  if (r) {
                                    setResourcePool({
                                      ...resourcePool,
                                      [id]: r,
                                    });
                                  }
                                });
                              addItem(inputModel, item);
                              setInputModel({ ...inputModel });
                            }}
                          />
                          <Checkbox
                            isDisabled={inputModel.deleteAllSourceResources}
                            isSelected={
                              inputModel.deleteAllSourceResources ||
                              item.deleteSourceResource
                            }
                            onValueChange={(v) => {
                              console.log(item);
                              item.deleteSourceResource = v;
                              addItem(inputModel, item);
                              console.log(inputModel);
                              setInputModel({ ...inputModel });
                            }}
                          >
                            {t<string>("Delete source resource (data only)")}
                          </Checkbox>
                          <Checkbox
                            isDisabled={inputModel.keepMediaLibraryForAll}
                            isSelected={
                              inputModel.keepMediaLibraryForAll ||
                              item.keepMediaLibrary
                            }
                            onValueChange={(v) => {
                              item.keepMediaLibrary = v;
                              addItem(inputModel, item);
                              setInputModel({ ...inputModel });
                            }}
                          >
                            {t<string>(
                              "Keep associated media library information for target resource",
                            )}
                          </Checkbox>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </div>
        <div className={"flex items-center justify-between"}>
          <div className={"flex items-center gap-2"}>
            <Checkbox
              isSelected={inputModel.deleteAllSourceResources}
              size={"sm"}
              onValueChange={(v) => {
                setInputModel({
                  ...inputModel,
                  deleteAllSourceResources: v,
                });
              }}
            >
              {t<string>("Delete source resource (data only)")}(
              {t<string>("For all {{count}} items", {
                count: fromResources.length,
              })}
              )
            </Checkbox>
            <Tooltip
              color={"secondary"}
              content={
                <div>
                  <div>
                    {t<string>(
                      "Category and media library of target resource will be remained by default.",
                    )}
                  </div>
                  <div>
                    {t<string>(
                      "By disable this options, the category and media library of target resource will be replaced with data from source resource.",
                    )}
                  </div>
                </div>
              }
            >
              <Checkbox
                isSelected={inputModel.keepMediaLibraryForAll}
                size={"sm"}
                onValueChange={(v) => {
                  setInputModel({
                    ...inputModel,
                    keepMediaLibraryForAll: v,
                  });
                }}
              >
                {t<string>(
                  "Keep associated media library information for target resource",
                )}
                (
                {t<string>("For all {{count}} items", {
                  count: fromResources.length,
                })}
                )
              </Checkbox>
            </Tooltip>
          </div>
          <TPagination page={page} />
        </div>
      </div>
    </Modal>
  );
};

ResourceTransferModal.displayName = "ResourceTransferModal";

export default ResourceTransferModal;
