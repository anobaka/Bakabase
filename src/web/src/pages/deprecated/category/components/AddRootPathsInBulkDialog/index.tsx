"use client";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { MdDelete } from "react-icons/md";

import { Button, Modal, Input } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface IProps {
  libraryId: number;
  onSubmitted?: () => void;
  onDestroyed?: () => void;
}

const AddRootPathsInBulkDialog = ({
  libraryId,
  onSubmitted,
  onDestroyed,
}: IProps) => {
  const { t } = useTranslation();
  const [paths, setPaths] = useState<string[]>([]);
  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  return (
    <Modal
      defaultVisible
      size="xl"
      title={t<string>("Add root paths in bulk")}
      visible={visible}
      onClose={close}
      onDestroyed={onDestroyed}
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
    >
      <div className="space-y-4">
        {paths.map((path, index) => (
          <div key={index} className="flex items-center gap-2">
            <Input
              className="flex-1"
              placeholder={t<string>("Root path")}
              value={path}
              onValueChange={(value) => {
                const newPaths = [...paths];

                newPaths[index] = value;
                setPaths(newPaths);
              }}
            />
            <Button
              isIconOnly
              color="danger"
              size="sm"
              variant="light"
              onPress={() => {
                const newPaths = paths.filter((_, i) => i !== index);

                setPaths(newPaths);
              }}
            >
              <MdDelete className="text-base" />
            </Button>
          </div>
        ))}

        <Button
          color="primary"
          size="sm"
          onPress={() => {
            setPaths([...paths, ""]);
          }}
        >
          {t<string>("Add a root path")}
        </Button>
      </div>
    </Modal>
  );
};

export default AddRootPathsInBulkDialog;
