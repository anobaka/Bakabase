/** These activities pass a DownloadWorkset, which can contain several targets and many files. */
export const downloadWorksetExecutionLabels = {
  inputLabelKey: "workflow.counts.inputWorksets",
  outputLabelKey: "workflow.counts.outputWorksets",
  explanationKey: "workflow.counts.worksetExplanation",
};

export const postTaskExecutionLabels = {
  inputLabelKey: "workflow.counts.inputPosts",
  outputLabelKey: "workflow.counts.outputPosts",
  explanationKey: "workflow.counts.postExplanation",
};

/** Shared by processors accepting either one acquisition item or an aggregate workset. */
export const fileProcessingExecutionLabels = {
  inputLabelKey: "workflow.counts.inputProcessingItems",
  outputLabelKey: "workflow.counts.outputProcessingItems",
  explanationKey: "workflow.counts.processingExplanation",
};
