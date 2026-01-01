import type { MediaLibrary } from "@/pages/deprecated/media-library/models";

import { useEffect, useState } from "react";

import { Button, Chip, toast, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { AiOutlineCopy } from "react-icons/ai";
import { useTranslation } from "react-i18next";

type Props = {
  onSelect: (path: string, mediaLibraryId: number) => any;
  onPathsLoaded: (paths: Set<string>) => any;
};

const MediaLibraryPathSelectorInner = (props: Props) => {
  const { t } = useTranslation();
  const { onSelect } = props;

  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);

  useEffect(() => {
    BApi.mediaLibraryV2.getAllMediaLibraryV2().then((r) => {
      setMediaLibraries(r.data ?? []);
      props.onPathsLoaded(new Set(r.data?.flatMap((ml) => ml.paths ?? []) ?? []));
    });
  }, []);

  return (
    <div className="grid gap-x-2 items-center" style={{ gridTemplateColumns: "auto 1fr" }}>
      {mediaLibraries.map((ml) => {
        return (
          <>
            <Chip variant="light">{ml.name}</Chip>
            <div>
              {ml.paths?.map((p) => {
                return (
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
                          toast.success(t<string>("Path copied to clipboard"));
                        }}
                      >
                        <AiOutlineCopy className="text-lg" />
                      </Button>
                    </div>
                  }>
                    <Button
                      key={p}
                      color="success"
                      size="sm"
                      variant="light"
                      onPress={() => onSelect(p, ml.id)}
                    >
                      {p}
                    </Button>
                  </Tooltip>
                );
              })}
            </div>
          </>
        );
      })}
    </div>
  );
};

export default MediaLibraryPathSelectorInner;
