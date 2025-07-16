"use client";

import type { PreviewResource } from "@/pages/media-library-template/components/Preview/index";

type Props = {
  resource: PreviewResource;
};

export default ({ resource }: Props) => {
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
