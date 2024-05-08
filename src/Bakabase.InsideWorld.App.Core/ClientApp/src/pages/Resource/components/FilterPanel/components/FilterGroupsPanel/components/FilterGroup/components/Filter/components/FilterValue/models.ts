export interface FilterValueProps<V> {
  editing: boolean;
  value?: V;
  onChange?: (value: V) => any;
}

export interface FilterValueContext {
  value: any;
  renderValue: () => any;
  renderEditor: (multiple: boolean, onChange: (value: any) => any) => any;
}
