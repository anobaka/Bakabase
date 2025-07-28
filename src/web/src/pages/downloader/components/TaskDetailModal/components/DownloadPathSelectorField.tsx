"use client";

import DownloadPathSelector from "./DownloadPathSelector";

type Props = {
  downloadPath?: string;
  onChange: (downloadPath?: string) => void;
};
const DownloadPathSelectorField = ({ downloadPath, onChange }: Props) => {
  return (
    <DownloadPathSelector downloadPath={downloadPath} onChange={onChange} />
  );
};

DownloadPathSelectorField.displayName = "DownloadPathSelectorField";

export default DownloadPathSelectorField;
