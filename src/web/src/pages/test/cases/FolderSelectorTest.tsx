import { useState } from "react";

import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import FolderSelector from "@/components/FolderSelector";

const FolderSelectorTest = () => {
  const { createPortal } = useBakabaseContext();
  const [selectedPath, setSelectedPath] = useState<string | undefined>(undefined);

  return (
    <div>
      <div>Selected Path: {selectedPath}</div>
      <Button
        onPress={() => {
          createPortal(FolderSelector, {
            sources: ["custom", "media library"],
            onSelect: (path: string) => {
              console.log(path);
              setSelectedPath(path);
            },
          });
        }}
      >
        Open
      </Button>
    </div>
  );
};

export default FolderSelectorTest;
