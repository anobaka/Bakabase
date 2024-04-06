import { Switch } from '@nextui-org/react';

interface SwitchProps {
  defaultSelected?: boolean;
  isSelected?: boolean;
  label?: any;
  onValueChange?: (value: boolean) => void;
  size?: 'sm' | 'md' | 'lg';
}

export default ({ label, ...otherProps }: SwitchProps) => {
  return (
    <Switch {...otherProps}>
      {label}
    </Switch>
  );
};
