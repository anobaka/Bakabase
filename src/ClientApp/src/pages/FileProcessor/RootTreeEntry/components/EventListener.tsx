import { useCallback, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';

export enum SelectionMode{
  Normal = 1,
  Ctrl = 2,
  Shift = 3,
}

type Props = {
  onSelectionModeChange: (mode: SelectionMode) => void;
  onClick?: (evt: MouseEvent) => void;
  onDelete?: () => any;
  onKeyDown?: (key: string, evt: KeyboardEvent) => any;
};

export default (props: Props) => {
  const { t } = useTranslation();
  const propsRef = useRef(props);
  const selectionModeRef = useRef<SelectionMode>(SelectionMode.Normal);

  useEffect(() => {
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('click', onClick);

    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('click', onClick);
    };
  }, []);

  const changeSelectionMode = (mode: SelectionMode) => {
    if (selectionModeRef.current != mode) {
      selectionModeRef.current = mode;
      propsRef.current.onSelectionModeChange(mode);
    }
  };

  const onClick = useCallback((evt: MouseEvent) => {
    console.log(1);
    propsRef.current.onClick?.(evt);
  }, []);

  const onKeyDown = useCallback((e: KeyboardEvent) => {
    switch (e.key) {
      case 'Control':
        changeSelectionMode(SelectionMode.Ctrl);
        break;
      case 'Shift':
        changeSelectionMode(SelectionMode.Shift);
        break;
      case 'Delete':
        propsRef.current.onDelete?.();
        break;
      default:
        propsRef.current.onKeyDown?.(e.key, e);
        break;
    }
  }, []);

  const onKeyUp = useCallback((e: KeyboardEvent) => {
    switch (e.key) {
      case 'Control':
        changeSelectionMode(SelectionMode.Normal);
        break;
      case 'Shift':
        changeSelectionMode(SelectionMode.Normal);
        break;
    }
  }, []);

  return null;
};
