"use client";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { MdDelete } from "react-icons/md";

import { Button, Modal, Input } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface IProps {
  categoryId: number;
  onSubmitted?: () => void;
  onDestroyed?: () => void;
}

const AddMediaLibraryInBulkDialog = ({
  categoryId,
  onSubmitted,
  onDestroyed,
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
      defaultVisible
      size="xl"
      title={t<string>("Add media libraries in bulk")}
      visible={visible}
      onClose={close}
      onDestroyed={onDestroyed}
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
    >
      <div className="space-y-4">
        {nameAndPaths.map((item, index) => (
          <div key={index} className="border rounded-lg p-4 space-y-3">
            <div className="flex items-center gap-2">
              <Input
                className="flex-1"
                placeholder={t<string>("Name")}
                value={item.name}
                onValueChange={(value) => {
                  const newNameAndPaths = [...nameAndPaths];

                  newNameAndPaths[index].name = value;
                  setNameAndPaths(newNameAndPaths);
                }}
              />
              <Button
                isIconOnly
                color="danger"
                size="sm"
                variant="light"
                onPress={() => {
                  const newNameAndPaths = nameAndPaths.filter(
                    (_, i) => i !== index,
                  );

                  setNameAndPaths(newNameAndPaths);
                }}
              >
                <MdDelete className="text-base" />
              </Button>
            </div>

            <div className="space-y-2">
              <div className="text-sm font-medium">
                {t<string>("Root paths")}
              </div>
              {item.paths.map((path, pathIndex) => (
                <div key={pathIndex} className="flex items-center gap-2">
                  <Input
                    className="flex-1"
                    placeholder={t<string>("Root path")}
                    value={path}
                    onValueChange={(value) => {
                      const newNameAndPaths = [...nameAndPaths];

                      newNameAndPaths[index].paths[pathIndex] = value;
                      setNameAndPaths(newNameAndPaths);
                    }}
                  />
                  <Button
                    isIconOnly
                    color="danger"
                    size="sm"
                    variant="light"
                    onPress={() => {
                      const newNameAndPaths = [...nameAndPaths];

                      newNameAndPaths[index].paths.splice(pathIndex, 1);
                      setNameAndPaths(newNameAndPaths);
                    }}
                  >
                    <MdDelete className="text-base" />
                  </Button>
                </div>
              ))}
              <Button
                color="primary"
                size="sm"
                variant="light"
                onPress={() => {
                  const newNameAndPaths = [...nameAndPaths];

                  newNameAndPaths[index].paths.push("");
                  setNameAndPaths(newNameAndPaths);
                }}
              >
                {t<string>("Add root path")}
              </Button>
            </div>
          </div>
        ))}

        <Button
          color="primary"
          size="sm"
          onPress={() => {
            setNameAndPaths([...nameAndPaths, { name: "", paths: [] }]);
          }}
        >
          {t<string>("Add a media library")}
        </Button>
      </div>
    </Modal>
  );
};

export default AddMediaLibraryInBulkDialog;
