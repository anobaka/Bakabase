"use client";

import type { DialogProps } from "@alifd/next/types/dialog";

import { useState } from "react";
import { Button, Dialog, Input, Table } from "@alifd/next";
import { useTranslation } from "react-i18next";

import ClickableIcon from "@/components/ClickableIcon";
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
    <Dialog
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
                addonAfter={
                  <ClickableIcon
                    colorType={"danger"}
                    style={{ marginLeft: 5 }}
                    type={"delete"}
                    onClick={() => {
                      paths.splice(i, 1);
                      setPaths([...paths]);
                    }}
                  />
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
    </Dialog>
  );
};

AddRootPathsInBulkDialog.show = (props: IProps) =>
  createPortalOfComponent(AddRootPathsInBulkDialog, props);

export default AddRootPathsInBulkDialog;
