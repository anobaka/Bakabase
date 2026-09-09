import type { BakabaseModulesCollectionAbstractionsModelsDomainResourceCollection } from "@/sdk/Api";

import { create } from "zustand";

export type CollectionModel = BakabaseModulesCollectionAbstractionsModelsDomainResourceCollection;

interface CollectionsState {
  collections: Map<number, CollectionModel>;
  /** True once a page has loaded the list, so an empty map can be told from an unloaded one. */
  loaded: boolean;
  setCollections: (collections: CollectionModel[]) => void;
  updateCollection: (collection: CollectionModel) => void;
  removeCollection: (id: number) => void;
}

/**
 * Collections, kept here rather than in each page because more than one thing shows them at
 * once — the list, the detail page, the "add to collection" menu — and the server pushes a
 * collection whenever its numbers move.
 */
export const useCollectionsStore = create<CollectionsState>((set) => ({
  collections: new Map(),
  loaded: false,
  setCollections: (collections) =>
    set(() => {
      const next = new Map<number, CollectionModel>();

      for (const c of collections) {
        if (c.id != null) next.set(c.id, c);
      }

      return { collections: next, loaded: true };
    }),
  updateCollection: (collection) =>
    set((state) => {
      if (collection.id == null) return state;
      const next = new Map(state.collections);

      next.set(collection.id, collection);

      return { collections: next };
    }),
  removeCollection: (id) =>
    set((state) => {
      const next = new Map(state.collections);

      next.delete(id);

      return { collections: next };
    }),
}));

/** The list, in the order the server hands it back (by `order`, then id). */
export const selectCollectionList = (state: CollectionsState) =>
  [...state.collections.values()].sort(
    (a, b) => (a.order ?? 0) - (b.order ?? 0) || (a.id ?? 0) - (b.id ?? 0),
  );

export const selectCollectionById = (id: number) => (state: CollectionsState) =>
  state.collections.get(id);
