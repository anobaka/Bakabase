"use client";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import {
  DeleteOutlined,
  FileUnknownOutlined,
  SyncOutlined,
} from "@ant-design/icons";

import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Divider,
  Modal,
  Spinner,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceTransferModal from "@/components/ResourceTransferModal";
import { Resource } from "@/core/models/Resource";
import { DestroyableProps } from "../bakaui/types";

type Props = {
  mediaLibraryId?: number;
  onHandled?: () => any;
} & DestroyableProps;

const HandleUnknownResourcesModal = ({ onHandled, mediaLibraryId }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [unknownResources, setUnknownResources] = useState<Resource[]>([]);
  const [loading, setLoading] = useState(true);

  const init = async () => {
    const r = await BApi.resource.getUnknownResources({ mediaLibraryId });
    setUnknownResources(r.data || []);
    setLoading(false);
  };

  useEffect(() => {
    init();
  }, []);

  return (
    <Modal
      defaultVisible={true}
      title={t<string>("Choose a method to handle unknown resources")}
      size="lg"
      footer={false}
      // style={{maxWidth: '90vw'}}
    >
      {loading ? (
        <div className="flex justify-center items-center h-full">
          <Spinner />
        </div>
      ) : (
      <div className={"flex flex-col gap-4 mb-4"}>
        <div>
          <div>
            {t<string>(
              "When the system fails to find the file or folder corresponding to the resource path, the resource is marked as an unknown resource.",
            )}
          </div>
          <div>
            {t<string>(
              "In most cases, this is caused by changes in the names of files or folders.",
            )}
          </div>
        </div>
        <div className={"flex items-start gap-2 justify-around"}>
          <Card
            isHoverable
            isPressable
            className="w-[300px]"
            onPress={() => {
              createPortal(ResourceTransferModal, {
                fromResources: unknownResources,
                onDestroyed: () => {
                  onHandled?.();
                },
              });
            }}
          >
            <CardHeader className="flex gap-3 text-lg">
              <SyncOutlined className={"text-success"} />
              {t<string>("Transfer data")}
            </CardHeader>
            <Divider />
            <CardBody>
              <div>
                {t<string>(
                  "You can transfer the data of unknown resources to existing resources.",
                )}
              </div>
              <div>
                {t<string>(
                  "You can also transfer the data of some of unknown resources to existing resources first, and then delete the remaining unknown resources.",
                )}
              </div>
            </CardBody>
          </Card>
          <Card
            isHoverable
            isPressable
            className="w-[300px]"
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>(
                  "Delete {{count}} unknown resources permanently",
                  { count: unknownResources.length },
                ),
                children: (
                  <div>
                    {t<string>(
                      "Be careful, this operation can not be undone",
                    )}
                  </div>
                ),
                onOk: async () => {
                  await BApi.resource.deleteUnknownResources({ mediaLibraryId });
                  init();
                  onHandled?.();
                },
              });
            }}
          >
            <CardHeader className="flex gap-3 text-lg">
              <DeleteOutlined className={"text-danger"} />
              {t<string>(
                "Delete {{count}} unknown resources permanently",
                { count: unknownResources.length },
              )}
            </CardHeader>
            <Divider />
            <CardBody>
              <div>
                {t<string>(
                  "You can delete all unknown resources permanently.",
                )}
              </div>
            </CardBody>
            </Card>
          </div>
        </div>
      )}
    </Modal>
  )
};

HandleUnknownResourcesModal.displayName = "HandleUnknownResourcesModal";

export default HandleUnknownResourcesModal;
