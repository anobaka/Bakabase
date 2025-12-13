import type { components } from "@/sdk/BApi2";

import { create } from "zustand";

type SearchForm = components["schemas"]["Bakabase.Modules.Search.Models.Db.ResourceSearchDbModel"];

interface PendingSearchState {
  /** Pending search to be applied when navigating to resource page */
  pendingSearch?: SearchForm;
  /** Set a pending search */
  setPendingSearch: (search: SearchForm) => void;
  /** Consume and clear the pending search (returns undefined if none) */
  consumePendingSearch: () => SearchForm | undefined;
}

export const usePendingSearchStore = create<PendingSearchState>((set, get) => ({
  pendingSearch: undefined,
  setPendingSearch: (search) => {
    set({ pendingSearch: search });
  },
  consumePendingSearch: () => {
    const current = get().pendingSearch;
    if (current) {
      set({ pendingSearch: undefined });
    }
    return current;
  },
}));
