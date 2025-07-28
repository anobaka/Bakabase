"use client";

import { Button } from "@/components/bakaui";
import PresetTemplateBuilder from "@/pages/media-library-template/components/PresetTemplateBuilder";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
const PresetMediaLibraryTemplateBuilderTestPage = () => {
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

PresetMediaLibraryTemplateBuilderTestPage.displayName =
  "PresetMediaLibraryTemplateBuilderTest";

export default PresetMediaLibraryTemplateBuilderTestPage;
