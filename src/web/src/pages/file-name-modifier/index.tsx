import React from "react";

import FileNameModifier from "@/components/FileNameModifier";

const FileNameModifierPage: React.FC = () => {
  return (
    <div className="p-4 h-full">
      <div className="h-full">
        <FileNameModifier initialFilePaths={[]} />
      </div>
    </div>
  );
};

export default FileNameModifierPage;
