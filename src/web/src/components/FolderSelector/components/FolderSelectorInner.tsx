import { useTranslation } from "react-i18next";
import { useState } from "react";

import { Accordion, AccordionItem } from "../../bakaui";

import CustomPathSelectorInner from "./CustomPathSelectorInner";
import MediaLibraryPathSelectorInner from "./MediaLibraryPathSelectorInner";
import BApi from "@/sdk/BApi";

type Source = "custom" | "media library";

type Props = {
  sources: Source[];
  onSelect: (path: string) => any;
};

const FolderSelectorInner = ({ sources, onSelect: propsOnSelect }: Props) => {
  const { t } = useTranslation();
  const [mlPaths, setMlPaths] = useState<Set<string>>(new Set());

  const onSelect = async (path: string) => {
    await BApi.options.addLatestMovingDestination(path);
    propsOnSelect(path);
  };

  const onPathsLoaded = (paths: Set<string>) => {
    setMlPaths(paths);
  };

  const renderSourceInner = (source: Source) => {
    switch (source) {
      case "media library":
        return (
          <MediaLibraryPathSelectorInner
            onSelect={onSelect}
            onPathsLoaded={onPathsLoaded}
          />
        );
      case "custom":
        return <CustomPathSelectorInner onSelect={onSelect} mlPaths={mlPaths} />;
    }
  };

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

export default FolderSelectorInner;


