"use client";

import { Button } from "@/components/bakaui";
import PresetTemplateBuilder from "@/pages/media-library-template/components/PresetTemplateBuilder";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

export default () => {
  const { createPortal } = useBakabaseContext();

  return (
    <Button
      onPress={() => {
        createPortal(PresetTemplateBuilder, {});
      }}
    >
      PresetMediaLibraryTemplateBuilder
    </Button>
  );
};
