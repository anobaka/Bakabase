import type { DestroyableProps } from "../bakaui/types";

import { useTranslation } from "react-i18next";
import { useState } from "react";

import { Modal } from "../bakaui";

import FolderSelectorInner from "./components/FolderSelectorInner";
import { useFileSystemOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

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
      visible={visible}
      onClose={() => setVisible(false)}
      // onClose={() => {
      //   setVisible(false);
      // }}
      // onOpenChange={}
      title={t('Select Path')}
    >
      <FolderSelectorInner
        sources={sources}
        onSelect={async (path) => {
          propsOnSelect(path);
          setVisible(false);
        }}
      />
    </Modal>
  );
};

export default FolderSelector;
