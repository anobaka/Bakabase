import { Input } from '@nextui-org/react';

interface IProps {
  startContent?: React.ReactNode;
  endContent?: React.ReactNode;
  placeholder?: string;
  className?: any;
}

export default (props: IProps) => {
  return (
    <Input {...props} />
  );
};
