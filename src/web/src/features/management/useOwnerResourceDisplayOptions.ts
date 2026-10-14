import type { BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions } from "@/sdk/Api";
import type { PropertyValueScope } from "@/sdk/constants";

import { QueryClient, useQuery } from "@tanstack/react-query";

import { ownerClient, sourceKey, type ManagementSource } from "./api";
import { useResourceOwner } from "./resourceContext";

export interface OwnerResourceDisplayOptions {
  uiResource?: Partial<BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions>;
  propertyValueScopePriority?: PropertyValueScope[];
}

// Cards share one request per owner/epoch; their custom-property IDs never enter
// the viewing server's global UI settings. No query provider is needed by cards.
const displayOptionsQueryClient = new QueryClient();

export async function patchOwnerResourceScopePriority(
  owner: ManagementSource,
  propertyValueScopePriority: PropertyValueScope[],
) {
  await ownerClient(owner)("resourceDisplayOptionsPatch", { body: { propertyValueScopePriority } });
  await displayOptionsQueryClient.invalidateQueries({
    queryKey: ["owner-resource-display-options", sourceKey(owner)],
  });
}

export function useOwnerResourceDisplayOptions() {
  const owner = useResourceOwner();

  return useQuery(
    {
      queryKey: ["owner-resource-display-options", owner ? sourceKey(owner) : "local"],
      enabled: !!owner && !owner.isLocal && owner.available,
      queryFn: async (): Promise<OwnerResourceDisplayOptions> => {
        const response = await ownerClient(owner!)<OwnerResourceDisplayOptions>(
          "resourceDisplayOptions",
        );

        if (response.code || !response.data)
          throw new Error(response.message || "Unable to load device resource display options");

        return response.data;
      },
      staleTime: 60_000,
      retry: false,
    },
    displayOptionsQueryClient,
  );
}
