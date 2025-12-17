"use client";

import type { PreviewResource } from "@/pages/deprecated/media-library-template/components/Preview/index.tsx";

type Props = {
  resource: PreviewResource;
};
const Resource = ({ resource }: Props) => {
  return (
    <div className={"flex gap-1 items-center"}>
      <div>
        <img />
      </div>
      <div>
        <div>{resource.name}</div>
        <div />
      </div>
    </div>
  );
};

Resource.displayName = "Resource";

export default Resource;
