"use client";

import { MenuItem } from "@szhsin/react-menu";
import React from "react";
import { useTranslation } from "react-i18next";
import {
  ApiOutlined,
  DeleteOutlined,
  ExportOutlined,
  FileSyncOutlined,
  SendOutlined,
} from "@ant-design/icons";
import { useNavigate } from "react-router-dom";

import MediaLibraryPathSelectorV2 from "@/components/MediaLibraryPathSelectorV2";
import MediaLibrarySelectorV2 from "@/components/MediaLibrarySelectorV2";
import { ResourceAdditionalItem } from "@/sdk/constants";
import ResourceTransferModal from "@/components/ResourceTransferModal";
import { Checkbox, Modal } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

const log = buildLogger("ResourceContextMenuItems");

type Props = {
  selectedResourceIds: number[];
  onSelectedResourcesChanged?: (ids: number[]) => any;
};
const ContextMenuItems = ({
  selectedResourceIds,
  onSelectedResourcesChanged,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  return (
    <>
      <MenuItem
        onClick={() => {
          log("inner", "click");
          createPortal(MediaLibraryPathSelectorV2, {
            confirmation: true,
            onSelect: (id, path, isLegacyMediaLibrary) => {
              if (selectedResourceIds.length > 0) {
                BApi.resource
                  .moveResources({
                    ids: selectedResourceIds,
                    path,
                    mediaLibraryId: id,
                    isLegacyMediaLibrary,
                  })
                  .then((r) => {
                    // todo: moving files is a asynchronized operation, we need some way to update other resources after moving
                    // onSelectedResourcesChanged?.(selectedResourceIds);
                  });
              }
            },
          });
        }}
        onClickCapture={() => {
          log("inner", "click capture");
        }}
      >
        <div className={"flex items-center gap-2"}>
          <FileSyncOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>(
                "Move {{count}} resources to media library (Including file system entries)",
                { count: selectedResourceIds.length },
              )
            : t<string>(
                "Move to media library (Including file system entries)",
              )}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          log("inner", "click");
          createPortal(MediaLibrarySelectorV2, {
            confirmation: true,
            onSelect: async (id, isLegacyMediaLibrary) => {
              await BApi.resource
                .moveResources({
                  ids: selectedResourceIds,
                  mediaLibraryId: id,
                  isLegacyMediaLibrary,
                })
                .then((r) => {
                  onSelectedResourcesChanged?.(selectedResourceIds);
                });
            },
          });
        }}
        onClickCapture={() => {
          log("inner", "click capture");
        }}
      >
        <div className={"flex items-center gap-2"}>
          <ApiOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>(
                "Move {{count}} resources to media library (Data only)",
                { count: selectedResourceIds.length },
              )
            : t<string>("Move to media library (Data only)")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          log("inner", "click");
          BApi.resource
            .getResourcesByKeys({
              ids: selectedResourceIds,
              additionalItems: ResourceAdditionalItem.All,
            })
            .then((r) => {
              const resources = r.data || [];

              createPortal(ResourceTransferModal, {
                fromResources: resources,
              });
            });
        }}
        onClickCapture={() => {
          log("inner", "click capture");
        }}
      >
        <div className={"flex items-center gap-2"}>
          <SendOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("Transfer data of {{count}} resources", {
                count: selectedResourceIds.length,
              })
            : t<string>("Transfer resource data")}
        </div>
      </MenuItem>
      {selectedResourceIds.length > 1 && (
        <MenuItem
          onClick={() => {
            log("inner", "click");
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("We are leaving current page"),
              onOk: async () => {
                const navigate = useNavigate();

                navigate("/bulkmodification2");
              },
            });
          }}
          onClickCapture={() => {
            log("inner", "click capture");
          }}
        >
          <div className={"flex items-center gap-2 text-secondary"}>
            <ExportOutlined className={"text-base"} />
            {t<string>("Bulk modification")}
          </div>
        </MenuItem>
      )}
      <MenuItem
        onClick={() => {
          log("inner", "click");
          let deleteFiles = false;

          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("Delete {{count}} resources", {
              count: selectedResourceIds.length,
            }),
            children: (
              <div>
                <div>
                  <Checkbox
                    // color={'warning'}
                    // size={'sm'}
                    onValueChange={(s) => (deleteFiles = s)}
                  >
                    {t<string>("Delete files also")}
                  </Checkbox>
                </div>
                <div className={"opacity-80"}>
                  {t<string>(
                    "Resource will be shown again after next synchronization if you leave files undeleted.",
                  )}
                </div>
                <div className={"mt-6 font-bold"}>
                  {t<string>(
                    "Are you sure you want to delete {{count}} resources?",
                    { count: selectedResourceIds.length },
                  )}
                </div>
              </div>
            ),
            onOk: async () => {
              await BApi.resource.deleteResourcesByKeys({
                ids: selectedResourceIds,
                deleteFiles,
              });
            },
          });
        }}
        onClickCapture={() => {
          log("inner", "click capture");
        }}
      >
        <div className={"flex items-center gap-2 text-danger"}>
          <DeleteOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("Delete {{count}} resources", {
                count: selectedResourceIds.length,
              })
            : t<string>("Delete resource")}
        </div>
      </MenuItem>
    </>
  );
};

ContextMenuItems.displayName = "ContextMenuItems";

export default ContextMenuItems;
