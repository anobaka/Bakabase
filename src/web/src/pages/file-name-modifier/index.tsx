import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineEdit } from "react-icons/ai";

import FileNameModifier from "@/components/FileNameModifier";

const FileNameModifierPage: React.FC = () => {
  const { t } = useTranslation();

  return (
    <div className="flex h-full min-h-0 min-w-0 flex-col gap-4">
      <header className="shrink-0">
        <h1 className="flex items-center gap-2 text-lg font-semibold">
          <AiOutlineEdit aria-hidden className="text-primary" />
          {t<string>("FileNameModifier.Title")}
        </h1>
        <p className="mt-1 text-sm leading-relaxed text-default-500">
          {t<string>("fileNameModifier.page.description")}
        </p>
      </header>
      <div className="min-h-0 flex-1">
        <FileNameModifier initialFilePaths={[]} />
      </div>
    </div>
  );
};

export default FileNameModifierPage;
