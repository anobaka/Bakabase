import { create } from "zustand";

interface SearchFormState {
  orders: any[];
  mediaLibraryIdGroups: Record<string, any>;
  addDts: any[];
  releaseDts: any[];
  fileCreateDts: any[];
  fileModifyDts: any[];
  patch: (payload: Partial<Omit<SearchFormState, "patch" | "replace">>) => void;
  replace: (payload: Omit<SearchFormState, "patch" | "replace">) => void;
}

export const useSearchFormStore = create<SearchFormState>((set, get) => ({
  orders: [],
  mediaLibraryIdGroups: {},
  addDts: [],
  releaseDts: [],
  fileCreateDts: [],
  fileModifyDts: [],
  patch: (payload) => {
    const prevState = get();
    const model = {
      ...prevState,
      ...payload,
    };

    console.log("patching search form", { ...prevState }, "with", {
      ...payload,
    });
    console.log("search form after patching", model);
    set(model);
  },
  replace: (payload) => {
    console.log("replacing search form with", payload);
    set(payload);
  },
}));
