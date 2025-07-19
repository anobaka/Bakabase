"use client";

import type { DialogProps } from "@alifd/next/types/dialog";

import { useState } from "react";
import { Button, Modal, Input, Table } from "@/components/bakaui";
import { useTranslation } from "react-i18next";

import { MdDelete } from "react-icons/md";
import { createPortalOfComponent } from "@/components/utils";
import BApi from "@/sdk/BApi";

interface IProps extends DialogProps {
  categoryId: number;
  onSubmitted: any;
}

const AddMediaLibraryInBulkDialog = ({
  categoryId,
  onSubmitted,
  ...dialogProps
}: IProps) => {
  const { t } = useTranslation();
  const [nameAndPaths, setNameAndPaths] = useState<
    { name: string; paths: string[] }[]
  >([]);
  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  return (
    <Modal
      v2
      style={{ minWidth: 600 }}
      title={t<string>("Add media libraries in bulk")}
      visible={visible}
      width={"auto"}
      onCancel={close}
      onClose={close}
      onOk={async () => {
        const model = {
          nameAndPaths: nameAndPaths.reduce<Record<string, string[]>>(
            (s, t) => {
              s[t.name] = t.paths;

              return s;
            },
            {},
          ),
        };
        const rsp = await BApi.mediaLibrary.addMediaLibrariesInBulk(
          categoryId,
          model,
        );

        if (!rsp.code) {
          onSubmitted?.();
          close();
        }
      }}
      {...dialogProps}
    >
      <Table dataSource={nameAndPaths} hasBorder={false}>
        <Table.Column
          cell={(name, i, r) => {
            return (
              <Input
                hasClear
                trim
                placeholder={t<string>("Name")}
                onChange={(v) => {
                  nameAndPaths[i].name = v.toString();
                }}
              />
            );
          }}
          dataIndex={"name"}
          title={t<string>("Media libraries")}
        />
        <Table.Column
          cell={(paths, i, r) => {
            const elements = (paths || []).map((p, j) => {
              return (
                <Input
                  key={j}
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
                        paths.splice(j, 1);
                        setNameAndPaths([...nameAndPaths]);
                      }}
                    >
                      <MdDelete className={"text-base"} />
                    </Button>
                  }
                  placeholder={t<string>("Root path")}
                  style={{ width: 800 }}
                  onChange={(vp) => {
                    paths[j] = vp;
                  }}
                />
              );
            });

            elements.push(
              <Button
                type={'primary'}
                onClick={() => {
                  if (!paths) {
                    paths = [];
                  }
                  paths.push('');
                  setNameAndPaths([...nameAndPaths]);
                }}
                text
                // size={'small'}
                key={-1}
              >
                {t<string>("Add root path")}
              </Button>,
            );

            return (
              <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                {elements}
              </div>
            );
          }}
          dataIndex={"paths"}
          title={t<string>("Root paths")}
        />
      </Table>
      <Button
        style={{ marginTop: 5 }}
        onClick={() => {
          setNameAndPaths([...nameAndPaths, { name: '', paths: [] }]);
        }}
        text
        // size={'small'}
        type={'primary'}
      >
        {t<string>("Add a media library")}
      </Button>
    </Modal>
  );
};

AddMediaLibraryInBulkDialog.show = (props: IProps) =>
  createPortalOfComponent(AddMediaLibraryInBulkDialog, props);

export default AddMediaLibraryInBulkDialog;
