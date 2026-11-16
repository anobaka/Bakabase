import type { DestroyableProps } from "../bakaui/types";

import { useTranslation } from "react-i18next";
import { useState } from "react";

import { Modal } from "../bakaui";

import FolderSelectorInner from "./components/FolderSelectorInner";

import { storageError } from "@/stores/userStorage";

type Source = "custom" | "media library";

type Props = {
  sources: Source[];
  onSelect: (path: string) => any;
} & DestroyableProps;

const FolderSelector = (props: Props) => {
  const { t } = useTranslation();
  const { sources, onSelect: propsOnSelect, onDestroyed } = props;
  const [visible, setVisible] = useState(true);

  return (
    <Modal
      footer={false}
      size="lg"
      title={t("folderSelector.title")}
      onClose={() => setVisible(false)}
      // onClose={() => {
      //   setVisible(false);
      // }}
      // onOpenChange={}
      visible={visible}
    >
      <FolderSelectorInner
        sources={sources}
        onSelect={async (path) => {
          const result = await propsOnSelect(path);

          if (result?.code) throw storageError(result);
          setVisible(false);
        }}
      />
    </Modal>
  );
};

export default FolderSelector;
