/** Entry points that expose workflow integration beside the action that produces its input. */
export const workflowIntegrationSurfaces = {
  acquisition: {
    triggerKinds: ["acquisition.requested", "acquisition.statusChanged", "resource.materialized"],
    components: ["pages/acquisition/index.tsx"],
  },
  downloader: {
    triggerKinds: ["downloader.resultReady", "downloader.completed"],
    components: ["pages/downloader/index.tsx"],
  },
  postParser: {
    triggerKinds: ["postParser.manual"],
    components: ["pages/post-parser/index.tsx"],
  },
  subscription: {
    triggerKinds: ["subscription.updated"],
    components: [
      "pages/subscription/index.tsx",
      "pages/collection/detail/components/SourceTab.tsx",
    ],
  },
  collection: {
    triggerKinds: ["collection.membersAdded"],
    components: ["pages/collection/detail/components/MembersTab.tsx"],
  },
  fileAutomation: {
    triggerKinds: ["fs.manualScan", "fs.scheduledScan", "fs.watch"],
    components: ["pages/file-name-modifier/index.tsx"],
  },
} as const;

export type WorkflowIntegrationSurface = keyof typeof workflowIntegrationSurfaces;
