import type { components } from "@/sdk/BApi2";

export type MediaLibrary =
  components["schemas"]["Bakabase.Abstractions.Models.Domain.MediaLibraryV2"];

export type MediaLibraryPlayer = {
  extensions?: string[];
  executablePath: string;
  command: string;
};
