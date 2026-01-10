import { AiOutlineCheck, AiOutlineCloseCircle, AiOutlineCopy, AiOutlineDelete } from "react-icons/ai";
import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, toast, Tooltip } from "@/components/bakaui";
import PathAutocomplete from "@/components/PathAutocomplete";
import { useFileSystemOptionsStore } from "@/stores/options";

type Props = {
  onSelect: (path: string) => any;
  mlPaths: Set<string>;
};

const CustomPathSelectorInner = (props: Props) => {
  const { onSelect, mlPaths } = props;
  const { t } = useTranslation();
  const fsOptions = useFileSystemOptionsStore((state) => state);
  const paths = fsOptions.data?.recentMovingDestinations ?? [];

  const [manualPath, setManualPath] = useState("");

  return (
    <div className="flex flex-col gap-1">
      <div className="flex flex-wrap items-center gap-1">
        {paths
          // .filter((p) => !warningPaths?.includes(p))
          .map((p) => {
            return (
              <div key={p}>
                <Tooltip content={
                  <div className="flex items-center gap-0">
                    {/* Copy path */}
                    <Button
                      isIconOnly
                      color="default"
                      size="sm"
                      variant="light"
                      onPress={() => {
                        navigator.clipboard.writeText(p);
                        toast.success(t<string>("common.tip.pathCopied"));
                      }}
                    >
                      <AiOutlineCopy className="text-lg" />
                    </Button>
                    <Button
                      isIconOnly
                      color="danger"
                      size="sm"
                      variant="light"
                      onPress={async () => {
                        await fsOptions.patch({
                          recentMovingDestinations: paths.filter((p) => p !== p),
                        });
                      }}
                    >
                      <AiOutlineDelete className="text-lg" />
                    </Button>
                  </div>
                }>
                  <Button
                    key={p}
                    color={mlPaths.has(p) ? "success" : "primary"}
                    size="sm"
                    variant="light"
                    onClick={() => onSelect(p)}
                  >
                    {p}
                  </Button>
                </Tooltip>
              </div>
            );
          })}
      </div>
      <div className="flex items-center gap-2">
        <PathAutocomplete
          pathType="folder"
          placeholder={t("folderSelector.placeholder.manuallyEnterPath")}
          size="sm"
          value={manualPath}
          onChange={setManualPath}
        />
        <Button
          isIconOnly
          color="success"
          size="sm"
          variant="flat"
          onPress={() => onSelect(manualPath)}
        >
          <AiOutlineCheck className="text-lg" />
        </Button>
      </div>
    </div>
  );
};

export default CustomPathSelectorInner;
