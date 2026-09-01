export type ValueEditorProps<TDbValue, TBizValue = TDbValue> = {
  value?: TDbValue;
  onValueChange?: (dbValue?: TDbValue, bizValue?: TBizValue) => any;
  onCancel?: () => any;
};
