import { Switch } from '@nextui-org/react';

interface SwitchProps {
  defaultSelected?: boolean;
  isSelected?: boolean;
  label?: any;
  onValueChange?: (value: boolean) => void;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
}

export default ({ label, ...otherProps }: SwitchProps) => {
  return (
    <Switch {...otherProps}>
      {label}
    </Switch>
  );
};
