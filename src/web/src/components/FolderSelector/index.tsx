import type { DestroyableProps } from "../bakaui/types";

import { useTranslation } from "react-i18next";
import { useState } from "react";

import { Accordion, AccordionItem, Modal } from "../bakaui";

import CustomPathSelectorInner from "./components/CustomPathSelectorInner";
import MediaLibraryPathSelectorInner from "./components/MediaLibraryPathSelectorInner";
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

  const [mlPaths, setMlPaths] = useState<Set<string>>(new Set());

  const onSelect = async (path: string) => {
    await BApi.options.addLatestMovingDestination(path);
    propsOnSelect(path);
    setVisible(false);
  };

  const onPathsLoaded = (paths: Set<string>) => {
    setMlPaths(paths);
  };

  const renderSourceInner = (source: Source) => {
    switch (source) {
      case "media library":
        return <MediaLibraryPathSelectorInner onSelect={onSelect} onPathsLoaded={onPathsLoaded} />;
      case "custom":
        return <CustomPathSelectorInner onSelect={onSelect} mlPaths={mlPaths} />;
    }
  };

  const renderSources = () => {
    return (
      <Accordion hideIndicator selectedKeys={sources.map((s) => s)} variant="splitted">
        {sources.map((s) => {
          return (
            <AccordionItem key={s} aria-label={t(s)} title={t(s)}>
              {renderSourceInner(s)}
            </AccordionItem>
          );
        })}
      </Accordion>
    );
  };

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
      {renderSources()}
    </Modal>
  );
};

export default FolderSelector;
