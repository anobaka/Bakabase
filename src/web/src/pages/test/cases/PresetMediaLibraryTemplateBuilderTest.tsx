"use client";

import { Button } from "@/components/bakaui";
import PresetTemplateBuilder from "@/pages/deprecated/media-library-template/components/PresetTemplateBuilder";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
const PresetMediaLibraryTemplateBuilderTest = () => {
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

PresetMediaLibraryTemplateBuilderTest.displayName = "PresetMediaLibraryTemplateBuilderTest";

export default PresetMediaLibraryTemplateBuilderTest;
