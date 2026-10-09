import { useTranslation } from "react-i18next";
import { useRef, useState } from "react";

import { Accordion, AccordionItem } from "../../bakaui";

import CustomPathSelectorInner from "./CustomPathSelectorInner";
import MediaLibraryPathSelectorInner from "./MediaLibraryPathSelectorInner";

import BApi from "@/sdk/BApi";
import { storageError, validateUserStoragePaths } from "@/stores/userStorage";

type Source = "custom" | "media library";

type Props = {
  sources: Source[];
  onSelect: (path: string) => any;
};

const FolderSelectorInner = ({ sources, onSelect: propsOnSelect }: Props) => {
  const { t } = useTranslation();
  const [mlPaths, setMlPaths] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string>();
  const selecting = useRef(false);

  const onSelect = async (path: string) => {
    if (selecting.current) return;
    if (!path.trim()) {
      setError(t("fileExplorer.storage.choosePath"));

      return;
    }
    selecting.current = true;
    setError(undefined);
    try {
      await validateUserStoragePaths([path]);
      await BApi.options.addLatestMovingDestination(path);
      propsOnSelect(path);
    } catch (cause) {
      setError(storageError(cause, t).message || t("fileExplorer.storage.pathRejected"));
    } finally {
      selecting.current = false;
    }
  };

  const onPathsLoaded = (paths: Set<string>) => {
    setMlPaths(paths);
  };

  const renderSourceInner = (source: Source) => {
    switch (source) {
      case "media library":
        return <MediaLibraryPathSelectorInner onPathsLoaded={onPathsLoaded} onSelect={onSelect} />;
      case "custom":
        return <CustomPathSelectorInner mlPaths={mlPaths} onSelect={onSelect} />;
    }
  };

  return (
    <>
      {error && (
        <p className="px-2 text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      <Accordion hideIndicator selectedKeys={sources.map((s) => s)} variant="splitted">
        {sources.map((s) => {
          return (
            <AccordionItem key={s} aria-label={t(s)} title={t(s)}>
              {renderSourceInner(s)}
            </AccordionItem>
          );
        })}
      </Accordion>
    </>
  );
};

export default FolderSelectorInner;
