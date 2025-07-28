"use client";

import type { BulkModification as BulkModificationModel } from "@/pages/bulk-modification2/components/BulkModification";

import { useTranslation } from "react-i18next";
import { DeleteOutlined, EditOutlined } from "@ant-design/icons";
import { useCallback, useEffect, useState } from "react";
import { useUpdate } from "react-use";
import toast from "react-hot-toast";

import BulkModification from "./components/BulkModification";

import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Input,
  Modal,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useBulkModificationInternalsStore } from "@/stores/bulkModificationInternals";
import { StandardValueType } from "@/sdk/constants";
const BulkModification2Page = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const bmInternals = useBulkModificationInternalsStore.getState();

  const [expandedKeys, setExpandedKeys] = useState<string[]>(["4"]);

  const [bulkModifications, setBulkModifications] =
    useState<BulkModificationModel[]>();

  const loadAllBulkModifications = useCallback(async () => {
    const r = await BApi.bulkModification.getAllBulkModifications();
    const bms = r.data || [];

    // setExpandedKeys(bms.map(b => b.id.toString()));
    setBulkModifications(bms);
  }, []);

  useEffect(() => {
    loadAllBulkModifications();
  }, []);

  console.log(expandedKeys);

  return (
    <div>
      <div className={"flex items-center gap-2"}>
        <Button
          color={"primary"}
          size={"sm"}
          onPress={() => {
            BApi.bulkModification.addBulkModification().then((r) => {
              loadAllBulkModifications();
            });
          }}
        >
          {t<string>("Add a bulk modification")}
        </Button>
        <div className={"flex items-center gap-1"}>
          {t<string>("Under development, currently supported data types")}
          {bmInternals.supportedStandardValueTypes?.map((x) => {
            return (
              <Chip radius={"sm"} size={"sm"}>
                {t<string>(`StandardValueType.${StandardValueType[x]}`)}
              </Chip>
            );
          })}
        </div>
      </div>
      {bulkModifications ? (
        bulkModifications.length == 0 ? (
          <div className={"flex items-center justify-center min-h-[400px]"}>
            {t<string>("No data")}
          </div>
        ) : (
          <Accordion
            className={"p-0 pt-1"}
            selectedKeys={expandedKeys}
            selectionMode={"multiple"}
            variant={"splitted"}
            onSelectionChange={(keys) => {
              if (!keys) {
                setExpandedKeys([]);
              }
              setExpandedKeys(Array.from(keys).map((x) => x as string));
            }}
          >
            {bulkModifications.map((bm, i) => {
              const isExpanded = expandedKeys.includes(bm.id.toString());

              console.log(expandedKeys, isExpanded, bm.id.toString());

              return (
                <AccordionItem
                  key={bm.id.toString()}
                  //   subtitle={(
                  //     <div>
                  //       123
                  //     </div>
                  // )}
                  title={
                    <div className={"flex items-center justify-between"}>
                      <div className={"flex items-center gap-1"}>
                        <div className={"flex items-center gap-1"}>
                          {bm.name}
                          {isExpanded && (
                            <Button
                              isIconOnly
                              size={"sm"}
                              variant={"light"}
                              onPress={(e) => {
                                let newName = bm.name;

                                createPortal(Modal, {
                                  defaultVisible: true,
                                  size: "lg",
                                  title: t<string>(
                                    "Edit name of bulk modification",
                                  ),
                                  children: (
                                    <Input
                                      isRequired
                                      defaultValue={bm.name}
                                      onValueChange={(v) =>
                                        (newName = v.trim())
                                      }
                                    />
                                  ),
                                  onOk: async () => {
                                    if (newName.length == 0) {
                                      toast.error(
                                        t<string>("Name cannot be empty"),
                                      );
                                      throw new Error("Name cannot be empty");
                                    }
                                    BApi.bulkModification
                                      .patchBulkModification(bm.id, {
                                        name: newName,
                                      })
                                      .then((r) => {
                                        if (!r.code) {
                                          bm.name = newName;
                                          forceUpdate();
                                        }
                                      });
                                  },
                                });
                              }}
                            >
                              <EditOutlined className={"text-base"} />
                            </Button>
                          )}
                        </div>
                        <Chip size={"sm"}>
                          {t<string>("{{count}} resources related", {
                            count: bm.filteredResourceIds?.length || 0,
                          })}
                        </Chip>
                        {/* <Button */}
                        {/*   size={'sm'} */}
                        {/*   variant={'light'} */}
                        {/*   color={'primary'} */}
                        {/* > */}
                        {/*   {t<string>('How does this work?')} */}
                        {/* </Button> */}
                      </div>
                      <div className={"flex items-center gap-1"}>
                        <Chip
                          size={"sm"}
                          variant={"light"}
                          // color={'secondary'}
                        >
                          {bm.createdAt}
                        </Chip>
                        <Button
                          size={"sm"}
                          variant={"bordered"}
                          onClick={() => {
                            BApi.bulkModification
                              .duplicateBulkModification(bm.id)
                              .then((r) => {
                                loadAllBulkModifications();
                              });
                          }}
                        >
                          {t<string>("Duplicate")}
                        </Button>
                        <Tooltip
                          content={t<string>(
                            `Click to ${bm.isActive ? "disable" : "enable"}`,
                          )}
                        >
                          <Button
                            color={bm.isActive ? "success" : "warning"}
                            size={"sm"}
                            variant={"light"}
                            onClick={() => {
                              BApi.bulkModification
                                .patchBulkModification(bm.id, {
                                  isActive: !bm.isActive,
                                })
                                .then((r) => {
                                  loadAllBulkModifications();
                                });
                            }}
                          >
                            {t<string>(bm.isActive ? "Enabled" : "Disabled")}
                          </Button>
                        </Tooltip>
                        <Button
                          isIconOnly
                          color={"danger"}
                          size={"sm"}
                          variant={"light"}
                          onClick={() => {
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t<string>("Delete bulk modification"),
                              children: t<string>(
                                "Are you sure to delete this bulk modification?",
                              ),
                              onOk: async () => {
                                await BApi.bulkModification.deleteBulkModification(
                                  bm.id,
                                );
                                loadAllBulkModifications();
                              },
                            });
                          }}
                        >
                          <DeleteOutlined className={"text-base"} />
                        </Button>
                      </div>
                    </div>
                  }
                >
                  <BulkModification
                    bm={bm}
                    onChange={(newBm) =>
                      setBulkModifications(
                        bulkModifications.map((b) =>
                          b.id == newBm.id ? newBm : b,
                        ),
                      )
                    }
                  />
                </AccordionItem>
              );
            })}
          </Accordion>
        )
      ) : (
        <div className={"flex items-center justify-center min-h-[400px]"}>
          <Spinner size={"lg"} />
        </div>
      )}
    </div>
  );
};

BulkModification2Page.displayName = "BulkModification2Page";

export default BulkModification2Page;
