export type ValueEditorProps<V = any> = {
  initValue?: V;
  onChange?: (value?: V) => any;
  onCancel?: () => any;
};

