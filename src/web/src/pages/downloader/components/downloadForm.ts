export type DownloadFormState = { canSubmit: boolean; busy: boolean };

export type DownloadFormHandle = { submit: () => Promise<void> };
