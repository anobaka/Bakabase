import type { IProperty } from "@/components/Property/models";

import { QueryClient, useQuery } from "@tanstack/react-query";

import { canReadSourceMetadata } from "./metadata";
import { useResourceApi, useResourceOwner, useResourceOwnerKey } from "./resourceContext";

// A virtualized list shares one metadata request across every card for an owner.
const queryClient = new QueryClient();

export function useResourceCoverProperties(ids: readonly number[]) {
  const api = useResourceApi();
  const owner = useResourceOwner();
  const ownerKey = useResourceOwnerKey();

  return useQuery(
    {
      queryKey: [
        "resource-cover-properties",
        ownerKey,
        owner?.metadata?.data.activeGeneration,
        owner?.metadata?.data.checkpoint?.catalogRevision,
        [...ids].sort((a, b) => a - b).join(","),
      ],
      enabled: ids.length > 0 && (!owner || canReadSourceMetadata(owner)),
      queryFn: async () => {
        const response = await api.customProperty.getCustomPropertyByKeys(
          { ids: [...ids] },
          { showErrorToast: false },
        );

        if (response.code) throw new Error(response.message || "Unable to load cover properties");

        return new Map(
          (response.data ?? []).map((property) => [property.id, property as IProperty]),
        );
      },
      staleTime: 60_000,
      retry: false,
    },
    queryClient,
  );
}
