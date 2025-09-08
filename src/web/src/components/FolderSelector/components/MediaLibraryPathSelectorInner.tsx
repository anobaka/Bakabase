import type { MediaLibrary } from "@/pages/media-library/models";

import { useEffect, useState } from "react";

import { Button, Chip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type Props = {
  onSelect: (path: string, mediaLibraryId: number) => any;
  onPathsLoaded: (paths: Set<string>) => any;
};

const MediaLibraryPathSelectorInner = (props: Props) => {
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
                  <Button
                    key={p}
                    color="success"
                    size="sm"
                    variant="light"
                    onPress={() => onSelect(p, ml.id)}
                  >
                    {p}
                  </Button>
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
