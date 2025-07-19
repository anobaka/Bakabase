"use client";

import type { DialogProps } from "@alifd/next/types/dialog";

import { useState } from "react";
import { Button, Modal, Input, Table } from "@/components/bakaui";
import { useTranslation } from "react-i18next";

import { MdDelete } from "react-icons/md";
import { createPortalOfComponent } from "@/components/utils";
import BApi from "@/sdk/BApi";

interface IProps extends DialogProps {
  libraryId: number;
  onSubmitted: any;
}

const AddRootPathsInBulkDialog = ({
  libraryId,
  onSubmitted,
  ...dialogProps
}: IProps) => {
  const { t } = useTranslation();
  const [paths, setPaths] = useState<string[]>([]);
  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  return (
    <Modal
      v2
      style={{ minWidth: 600 }}
      title={t<string>("Add root paths in bulk")}
      visible={visible}
      width={"auto"}
      onCancel={close}
      onClose={close}
      onOk={async () => {
        const rsp = await BApi.mediaLibrary.addMediaLibraryRootPathsInBulk(
          libraryId,
          { rootPaths: paths },
        );

        if (!rsp.code) {
          onSubmitted?.();
          close();
        }
      }}
      {...dialogProps}
    >
      <Table dataSource={paths} hasBorder={false}>
        <Table.Column
          cell={(name, i, r) => {
            return (
              <Input
                hasClear
                trim
                endContent={
                  <Button
                    isIconOnly
                    color={"danger"}
                    size={"sm"}
                    variant={"light"}
                    style={{ marginLeft: 5 }}
                    onPress={() => {
                      paths.splice(i, 1);
                      setPaths([...paths]);
                    }}
                  >
                    <MdDelete className={"text-base"} />
                  </Button>
                }
                placeholder={t<string>("Root path")}
                style={{ width: 800 }}
                onChange={(v) => {
                  paths[i] = v;
                }}
              />
            );
          }}
          dataIndex={"name"}
          title={t<string>("Root paths")}
        />
      </Table>
      <Button
        style={{ marginTop: 5 }}
        onClick={() => {
          setPaths([...paths, '']);
        }}
        text
        // size={'small'}
        type={'primary'}
      >
        {t<string>("Add a root path")}
      </Button>
    </Modal>
  );
};

AddRootPathsInBulkDialog.show = (props: IProps) =>
  createPortalOfComponent(AddRootPathsInBulkDialog, props);

export default AddRootPathsInBulkDialog;
