import React from "react";

import FileNameModifier from "@/components/FileNameModifier";

const FileNameModifierPage: React.FC = () => {
  return (
    <div className="h-full">
      <FileNameModifier initialFilePaths={[]} />
    </div>
  );
};

export default FileNameModifierPage;
