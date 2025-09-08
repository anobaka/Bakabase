import { create } from "zustand";

interface IconsState {
  icons: Record<string, any>;
  add: (icons: Record<string, any>) => void;
}

export const useIconsStore = create<IconsState>((set, get) => ({
  icons: {},
  add: (icons) =>
    set((state) => {
      const newIcons = { ...state.icons };

      Object.keys(icons).forEach((e) => {
        newIcons[e] = icons[e];
      });

      // console.log('newIcons', newIcons);
      return { icons: newIcons };
    }),
}));
