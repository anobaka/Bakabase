export interface ValueEditorProps<V = any> {
  initValue?: V;
  onChange?: (value?: V) => any;
}
