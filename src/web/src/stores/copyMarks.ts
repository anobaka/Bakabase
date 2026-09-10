import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { create } from "zustand";
import { persist } from "zustand/middleware";
import { v4 as uuidv4 } from "uuid";

export interface CandidateGroup {
  id: string;
  sourcePath: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  createdAt: number;
}

interface CopyMarksState {
  // Copy mode state
  copyModeEntryPath: string | null;
  selectedMarkIds: number[];

  // Candidate groups
  candidateGroups: CandidateGroup[];
  selectedGroupId: string | null;

  // Sidebar state
  sidebarCollapsed: boolean;

  // Actions
  enterCopyMode: (path: string) => void;
  exitCopyMode: () => void;
  toggleMarkSelection: (markId: number) => void;
  selectAllMarks: (markIds: number[]) => void;
  deselectAllMarks: () => void;
  confirmSelection: (sourcePath: string, marks: BakabaseAbstractionsModelsDomainPathMark[]) => void;
  removeGroup: (groupId: string) => void;
  removeMarkFromGroup: (groupId: string, markId: number) => void;
  selectGroup: (groupId: string | null) => void;
  toggleSidebar: () => void;
  setSidebarCollapsed: (collapsed: boolean) => void;
  clearAllGroups: () => void;
}

/**
 * Copied marks outlive a reload.
 *
 * Copying marks off one folder and pasting them onto twenty is a job people do across a session:
 * they copy, scroll, expand, open a modal, sometimes reload — and losing the clipboard to any of
 * that means starting over. Only the groups and where the sidebar is are kept; copy mode itself is
 * a half-finished action and would be confusing to come back to.
 */
export const useCopyMarksStore = create<CopyMarksState>()(
  persist(
    (set, get) => ({
      copyModeEntryPath: null,
      selectedMarkIds: [],
      candidateGroups: [],
      selectedGroupId: null,
      sidebarCollapsed: true,

      enterCopyMode: (path) =>
        set({
          copyModeEntryPath: path,
          selectedMarkIds: [],
        }),

      exitCopyMode: () =>
        set({
          copyModeEntryPath: null,
          selectedMarkIds: [],
        }),

      toggleMarkSelection: (markId) =>
        set((state) => {
          const idx = state.selectedMarkIds.indexOf(markId);

          if (idx > -1) {
            return { selectedMarkIds: state.selectedMarkIds.filter((id) => id !== markId) };
          } else {
            return { selectedMarkIds: [...state.selectedMarkIds, markId] };
          }
        }),

      selectAllMarks: (markIds) => set({ selectedMarkIds: markIds }),

      deselectAllMarks: () => set({ selectedMarkIds: [] }),

      confirmSelection: (sourcePath, marks) => {
        const selectedIds = get().selectedMarkIds;
        const selectedMarks = marks.filter((m) => m.id !== undefined && selectedIds.includes(m.id));

        if (selectedMarks.length === 0) return;

        const newGroup: CandidateGroup = {
          id: uuidv4(),
          sourcePath,
          marks: selectedMarks,
          createdAt: Date.now(),
        };

        set((state) => ({
          candidateGroups: [...state.candidateGroups, newGroup],
          selectedGroupId: newGroup.id,
          copyModeEntryPath: null,
          selectedMarkIds: [],
          sidebarCollapsed: false,
        }));
      },

      removeGroup: (groupId) =>
        set((state) => {
          const newGroups = state.candidateGroups.filter((g) => g.id !== groupId);

          return {
            candidateGroups: newGroups,
            selectedGroupId: state.selectedGroupId === groupId ? null : state.selectedGroupId,
            sidebarCollapsed: newGroups.length === 0 ? true : state.sidebarCollapsed,
          };
        }),

      removeMarkFromGroup: (groupId, markId) =>
        set((state) => {
          const newGroups = state.candidateGroups
            .map((g) => {
              if (g.id !== groupId) return g;
              const newMarks = g.marks.filter((m) => m.id !== markId);

              return { ...g, marks: newMarks };
            })
            .filter((g) => g.marks.length > 0); // Remove group if no marks left

          return {
            candidateGroups: newGroups,
            selectedGroupId: newGroups.find((g) => g.id === state.selectedGroupId)
              ? state.selectedGroupId
              : null,
            sidebarCollapsed: newGroups.length === 0 ? true : state.sidebarCollapsed,
          };
        }),

      selectGroup: (groupId) => set({ selectedGroupId: groupId }),

      toggleSidebar: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),

      setSidebarCollapsed: (collapsed) => set({ sidebarCollapsed: collapsed }),

      clearAllGroups: () =>
        set({
          candidateGroups: [],
          selectedGroupId: null,
          sidebarCollapsed: true,
        }),
    }),
    {
      name: "bakabase.copyMarks",
      partialize: (state) => ({
        candidateGroups: state.candidateGroups,
        selectedGroupId: state.selectedGroupId,
        sidebarCollapsed: state.sidebarCollapsed,
      }),
    },
  ),
);
