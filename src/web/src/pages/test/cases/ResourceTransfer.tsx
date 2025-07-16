"use client";

import type { Resource as ResourceModel } from "@/core/models/Resource";

import { useEffect, useState } from "react";

import ResourceTransferModal from "@/components/ResourceTransferModal";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button } from "@/components/bakaui";

export default () => {
  const { createPortal } = useBakabaseContext();
  const [resources, setResources] = useState<ResourceModel[]>([]);

  useEffect(() => {
    BApi.resource
      .searchResources({
        pageSize: 20,
        page: 1,
      })
      .then((r) => {
        setResources(r.data || []);
      });
  }, []);

  return (
    <Button
      onClick={() => {
        createPortal(ResourceTransferModal, {
          fromResources: resources,
        });
      }}
    >
      Open
    </Button>
  );
};
