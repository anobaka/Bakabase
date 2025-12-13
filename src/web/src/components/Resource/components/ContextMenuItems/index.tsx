"use client";

import { MenuItem } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";
import {
  ApiOutlined,
  DeleteOutlined,
  EditOutlined,
  SendOutlined,
} from "@ant-design/icons";

import MediaLibraryMultiSelector from "@/components/MediaLibraryMultiSelector";
import { ResourceAdditionalItem } from "@/sdk/constants";
import ResourceTransferModal from "@/components/ResourceTransferModal";
import { Modal } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import BulkPropertyEditor from "@/components/Resource/components/BulkPropertyEditor";

const log = buildLogger("ResourceContextMenuItems");

type Props = {
  selectedResourceIds: number[];
  selectedResources?: any[];
  onSelectedResourcesChanged?: (ids: number[]) => any;
};
const ContextMenuItems = ({
  selectedResourceIds,
  selectedResources,
  onSelectedResourcesChanged,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  return (
    <>
      <MenuItem
        onClick={() => {
          log("inner", "click");
          createPortal(MediaLibraryMultiSelector, {
            resourceIds: selectedResourceIds,
            onSubmit: () => {
              onSelectedResourcesChanged?.(selectedResourceIds);
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
            ? t<string>("Set media libraries for {{count}} resources", {
                count: selectedResourceIds.length,
              })
            : t<string>("Set media libraries")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          log("inner", "click");
          // Use provided resources if available, otherwise fetch
          if (selectedResources && selectedResources.length > 0) {
            createPortal(ResourceTransferModal, {
              fromResources: selectedResources,
            });
          } else {
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
          }
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
      <MenuItem
        onClick={() => {
          log("inner", "click");
          createPortal(BulkPropertyEditor, {
            resourceIds: selectedResourceIds,
            initialResources: selectedResources,
            onSubmitted: () => {
              onSelectedResourcesChanged?.(selectedResourceIds);
            },
          });
        }}
        onClickCapture={() => {
          log("inner", "click capture");
        }}
      >
        <div className={"flex items-center gap-2 text-secondary"}>
          <EditOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("Bulk edit properties")
            : t<string>("Edit properties")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          log("inner", "click");
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("Delete {{count}} resources", {
              count: selectedResourceIds.length,
            }),
            children: (
              <div>
                <div className={"font-bold"}>
                  {t<string>(
                    "Are you sure you want to delete {{count}} resources?",
                    { count: selectedResourceIds.length },
                  )}
                </div>
                <div className={"opacity-60 mt-2"}>
                  {t<string>("Files will not be deleted.")}
                </div>
              </div>
            ),
            onOk: async () => {
              await BApi.resource.deleteResourcesByKeys({
                ids: selectedResourceIds,
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
